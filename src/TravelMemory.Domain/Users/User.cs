namespace TravelMemory.Domain.Users;

// A person who signs in through an external identity provider. The internal Id is the
// OwnerId on all their data, so moving to another provider only means remapping the
// issuer and subject, never rewriting owned rows.
public sealed class User
{
    public const int MaxIssuerLength = 256;
    public const int MaxSubjectLength = 256;

    private User()
    {
    }

    private User(Guid id, string issuer, string subject, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Issuer = issuer;
        Subject = subject;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Issuer { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static User Create(string issuer, string subject, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > MaxIssuerLength)
        {
            throw new ArgumentException(
                $"Issuer must be between 1 and {MaxIssuerLength} characters.",
                nameof(issuer));
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength)
        {
            throw new ArgumentException(
                $"Subject must be between 1 and {MaxSubjectLength} characters.",
                nameof(subject));
        }

        return new User(Guid.NewGuid(), issuer, subject, createdAt.ToUniversalTime());
    }
}
