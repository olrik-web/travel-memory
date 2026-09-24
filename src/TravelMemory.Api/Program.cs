using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Api.Features.PhotoImports;
using TravelMemory.Api.Features.Trips;
using TravelMemory.Persistence.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerDbContext<TravelMemoryDbContext>("travelmemory");
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureQueueServiceClient("queues");
builder.AddTravelMemoryAuthentication();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PhotoStorage>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TravelMemoryDbContext>();
    await dbContext.Database.MigrateAsync();
    var photoStorage = scope.ServiceProvider.GetRequiredService<PhotoStorage>();
    await photoStorage.InitializeAsync(
        configureDevelopmentCors: true,
        CancellationToken.None);

    app.MapOpenApi();
}

app.MapTripEndpoints();
app.MapPhotoImportEndpoints();
app.MapDefaultEndpoints();

app.UseFileServer();
app.MapFallbackToFile("/trips/{*path:nonfile}", "index.html");

app.Run();

public partial class Program;
