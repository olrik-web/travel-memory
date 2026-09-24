namespace TravelMemory.Api.Auth;

public interface ICurrentUser
{
    Guid OwnerId { get; }
}
