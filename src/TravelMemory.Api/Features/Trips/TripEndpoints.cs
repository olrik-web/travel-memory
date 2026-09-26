namespace TravelMemory.Api.Features.Trips;

internal static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var trips = endpoints.MapGroup("/api/trips")
            .RequireAuthorization()
            .WithTags("Trips");

        trips.MapPost("/", CreateTrip.HandleAsync)
            .WithName("CreateTrip")
            .ProducesValidationProblem();
        trips.MapGet("/", ListTrips.HandleAsync)
            .WithName("ListTrips");
        trips.MapGet("/{id}", GetTrip.HandleAsync)
            .WithName("GetTrip");
        trips.MapPut("/{id}", UpdateTrip.HandleAsync)
            .WithName("UpdateTrip")
            .ProducesValidationProblem();
        trips.MapDelete("/{id}", DeleteTrip.HandleAsync)
            .WithName("DeleteTrip");

        return endpoints;
    }
}
