using TravelMemory.Domain.Trips;

namespace TravelMemory.Api.Tests.Domain;

public sealed class TripTests
{
    private static readonly Guid OwnerId = Guid.Parse("7856c148-8fd0-4df8-bd96-a3b9127fbadd");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Create_normalizes_title_and_timestamp()
    {
        var trip = Trip.Create(
            OwnerId,
            "  Summer in Tuscany  ",
            new DateOnly(2026, 7, 4),
            new DateOnly(2026, 7, 14),
            CreatedAt);

        Assert.Equal("Summer in Tuscany", trip.Title);
        Assert.Equal(TimeSpan.Zero, trip.CreatedAtUtc.Offset);
        Assert.Equal(CreatedAt.ToUniversalTime(), trip.CreatedAtUtc);
    }

    [Fact]
    public void Create_accepts_missing_dates()
    {
        var trip = Trip.Create(OwnerId, "A trip without dates", null, null, CreatedAt);

        Assert.Null(trip.StartDate);
        Assert.Null(trip.EndDate);
    }

    [Fact]
    public void Create_accepts_an_end_date_without_a_start_date()
    {
        var endDate = new DateOnly(2026, 7, 14);

        var trip = Trip.Create(OwnerId, "A trip with an unknown start", null, endDate, CreatedAt);

        Assert.Null(trip.StartDate);
        Assert.Equal(endDate, trip.EndDate);
    }

    [Fact]
    public void Create_accepts_the_same_start_and_end_date()
    {
        var date = new DateOnly(2026, 7, 14);

        var trip = Trip.Create(OwnerId, "Dagstur", date, date, CreatedAt);

        Assert.Equal(date, trip.StartDate);
        Assert.Equal(date, trip.EndDate);
    }

    [Fact]
    public void Create_rejects_an_end_date_before_the_start_date()
    {
        var exception = Assert.Throws<TripValidationException>(
            () => Trip.Create(
                OwnerId,
                "Reversed trip",
                new DateOnly(2026, 7, 14),
                new DateOnly(2026, 7, 4),
                CreatedAt));

        Assert.Contains("endDate", exception.Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_title(string? title)
    {
        var exception = Assert.Throws<TripValidationException>(
            () => Trip.Create(OwnerId, title, null, null, CreatedAt));

        Assert.Contains("title", exception.Errors.Keys);
    }

    [Fact]
    public void Create_rejects_a_title_over_the_maximum_length()
    {
        var exception = Assert.Throws<TripValidationException>(
            () => Trip.Create(
                OwnerId,
                new string('a', Trip.MaxTitleLength + 1),
                null,
                null,
                CreatedAt));

        Assert.Contains("title", exception.Errors.Keys);
    }
}
