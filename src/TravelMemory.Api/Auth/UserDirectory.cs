using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Domain.Users;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Auth;

internal sealed class UserDirectory(TravelMemoryDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Guid> GetOrCreateOwnerIdAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken)
    {
        var existingId = await FindOwnerIdAsync(issuer, subject, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var user = User.Create(issuer, subject, timeProvider.GetUtcNow());
        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return user.Id;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A concurrent first sign-in, such as two open tabs, created the user first.
            dbContext.Entry(user).State = EntityState.Detached;
            return await FindOwnerIdAsync(issuer, subject, cancellationToken)
                ?? throw new InvalidOperationException("The user disappeared after a conflict.");
        }
    }

    private Task<Guid?> FindOwnerIdAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .Where(user => user.Issuer == issuer && user.Subject == subject)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
