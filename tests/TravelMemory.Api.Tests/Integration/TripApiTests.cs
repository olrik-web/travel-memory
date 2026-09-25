using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using TravelMemory.Api.Features.Trips;

namespace TravelMemory.Api.Tests.Integration;

[Collection(ContainerTestCollection.Name)]
public sealed class TripApiTests : IAsyncLifetime
{
    private static readonly Guid FirstOwnerId =
        Guid.Parse("ad607b30-629e-4568-95f0-d746b6bd15ca");
    private static readonly Guid SecondOwnerId =
        Guid.Parse("2650f575-a679-410a-8f2d-3a691fc22ab4");

    private readonly MsSqlContainer sqlServer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest")
        .Build();
    private readonly AzuriteContainer azurite =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCommand("--skipApiVersionCheck")
            .Build();

    public Task InitializeAsync() =>
        Task.WhenAll(sqlServer.StartAsync(), azurite.StartAsync());

    public async Task DisposeAsync()
    {
        await sqlServer.DisposeAsync();
        await azurite.DisposeAsync();
    }

    [Fact]
    public async Task Create_list_and_get_persist_and_remain_owner_scoped()
    {
        TripResponse createdTrip;

        using (var firstFactory = CreateFactory(FirstOwnerId))
        using (var firstClient = firstFactory.CreateClient())
        {
            var createResponse = await firstClient.PostAsJsonAsync(
                "/api/trips/",
                new CreateTripRequest(
                    "Summer in Tuscany",
                    new DateOnly(2026, 7, 4),
                    new DateOnly(2026, 7, 14)));

            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            createdTrip = Assert.IsType<TripResponse>(
                await createResponse.Content.ReadFromJsonAsync<TripResponse>());
            Assert.Equal($"/api/trips/{createdTrip.Id}", createResponse.Headers.Location?.OriginalString);
        }

        using (var restartedFactory = CreateFactory(FirstOwnerId))
        using (var restartedClient = restartedFactory.CreateClient())
        {
            var list = Assert.IsType<TripListResponse>(
                await restartedClient.GetFromJsonAsync<TripListResponse>("/api/trips/"));
            var persistedTrip = Assert.Single(list.Items);
            Assert.Equal(createdTrip.Id, persistedTrip.Id);

            var openedTrip = Assert.IsType<TripResponse>(
                await restartedClient.GetFromJsonAsync<TripResponse>(
                    $"/api/trips/{createdTrip.Id}"));
            Assert.Equal("Summer in Tuscany", openedTrip.Title);
        }

        using (var secondFactory = CreateFactory(SecondOwnerId))
        using (var secondClient = secondFactory.CreateClient())
        {
            var secondOwnerList = Assert.IsType<TripListResponse>(
                await secondClient.GetFromJsonAsync<TripListResponse>("/api/trips/"));
            Assert.Empty(secondOwnerList.Items);

            var hiddenTripResponse = await secondClient.GetAsync($"/api/trips/{createdTrip.Id}");
            Assert.Equal(HttpStatusCode.NotFound, hiddenTripResponse.StatusCode);
        }
    }

    [Fact]
    public async Task Create_returns_validation_problem_for_an_invalid_date_range()
    {
        using var factory = CreateFactory(FirstOwnerId);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/trips/",
            new CreateTripRequest(
                "Reversed trip",
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 10)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = Assert.IsType<HttpValidationProblemDetails>(
            await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>());
        Assert.Contains("endDate", problem.Errors.Keys);
    }

    private TravelMemoryApplicationFactory CreateFactory(Guid ownerId) =>
        new(sqlServer.GetConnectionString(), azurite.GetConnectionString(), ownerId);
}
