using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.Trips;

internal static class ListTrips
{
    public static async Task<Ok<TripListResponse>> HandleAsync(
        ICurrentUser currentUser,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var trips = await dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.OwnerId == currentUser.OwnerId)
            .OrderBy(trip => trip.StartDate == null)
            .ThenByDescending(trip => trip.StartDate)
            .ThenByDescending(trip => trip.CreatedAtUtc)
            .ThenBy(trip => trip.Id)
            .Select(trip => new TripResponse(
                trip.Id,
                trip.Title,
                trip.StartDate,
                trip.EndDate,
                trip.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new TripListResponse(trips));
    }
}
