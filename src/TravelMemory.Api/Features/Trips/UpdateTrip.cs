using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Trips;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.Trips;

internal static class UpdateTrip
{
    public static async Task<Results<Ok<TripResponse>, NotFound, ValidationProblem>> HandleAsync(
        Guid id,
        UpdateTripRequest request,
        ICurrentUser currentUser,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var trip = await dbContext.Trips.SingleOrDefaultAsync(
            value => value.Id == id && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (trip is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            trip.Update(request.Title, request.StartDate, request.EndDate);
        }
        catch (TripValidationException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(TripResponse.From(trip));
    }
}
