using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TravelMemory.Api.Tests.Integration;

// Stands in for the OIDC cookie session: every request is signed in as the configured
// owner, with the owner id claim that sign-in adds after resolving the user.
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<TestAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<TestAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Options.OwnerId.ToString()),
                new Claim(ClaimTypes.Name, "Test user"),
            ],
            SchemeName);
        return Task.FromResult(
            AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

internal sealed class TestAuthenticationOptions : AuthenticationSchemeOptions
{
    public Guid OwnerId { get; set; }
}
