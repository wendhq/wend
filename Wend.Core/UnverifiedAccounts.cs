using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>
/// Registration creates an account that cannot log in. Left alone, one bot registration would hold
/// an address forever; purging returns it to whoever actually owns the mailbox.
///
/// The query is kept separate from the hosted service that calls it so it can be tested directly,
/// against real PostgreSQL, without waiting on a timer.
/// </summary>
public static class UnverifiedAccounts
{
    /// <summary>
    /// How long an account may sit unconfirmed before its address is released. Lives here rather
    /// than on the hosted service so the tests can pin the boundary to the same constant the
    /// production sweep uses — otherwise changing the window leaves the tests green while they
    /// measure a boundary nothing else has.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public static Task<int> PurgeAsync(WendDbContext db, DateTime cutoffUtc, CancellationToken ct = default) =>
        db.Users
            .Where(u => !u.EmailConfirmed && u.CreatedAt < cutoffUtc)
            .ExecuteDeleteAsync(ct);
}
