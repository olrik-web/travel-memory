using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;

namespace TravelMemory.Api.Features.Auth;

internal static class SignOut
{
    // Signing out deletes the session cookie in the response, and SameSite=Lax does not stop
    // a response to a cross-site form POST from doing that. Such a request arrives without
    // the cookie, though, so only a request that carries a session signs out; anything else
    // goes home, which keeps other sites from signing the user out.
    public static Results<SignOutHttpResult, RedirectHttpResult> Handle(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? TypedResults.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    OpenIdConnectDefaults.AuthenticationScheme,
                ])
            : TypedResults.Redirect("/");
}
