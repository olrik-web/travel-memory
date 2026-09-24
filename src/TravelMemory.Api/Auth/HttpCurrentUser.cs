using System.Security.Claims;

namespace TravelMemory.Api.Auth;

internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid OwnerId
    {
        get
        {
            var ownerIdValue = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(ownerIdValue, out var ownerId) || ownerId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("The authenticated user has no valid owner identifier.");
            }

            return ownerId;
        }
    }
}
