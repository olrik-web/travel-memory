using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TravelMemory.Api.Auth;

internal sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<DevelopmentIdentityOptions> identityOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string SchemeName = "Development";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ownerId = identityOptions.Value.OwnerId.ToString();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, ownerId),
            new Claim("sub", ownerId),
            new Claim(ClaimTypes.Name, "Local Travel Memory owner"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

internal sealed class DevelopmentIdentityOptions
{
    public Guid OwnerId { get; set; }
}
