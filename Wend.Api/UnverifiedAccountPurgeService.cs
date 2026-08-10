using Wend.Core;

namespace Wend.Api;

/// <summary>
/// Runs the unverified-account purge on a slow timer. Thin by design: the query it calls is in
/// Wend.Core and is what the tests exercise.
/// </summary>
public sealed class UnverifiedAccountPurgeService(
    IServiceScopeFactory scopes,
    ILogger<UnverifiedAccountPurgeService> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        // PeriodicTimer waits a full interval BEFORE its first tick. That is deliberate: every API
        // test boots this app and disposes it seconds later, so with a 6-hour interval the purge
        // never touches a test's throwaway database. Do not add an immediate first run.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();
                var cutoff = DateTime.UtcNow - UnverifiedAccounts.Window;
                var removed = await UnverifiedAccounts.PurgeAsync(db, cutoff, stoppingToken);
                if (removed > 0) log.LogInformation("Purged {Count} unverified accounts.", removed);
            }
            catch (Exception ex)
            {
                // A failed sweep must not take the web app down; the next tick tries again.
                log.LogError(ex, "Unverified-account purge failed.");
            }
        }
    }
}
