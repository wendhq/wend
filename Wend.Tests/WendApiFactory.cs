using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wend.Api;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// Boots the real app against a throwaway PostgreSQL database on the local/CI server. Each factory
/// instance creates its OWN empty database and drops it on dispose, so tests stay isolated exactly
/// as they were with per-test SQLite files. The app builds the schema on startup (Migrate).
///
/// The app registers NullCurrentUser, which would make every /api/* call 401, so tests override
/// ICurrentUser with a mutable TestCurrentUser and act as DefaultUserId by default.
/// </summary>
public sealed class WendApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"wend_test_{Guid.NewGuid():N}";

    /// <summary>The user every API test acts as by default. Boards created over HTTP belong to it.</summary>
    public string DefaultUserId { get; } = Guid.NewGuid().ToString();

    /// <summary>Swap UserId to act as somebody else (or null for anonymous) inside a test.</summary>
    public TestCurrentUser CurrentUser { get; } = new();

    /// <summary>Captured outbound auth email. Assert on Email.Sent instead of reading a log file.</summary>
    public FakeAuthEmailSender Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Both of Program.cs's environment-guarded branches must take their Development path here.
        builder.UseEnvironment("Development");

        // Create this instance's empty database on the shared server.
        using (var admin = new NpgsqlConnection(DatabaseFixture.AdminConnectionString))
        {
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_dbName}\"";
            cmd.ExecuteNonQuery();
        }

        // Point the app at it through the same seam Program.cs reads.
        var perTest = new NpgsqlConnectionStringBuilder(DatabaseFixture.AdminConnectionString)
        {
            Database = _dbName,
        };
        builder.UseSetting("ConnectionStrings:WendDb", perTest.ConnectionString);

        // Tests supply their own current user; the app's NullCurrentUser would make everything 401.
        // They also swap the file-writing dev sender for one that records in memory.
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ICurrentUser>(_ => CurrentUser);
            services.AddSingleton<IAuthEmailSender>(Email);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);

        // First client creation boots the app; seed the default user and start acting as them.
        // NOTE: this runs on EVERY CreateClient() call, so create the client once and switch
        // CurrentUser.UserId afterwards — calling CreateClient() again silently reverts to the
        // default user, and an isolation test would then pass for the wrong reason.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();
        if (!db.Users.Any(u => u.Id == DefaultUserId))
        {
            db.Users.Add(new WendUser
            {
                Id = DefaultUserId,
                UserName = "default@example.test",
                NormalizedUserName = "DEFAULT@EXAMPLE.TEST",
                Email = "default@example.test",
                NormalizedEmail = "DEFAULT@EXAMPLE.TEST",
                DisplayName = "Default Test User",
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            db.SaveChanges();
        }
        CurrentUser.UserId = DefaultUserId;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Native server persists (unlike a container) — drop this test's throwaway database.
        // DROP ... WITH (FORCE) (PG13+) terminates the app's leftover pooled connections to this
        // DB, so no global ClearAllPools() is needed (that would disrupt sibling tests' pools).
        using var admin = new NpgsqlConnection(DatabaseFixture.AdminConnectionString);
        admin.Open();
        using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE);";
        cmd.ExecuteNonQuery();
    }
}
