using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;

namespace TravelMemory.Api.Features.Auth;

internal static class SignIn
{
    public static ChallengeHttpResult Handle(string? returnUrl) =>
        TypedResults.Challenge(
            new AuthenticationProperties { RedirectUri = ToLocalUrl(returnUrl) },
            [OpenIdConnectDefaults.AuthenticationScheme]);

    // Only same-site paths are allowed, so the sign-in flow cannot be used as an open
    // redirect to another site ("//host" and "/\host" are protocol-relative in browsers).
    // Control characters are rejected too, because browsers strip tabs and newlines from
    // URLs, which turns "/\t/host" into "//host".
    internal static string ToLocalUrl(string? returnUrl) =>
        returnUrl is ['/', not '/' and not '\\', ..] or "/" && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : "/";
}
