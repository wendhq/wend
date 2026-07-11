using Npgsql;

namespace Wend.Tests;

/// <summary>
/// Base connection to the local PostgreSQL *server* (its maintenance 'postgres' database).
/// Each API test creates its OWN throwaway database on this server (see WendApiFactory), so
/// tests stay isolated exactly as they were with per-test SQLite files. No container: a native
/// PostgreSQL service locally, a Postgres service container on CI. The base connection comes from
/// WEND_TEST_PG (set on CI); locally it defaults to the standard dev instance. Nothing secret is committed.
/// On start it also drops any wend_test_* databases orphaned by a previously crashed run.
/// </summary>
[SetUpFixture]
public sealed class DatabaseFixture
{
    public static string AdminConnectionString { get; private set; } = "";

    [OneTimeSetUp]
    public void Init()
    {
        AdminConnectionString =
            Environment.GetEnvironmentVariable("WEND_TEST_PG")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        // Clean slate: drop wend_test_* databases left over from a crashed run (a native
        // server persists, unlike a thrown-away container).
        using var admin = new NpgsqlConnection(AdminConnectionString);
        admin.Open();
        var stale = new List<string>();
        using (var find = admin.CreateCommand())
        {
            find.CommandText = "SELECT datname FROM pg_database WHERE datname LIKE 'wend_test_%'";
            using var r = find.ExecuteReader();
            while (r.Read()) stale.Add(r.GetString(0));
        }
        foreach (var db in stale)
        {
            using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE);";
            drop.ExecuteNonQuery();
        }
    }
}
