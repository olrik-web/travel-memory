namespace TravelMemory.Domain.Trips;

public sealed class Trip
{
    public const int MaxTitleLength = 200;

    private Trip()
    {
    }

    private Trip(
        Guid id,
        Guid ownerId,
        string title,
        DateOnly? startDate,
        DateOnly? endDate,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerId = ownerId;
        Title = title;
        StartDate = startDate;
        EndDate = endDate;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Trip Create(
        Guid ownerId,
        string? title,
        DateOnly? startDate,
        DateOnly? endDate,
        DateTimeOffset createdAt)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("OwnerId cannot be empty.", nameof(ownerId));
        }

        var errors = Validate(title, startDate, endDate);
        if (errors.Count > 0)
        {
            throw new TripValidationException(errors);
        }

        return new Trip(
            Guid.NewGuid(),
            ownerId,
            title!.Trim(),
            startDate,
            endDate,
            createdAt.ToUniversalTime());
    }

    private static Dictionary<string, string[]> Validate(
        string? title,
        DateOnly? startDate,
        DateOnly? endDate)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var trimmedTitle = title?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            errors["title"] = ["Enter a title for the trip."];
        }
        else if (trimmedTitle.Length > MaxTitleLength)
        {
            errors["title"] = [$"The title can be at most {MaxTitleLength} characters."];
        }

        if (startDate.HasValue && endDate.HasValue && endDate < startDate)
        {
            errors["endDate"] = ["The end date cannot be before the start date."];
        }

        return errors;
    }
}

public sealed class TripValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Trip validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
