using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TravelMemory.EndToEndTests;

// Starts the real resource graph: SQL Server, Azurite, Keycloak, the API, the worker, and
// the Vite dev server. It needs Docker, the web app's npm packages, and free fixed ports.
public sealed class PhotoImportEndToEndTests
{
    private static readonly Uri WebOrigin = new("http://localhost:5173");
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);
    // All SQL Server starts together fit in the startup timeout (3 × 3 of 10 minutes), so
    // giving up on SQL Server is reported before the startup as a whole times out.
    private static readonly TimeSpan SqlServerStartTimeout = TimeSpan.FromMinutes(3);
    private const int MaxSqlServerStarts = 3;
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Signs_in_creates_a_trip_and_imports_a_photo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Keycloak's issuer and the realm's allowed redirect URI depend on the AppHost's
        // fixed ports, so the testing builder must not randomize them. Without data volumes
        // the run starts empty and leaves nothing in the local development data.
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TravelMemory_AppHost>(
                ["DcpPublisher:RandomizePorts=false", "TravelMemory:UseDataVolumes=false"],
                cancellationToken);
        // By default a resource gives up when a dependency stops, so the API would fail to
        // start while SQL Server is being restarted below; keep it waiting instead.
        builder.Services.Configure<ResourceNotificationServiceOptions>(options =>
            options.DefaultWaitBehavior = WaitBehavior.WaitOnResourceUnavailable);
        await using var app = await builder.BuildAsync(cancellationToken);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(StartupTimeout);
        // Starting waits for SQL Server, so it is watched, and restarted if needed, while
        // the application starts rather than afterwards. If the watcher gives up, it stops
        // the start as well, and its reason is what the test reports.
        var sqlServerStarted = WatchSqlServerAsync(app, startup);
        try
        {
            await app.StartAsync(startup.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await sqlServerStarted;
            throw;
        }

        await sqlServerStarted;
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", startup.Token);
        await app.ResourceNotifications.WaitForResourceAsync(
            "worker",
            KnownResourceStates.Running,
            startup.Token);
        await app.ResourceNotifications.WaitForResourceAsync(
            "web",
            KnownResourceStates.Running,
            startup.Token);

        using var browser = new BrowserSession(WebOrigin);
        await WaitForDevServerAsync(browser, startup.Token);

        using (var landing = await browser.SignInAsync("alice", "alice", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, landing.StatusCode);
            Assert.Equal(new Uri(WebOrigin, "/trips"), landing.RequestMessage?.RequestUri);
        }

        using (var me = await browser.GetAsync("/api/auth/me", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        var trip = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/trips/",
            new { title = "End-to-end trip", startDate = (string?)null, endDate = (string?)null },
            HttpStatusCode.Created,
            cancellationToken);
        var tripId = trip["id"]!.GetValue<string>();

        var photo = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "oriented.jpg"),
            cancellationToken);
        var batch = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            $"/api/trips/{tripId}/photo-imports",
            new
            {
                clientBatchId = Guid.NewGuid(),
                files = new[]
                {
                    new
                    {
                        clientFileId = Convert.ToHexStringLower(SHA256.HashData(photo)),
                        fileName = "oriented.jpg",
                        contentType = "image/jpeg",
                        sizeBytes = photo.LongLength,
                    },
                },
            },
            HttpStatusCode.Created,
            cancellationToken);
        var batchId = batch["id"]!.GetValue<string>();
        var item = batch["items"]![0]!;
        await UploadToBlobStorageAsync(
            item["uploadUrl"]!.GetValue<string>(),
            photo,
            cancellationToken);

        using (var completion = await browser.PostAsync(
            $"/api/photo-imports/{batchId}/items/{item["id"]}/complete-upload",
            content: null,
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, completion.StatusCode);
        }

        await WaitForBatchStateAsync(browser, batchId, "ReadyForReview", cancellationToken);
        await SendJsonAsync(
            browser,
            HttpMethod.Post,
            $"/api/photo-imports/{batchId}/finalize",
            new { timeAdjustmentMinutes = 0 },
            HttpStatusCode.Accepted,
            cancellationToken);
        await WaitForBatchStateAsync(browser, batchId, "Completed", cancellationToken);

        using var timelineResponse = await browser.GetAsync(
            $"/api/trips/{tripId}/photos",
            cancellationToken);
        var timeline = JsonNode.Parse(
            await timelineResponse.Content.ReadAsStringAsync(cancellationToken))!;
        var imported = Assert.Single(timeline["items"]!.AsArray())!;
        using var anonymous = new HttpClient();
        using var derivative = await anonymous.GetAsync(
            imported["webUrl"]!.GetValue<string>(),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, derivative.StatusCode);
        Assert.Equal("image/jpeg", derivative.Content.Headers.ContentType?.MediaType);
    }

    // SQL Server 2025 sometimes crashes while its container starts on CI runners (see
    // SqlServerFixture). Everything else waits for it, so restart it a few times instead
    // of waiting for the whole startup timeout, and name it when it keeps failing.
    // With dependents waiting on SQL Server, nothing else would stop the start when the
    // watcher gives up, so it cancels the start before reporting why.
    private static async Task WatchSqlServerAsync(
        DistributedApplication app,
        CancellationTokenSource startup)
    {
        try
        {
            await WaitForSqlServerAsync(app, startup.Token);
        }
        catch when (!startup.IsCancellationRequested)
        {
            await startup.CancelAsync();
            throw;
        }
    }

    private static async Task WaitForSqlServerAsync(
        DistributedApplication app,
        CancellationToken cancellationToken)
    {
        for (var start = 1; ; start++)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(SqlServerStartTimeout);
            string? stoppedState = null;
            try
            {
                // A restart first stops the resource, which reports Exited again, so a later
                // start is only judged once its container is running.
                if (start > 1)
                {
                    await app.ResourceNotifications.WaitForResourceAsync(
                        "sql",
                        KnownResourceStates.Running,
                        attempt.Token);
                }

                var resourceEvent = await app.ResourceNotifications.WaitForResourceAsync(
                    "sql",
                    resourceEvent =>
                        resourceEvent.Snapshot.HealthStatus == HealthStatus.Healthy
                        || KnownResourceStates.TerminalStates.Contains(
                            resourceEvent.Snapshot.State?.Text),
                    attempt.Token);
                if (resourceEvent.Snapshot.HealthStatus == HealthStatus.Healthy)
                {
                    return;
                }

                stoppedState = resourceEvent.Snapshot.State?.Text;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                stoppedState = $"not healthy after {SqlServerStartTimeout.TotalMinutes} minutes";
            }

            if (start == MaxSqlServerStarts)
            {
                Assert.Fail($"SQL Server did not start after {start} attempts; last state: {stoppedState}.");
            }

            TestContext.Current.SendDiagnosticMessage(
                $"SQL Server start {start} failed ({stoppedState}); restarting it.");
            var restart = await app.ResourceCommands.ExecuteCommandAsync(
                "sql",
                KnownResourceCommands.RestartCommand,
                cancellationToken);
            Assert.True(
                restart.Success,
                $"SQL Server could not be restarted: {restart.Message}");
        }
    }

    // Vite reports Running before it serves requests, and its proxy is the only way in.
    private static async Task WaitForDevServerAsync(
        BrowserSession browser,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                using var response = await browser.GetAsync("/", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static async Task<JsonNode> SendJsonAsync(
        BrowserSession browser,
        HttpMethod method,
        string path,
        object body,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken)
    {
        using var response = await browser.SendAsync(
            new HttpRequestMessage(method, new Uri(browser.Origin, path))
            {
                Content = JsonContent.Create(body),
            },
            cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(
            response.StatusCode == expectedStatus,
            $"{method} {path} returned {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!;
    }

    private static async Task UploadToBlobStorageAsync(
        string uploadUrl,
        byte[] content,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(content),
        };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task WaitForBatchStateAsync(
        BrowserSession browser,
        string batchId,
        string state,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(ProcessingTimeout);
        while (true)
        {
            using var response = await browser.GetAsync(
                $"/api/photo-imports/{batchId}",
                cancellationToken);
            var batch = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
            var current = batch["state"]!.GetValue<string>();
            if (current == state)
            {
                return;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail($"Batch {batchId} is still {current}, expected {state}: {batch}");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}
