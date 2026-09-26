using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using TravelMemory.Api.Auth;
using TravelMemory.Api.Features.Auth;

namespace TravelMemory.IntegrationTests.Integration;

[Collection(ContainerTestCollection.Name)]
public sealed class AuthApiTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private const string Issuer = "https://identity.test/realms/travel-memory";

    private readonly string databaseConnectionString =
        sqlServer.CreateDatabaseConnectionString();
    private readonly AzuriteContainer azurite =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCommand("--skipApiVersionCheck")
            .Build();

    public async ValueTask InitializeAsync() => await azurite.StartAsync();

    public ValueTask DisposeAsync() => azurite.DisposeAsync();

    [Fact]
    public async Task Answers_api_calls_without_a_session_with_401()
    {
        using var factory = CreateFactory(ownerId: null);
        using var client = factory.CreateClient(
            new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/trips/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ignores_a_sign_out_without_a_session()
    {
        using var factory = CreateFactory(ownerId: null);
        using var client = factory.CreateClient(
            new() { AllowAutoRedirect = false });

        // A cross-site form POST arrives without the SameSite=Lax cookie. Its response must
        // not delete the cookie, or another site could sign the user out.
        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Returns_the_signed_in_user()
    {
        using var factory = CreateFactory(Guid.NewGuid());
        using var client = factory.CreateClient();

        var user = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        Assert.Equal("Test user", Assert.IsType<CurrentUserResponse>(user).Name);
    }

    [Fact]
    public async Task Maps_each_provider_subject_to_one_stable_owner()
    {
        using var factory = CreateFactory(ownerId: null);
        // Creating a client starts the app, which applies the migrations.
        using var client = factory.CreateClient();

        var alice = await GetOrCreateOwnerIdAsync(factory, Issuer, "alice");
        var aliceAgain = await GetOrCreateOwnerIdAsync(factory, Issuer, "alice");
        var bob = await GetOrCreateOwnerIdAsync(factory, Issuer, "bob");
        var aliceElsewhere = await GetOrCreateOwnerIdAsync(
            factory,
            "https://other-identity.test",
            "alice");

        Assert.Equal(alice, aliceAgain);
        Assert.NotEqual(alice, bob);
        Assert.NotEqual(alice, aliceElsewhere);
    }

    [Fact]
    public async Task Resolves_concurrent_first_sign_ins_to_the_same_owner()
    {
        using var factory = CreateFactory(ownerId: null);
        using var client = factory.CreateClient();

        var ownerIds = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                GetOrCreateOwnerIdAsync(factory, Issuer, "two-tabs")));

        Assert.Single(ownerIds.Distinct());
    }

    private static async Task<Guid> GetOrCreateOwnerIdAsync(
        TravelMemoryApplicationFactory factory,
        string issuer,
        string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<UserDirectory>()
            .GetOrCreateOwnerIdAsync(issuer, subject, CancellationToken.None);
    }

    private TravelMemoryApplicationFactory CreateFactory(Guid? ownerId) =>
        new(databaseConnectionString, azurite.GetConnectionString(), ownerId);
}
