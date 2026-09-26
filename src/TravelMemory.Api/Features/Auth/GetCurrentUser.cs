using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

namespace TravelMemory.Api.Features.Auth;

internal static class GetCurrentUser
{
    public static Ok<CurrentUserResponse> Handle(ClaimsPrincipal user) =>
        TypedResults.Ok(new CurrentUserResponse(user.Identity?.Name ?? "Signed in"));
}
