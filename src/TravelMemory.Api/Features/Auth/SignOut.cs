using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;

namespace TravelMemory.Api.Features.Auth;

internal static class SignOut
{
    // POST only: with a SameSite=Lax cookie, another site cannot sign the user out.
    public static SignOutHttpResult Handle() =>
        TypedResults.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme,
            ]);
}
