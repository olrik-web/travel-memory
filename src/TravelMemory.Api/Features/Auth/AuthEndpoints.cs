namespace TravelMemory.Api.Features.Auth;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth")
            .WithTags("Auth");

        // Browser navigations rather than API calls, so they stay out of the OpenAPI document.
        auth.MapGet("/login", SignIn.Handle)
            .ExcludeFromDescription();
        auth.MapPost("/logout", SignOut.Handle)
            .ExcludeFromDescription();
        auth.MapGet("/me", GetCurrentUser.Handle)
            .RequireAuthorization()
            .WithName("GetCurrentUser");

        return endpoints;
    }
}
