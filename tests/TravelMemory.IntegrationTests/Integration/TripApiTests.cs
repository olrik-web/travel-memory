using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;
using TravelMemory.Api.Features.Trips;

namespace TravelMemory.IntegrationTests.Integration;

[Collection(ContainerTestCollection.Name)]
public sealed class TripApiTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private static readonly Guid FirstOwnerId =
        Guid.Parse("ad607b30-629e-4568-95f0-d746b6bd15ca");
    private static readonly Guid SecondOwnerId =
        Guid.Parse("2650f575-a679-410a-8f2d-3a691fc22ab4");

    private readonly string databaseConnectionString =
        sqlServer.CreateDatabaseConnectionString();
    private readonly AzuriteContainer azurite =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCommand("--skipApiVersionCheck")
            .Build();

    public async ValueTask InitializeAsync() => await azurite.StartAsync();

    public ValueTask DisposeAsync() => azurite.DisposeAsync();

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
    public async Task Update_changes_an_owned_trip_and_hides_others()
    {
        TripResponse trip;
        using (var ownerFactory = CreateFactory(FirstOwnerId))
        using (var ownerClient = ownerFactory.CreateClient())
        {
            var created = await ownerClient.PostAsJsonAsync(
                "/api/trips/",
                new CreateTripRequest("Poland", null, null));
            trip = Assert.IsType<TripResponse>(
                await created.Content.ReadFromJsonAsync<TripResponse>());

            var updated = await ownerClient.PutAsJsonAsync(
                $"/api/trips/{trip.Id}",
                new UpdateTripRequest(
                    "Summer in Poland",
                    new DateOnly(2026, 7, 11),
                    new DateOnly(2026, 7, 21)));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            var opened = Assert.IsType<TripResponse>(
                await ownerClient.GetFromJsonAsync<TripResponse>($"/api/trips/{trip.Id}"));
            Assert.Equal("Summer in Poland", opened.Title);
            Assert.Equal(new DateOnly(2026, 7, 11), opened.StartDate);
            Assert.Equal(new DateOnly(2026, 7, 21), opened.EndDate);

            var invalid = await ownerClient.PutAsJsonAsync(
                $"/api/trips/{trip.Id}",
                new UpdateTripRequest(
                    "Summer in Poland",
                    new DateOnly(2026, 7, 21),
                    new DateOnly(2026, 7, 11)));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using var otherFactory = CreateFactory(SecondOwnerId);
        using var otherClient = otherFactory.CreateClient();
        var hidden = await otherClient.PutAsJsonAsync(
            $"/api/trips/{trip.Id}",
            new UpdateTripRequest("Taken over", null, null));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
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

    [Fact]
    public async Task Forbids_a_user_without_a_valid_owner_identifier()
    {
        using var factory = new TravelMemoryApplicationFactory(
            databaseConnectionString,
            azurite.GetConnectionString(),
            FirstOwnerId,
            services => services
                .AddAuthentication(OwnerlessAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, OwnerlessAuthenticationHandler>(
                    OwnerlessAuthenticationHandler.SchemeName,
                    _ => { }));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/trips/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private TravelMemoryApplicationFactory CreateFactory(Guid ownerId) =>
        new(databaseConnectionString, azurite.GetConnectionString(), ownerId);
}

// Authenticates a user whose identity has no usable owner id, like a token from an
// identity provider that lacks the expected subject claim.
internal sealed class OwnerlessAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Ownerless";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")],
            SchemeName);
        return Task.FromResult(
            AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
