using Microsoft.AspNetCore.Http.HttpResults;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Trips;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.Trips;

internal static class CreateTrip
{
    public static async Task<Results<Created<TripResponse>, ValidationProblem>> HandleAsync(
        CreateTripRequest request,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Trip trip;

        try
        {
            trip = Trip.Create(
                currentUser.OwnerId,
                request.Title,
                request.StartDate,
                request.EndDate,
                timeProvider.GetUtcNow());
        }
        catch (TripValidationException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal));
        }

        dbContext.Trips.Add(trip);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/trips/{trip.Id}", TripResponse.From(trip));
    }
}
