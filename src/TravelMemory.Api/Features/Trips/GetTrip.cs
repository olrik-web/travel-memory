using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.Trips;

internal static class GetTrip
{
    public static async Task<Results<Ok<TripResponse>, NotFound>> HandleAsync(
        Guid id,
        ICurrentUser currentUser,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var trip = await dbContext.Trips
            .AsNoTracking()
            .SingleOrDefaultAsync(
                trip => trip.Id == id && trip.OwnerId == currentUser.OwnerId,
                cancellationToken);

        return trip is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TripResponse.From(trip));
    }
}
