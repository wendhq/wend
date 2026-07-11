using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Wend.Tests;

/// <summary>
/// Boots the real app against a throwaway PostgreSQL database on the local/CI server. Each factory
/// instance creates its OWN empty database and drops it on dispose, so tests stay isolated exactly
/// as they were with per-test SQLite files. The app builds the schema on startup (EnsureCreated now,
/// Migrate from Task 2).
/// </summary>
public sealed class WendApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"wend_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
