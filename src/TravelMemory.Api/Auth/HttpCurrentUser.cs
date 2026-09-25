using System.Security.Claims;

namespace TravelMemory.Api.Auth;

internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid OwnerId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user is null || !TryGetOwnerId(user, out var ownerId))
            {
                throw new UnauthorizedAccessException("The authenticated user has no valid owner identifier.");
            }

            return ownerId;
        }
    }

    public static bool TryGetOwnerId(ClaimsPrincipal user, out Guid ownerId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out ownerId)
        && ownerId != Guid.Empty;
}
