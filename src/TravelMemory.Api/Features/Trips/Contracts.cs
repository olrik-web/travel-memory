using TravelMemory.Domain.Trips;

namespace TravelMemory.Api.Features.Trips;

public sealed record CreateTripRequest(
    string? Title,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record UpdateTripRequest(
    string? Title,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record TripResponse(
    Guid Id,
    string Title,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateTimeOffset CreatedAtUtc)
{
    public static TripResponse From(Trip trip) =>
        new(trip.Id, trip.Title, trip.StartDate, trip.EndDate, trip.CreatedAtUtc);
}

public sealed record TripListResponse(IReadOnlyList<TripResponse> Items);
