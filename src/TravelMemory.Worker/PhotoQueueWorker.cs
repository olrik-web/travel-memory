using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Worker;

internal sealed class PhotoQueueWorker(
    IServiceScopeFactory scopeFactory,
    BlobServiceClient blobServiceClient,
    QueueServiceClient queueServiceClient,
    PhotoMaintenance maintenance,
    TimeProvider timeProvider,
    ILogger<PhotoQueueWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FailedCycleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(30);
    private const int MaximumConcurrency = 4;
    private const int MaximumDequeueCount = 5;
    private DateTimeOffset nextMaintenanceAtUtc = DateTimeOffset.MinValue;

    private QueueClient Queue =>
        queueServiceClient.GetQueueClient(PhotoStorageNames.ProcessingQueue);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeStorageAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // An exception escaping ExecuteAsync stops the whole host, so a failing cycle,
            // such as a SQL or storage outage, is logged and the loop tries again shortly.
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Photo queue worker cycle failed; retrying shortly.");
                await Task.Delay(FailedCycleDelay, stoppingToken);
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        await RunMaintenanceWhenDueAsync(stoppingToken);
        var messages = (await Queue.ReceiveMessagesAsync(
            maxMessages: 8,
            visibilityTimeout: VisibilityTimeout,
            cancellationToken: stoppingToken)).Value;

        if (messages.Length == 0)
        {
            await Task.Delay(EmptyQueueDelay, stoppingToken);
            return;
        }

        await Parallel.ForEachAsync(
            messages,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaximumConcurrency,
                CancellationToken = stoppingToken,
            },
            ProcessMessageAsync);
    }

    private async ValueTask ProcessMessageAsync(
        QueueMessage message,
        CancellationToken cancellationToken)
    {
        // One failing message must not cancel the others in the same batch or stop the
        // worker. The message stays in the queue and becomes visible again after the
        // visibility timeout, so the dequeue limit below bounds how often it is retried.
        try
        {
            await HandleMessageAsync(message, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Unexpected error while handling photo queue message {MessageId}; it will be retried after the visibility timeout.",
                message.MessageId);
        }
    }

    private async Task HandleMessageAsync(
        QueueMessage message,
        CancellationToken cancellationToken)
    {
        // Deleting a poison message loses no work: the database job is authoritative, and a
        // job that is still pending is dispatched again with a fresh message.
        if (message.DequeueCount > MaximumDequeueCount)
        {
            logger.LogError(
                "Discarding photo queue message {MessageId} after {DequeueCount} delivery attempts: {MessageText}",
                message.MessageId,
                message.DequeueCount,
                message.MessageText);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
            return;
        }

        PhotoQueueMessage? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PhotoQueueMessage>(message.MessageText);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Discarding malformed photo queue message {MessageId}.", message.MessageId);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
            return;
        }

        if (payload is null || payload.JobId == Guid.Empty)
        {
            logger.LogError("Discarding photo queue message {MessageId} without a job id.", message.MessageId);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
            return;
        }

        // Continues the trace of the operation that created the job, such as the API request
        // that completed the upload. Without a valid trace parent, the span starts a new trace.
        ActivityContext.TryParse(payload.TraceParent, traceState: null, out var parent);
        using var activity = WorkerTelemetry.ActivitySource.StartActivity(
            "receive photo job",
            ActivityKind.Consumer,
            parent);
        activity?.SetTag("messaging.system", "azure_storage_queue");
        activity?.SetTag("messaging.destination.name", PhotoStorageNames.ProcessingQueue);
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("messaging.azure_storage_queue.dequeue_count", message.DequeueCount);
        activity?.SetTag("travelmemory.photo_job.id", payload.JobId);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<PhotoJobProcessor>();
            await processor.ProcessAsync(payload.JobId, cancellationToken);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(
                exception,
                "Photo job {JobId} was claimed by another worker.",
                payload.JobId);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
        }
        catch (RequestFailedException exception) when (
            exception.Status is 408 or 429 or >= 500)
        {
            logger.LogWarning(
                exception,
                "Transient storage error while processing photo job {JobId}; the queue message remains visible for retry.",
                payload.JobId);
        }
    }

    // The maintenance cycle also runs once a day as a scheduled job in Azure, where the
    // worker scales to zero between imports. While the worker is awake, running it every
    // few seconds dispatches the cleanup jobs and retries it creates without delay.
    private async Task RunMaintenanceWhenDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now < nextMaintenanceAtUtc)
        {
            return;
        }

        nextMaintenanceAtUtc = now.Add(MaintenanceInterval);
        await maintenance.RunAsync(cancellationToken);
    }

    private async Task InitializeStorageAsync(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
            .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer)
            .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await Queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }
}
