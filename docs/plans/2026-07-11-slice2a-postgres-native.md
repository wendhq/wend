# Wend Slice 2a — Plan 1 (revised): PostgreSQL (native) + Migrations + Test Harness — no Docker

> **Supersedes** [`2026-07-08-slice2a-postgres-migrations.md`](2026-07-08-slice2a-postgres-migrations.md) for how we run Postgres. The *only* change is the delivery of the database: a **native PostgreSQL Windows service** locally + a **Postgres service container** on CI, instead of Docker/Testcontainers. Everything else — the SQLite↔Postgres two-tier test strategy, EF migrations, the `ConnectionStrings:WendDb` seam — is unchanged.

**Why the change (2026-07-11):** Docker on Windows requires the Hyper-V hypervisor. On Malin's machine the hypervisor is incompatible with iCUE's kernel drivers (`CorsairLLAccess64` + the CPUID sensor driver) — enabling it produced `0x13a` kernel-heap-corruption BSODs (the same root cause as the April crash storm). iCUE is essential hardware control and can't be removed, and the machine is a gaming rig, so **the hypervisor stays off permanently.** Native PostgreSQL needs no hypervisor and no Docker.

**Goal:** Move Wend's persistence from SQLite to PostgreSQL and adopt EF Core migrations, with the app behaving exactly as it does today — no auth, no schema change beyond the engine swap — and all 147 tests green, on both dev machines and CI, with **no Docker anywhere.**

**Architecture:** The app reads its connection string from `ConnectionStrings:WendDb`. Repository *unit* tests stay on fast in-memory SQLite (engine-agnostic CRUD). API *integration* tests run against a real PostgreSQL **server**, each test creating and dropping its **own** throwaway database on that server — locally the native Windows service, on CI a Postgres service container. The base "server" connection comes from `WEND_TEST_PG` (defaulted to the standard local dev instance), so the identical test code runs in both places.

**Tech stack:** `net10.0`, EF Core 10, Npgsql `10.0.2`, `Microsoft.EntityFrameworkCore.Design`, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`. **No `Testcontainers`, no Docker.**

**Reference:** signed-off spec at [`2026-07-08-wend-slice2a-accounts-design.md`](../2026-07-08-wend-slice2a-accounts-design.md).

---

## Notes for the implementer

- **No Docker, no hypervisor.** Local dev + tests use a native PostgreSQL service; CI uses a Postgres service container (Docker exists on GitHub's Linux runner — not on our machines).
- **New packages** (approving this plan approves these): add `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Design`; **remove** `Microsoft.EntityFrameworkCore.Sqlite` from `Wend.Core` and add it to `Wend.Tests` (the repo unit tests, which no longer get it transitively). **Do NOT** add `Testcontainers` (the earlier plan did — this one doesn't).
- **Persistent server ⇒ tests must drop their own DB.** With Testcontainers the whole container was thrown away; a native server persists, so `WendApiFactory` now drops its throwaway database on dispose (`DROP DATABASE ... WITH (FORCE)`), or they would pile up.
- **Tests run sequentially (NUnit default) — keep it that way.** Each API test does `CREATE DATABASE` (which clones `template1`); parallel creates can fail with *"template1 is being accessed by other users."* Don't add `[Parallelizable]` to the API tests; if you ever do, serialise DB creation with a lock or retry.
- **Crash-orphan cleanup.** `DatabaseFixture` drops any leftover `wend_test_%` databases at run start, so a crashed run doesn't clutter the persistent server (belt-and-suspenders with the per-test drop on dispose).
- **Local test password is test-only.** The fixture default assumes the postgres password is `postgres` (Task 0); if you chose a different one, set the `WEND_TEST_PG` env var. This default is **never a prod path** — the app fails fast if `ConnectionStrings:WendDb` is unset, and prod supplies a least-privilege role via environment (deployment plan).
- **CI host assumption.** The test job runs directly on the runner, so `WEND_TEST_PG` uses `Host=localhost` to reach the service container. If a job `container:` is ever added, switch the host to the service name (`postgres`).
- **Migrations replace `EnsureCreated()`** (Task 2): the app calls `Database.Migrate()` at startup. Dev-simple; the deployment plan later switches to migration bundles for production.
- **Gotcha — stop the running app before `dotnet build`/`test`.** The process is `Wend.Api`, not `Wend`: `Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force`.
- **Commits:** one per task, own account, **no co-author / no AI attribution** (house rule). Run every command from the repo root.

## Task 0 — install the native PostgreSQL service (one-time, per machine)

```powershell
winget install --exact --id PostgreSQL.PostgreSQL.17 --interactive
```

(PostgreSQL's winget packages are versioned — `PostgreSQL.PostgreSQL.17`, not `PostgreSQL.PostgreSQL`.) During install set the **postgres** superuser password to `postgres` and keep port **5432** (local dev only — never committed). It installs as a normal Windows service (`postgresql-x64-17`) that starts with Windows. Verify:

```powershell
Get-Service postgresql*      # Running
```

Store the dev connection string in user-secrets (keeps it out of the repo):

```powershell
dotnet user-secrets init --project Wend.Api
dotnet user-secrets set "ConnectionStrings:WendDb" "Host=localhost;Port=5432;Database=wend;Username=postgres;Password=postgres" --project Wend.Api
```

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Core/Wend.Core.csproj` | modify | Drop the SQLite provider; add Npgsql |
| `Wend.Api/Wend.Api.csproj` | modify | Add `Microsoft.EntityFrameworkCore.Design` (migration tooling) |
| `Wend.Tests/Wend.Tests.csproj` | modify | Add SQLite provider (repo unit tests). **No Testcontainers.** |
| `Wend.Api/Program.cs` | modify | Read `ConnectionStrings:WendDb`; `UseNpgsql`; `EnsureCreated` → `Migrate` |
| `Wend.Tests/DatabaseFixture.cs` | new | Hold the base server connection (`WEND_TEST_PG` or local default) |
| `Wend.Tests/WendApiFactory.cs` | modify | Create + drop this test's own database on that server |
| `Wend.Core/Migrations/` | new | The generated `InitialCreate` migration |
| `.github/workflows/ci.yml` | modify | Add a Postgres service container + `WEND_TEST_PG` env |
| `README.md` | modify | Dev setup: native PostgreSQL required |

---

## Task 1 — move persistence to PostgreSQL (app + native test harness)

- [ ] **Step 1 — swap the provider packages** (from repo root):

```powershell
dotnet remove Wend.Core package Microsoft.EntityFrameworkCore.Sqlite
dotnet add    Wend.Core package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.2
dotnet add    Wend.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.9
```

- [ ] **Step 2 — point the app at PostgreSQL** — `Wend.Api/Program.cs`. Replace the SQLite `dbPath` + `AddDbContext(... UseSqlite ...)` lines with:

```csharp
var connectionString = builder.Configuration.GetConnectionString("WendDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:WendDb is not configured. Set it via user-secrets (dev) or environment (prod).");
var port = int.TryParse(builder.Configuration["Wend:Port"], out var p) ? p : 5174;

builder.Services.AddDbContext<WendDbContext>(options => options.UseNpgsql(connectionString));
```

Leave `EnsureCreated()` for this task (Task 2 replaces it). `UseNpgsql` is in the `Microsoft.EntityFrameworkCore` namespace, already imported.

- [ ] **Step 3 — base server connection for tests** — new `Wend.Tests/DatabaseFixture.cs`:

```csharp
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
```

- [ ] **Step 4 — per-test database** — replace `Wend.Tests/WendApiFactory.cs`:

```csharp
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
        using (var admin = new NpgsqlConnection(DatabaseFixture.AdminConnectionString))
        {
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_dbName}\"";
            cmd.ExecuteNonQuery();
        }

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
```

The API test classes still just `new WendApiFactory()` — **no changes to any test class.** Repo unit tests untouched (still in-memory SQLite).

- [ ] **Step 5 — run the suite.** Native Postgres service running (`Get-Service postgresql*`), then `dotnet test`. Expected: PASS — all 147.

- [ ] **Step 6 — commit:** `Move persistence to PostgreSQL; API tests on a native Postgres server`

---

## Task 2 — adopt EF Core migrations

- [ ] **Step 1** — `dotnet add Wend.Api package Microsoft.EntityFrameworkCore.Design`
- [ ] **Step 2** — `dotnet tool install --global dotnet-ef` (or `dotnet tool update --global dotnet-ef`; tool major must be ≥ EF 10)
- [ ] **Step 3** — `dotnet ef migrations add InitialCreate --project Wend.Core --startup-project Wend.Api` (if it can't read the connection string, `ASPNETCORE_ENVIRONMENT=Development` first so user-secrets load)
- [ ] **Step 4** — replace the `EnsureCreated()` block in `Program.cs`:

```csharp
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<WendDbContext>().Database.Migrate();
```

- [ ] **Step 5** — `dotnet ef database update --project Wend.Core --startup-project Wend.Api` (confirm the tooling round-trips)
- [ ] **Step 6** — `dotnet test` → all 147.
- [ ] **Step 7** — commit (include `Wend.Core/Migrations/`): `Adopt EF Core migrations; replace EnsureCreated with Migrate`

---

## Task 3 — CI Postgres service + acceptance + docs

- [ ] **Step 1 — `.github/workflows/ci.yml`**: add a Postgres service to the test job and point the tests at it:

```yaml
    services:
      postgres:
        image: postgres:17-alpine
        env:
          POSTGRES_PASSWORD: postgres
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready --health-interval 10s --health-timeout 5s --health-retries 5
    env:
      WEND_TEST_PG: "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"
```

(The unit tests ignore it — they're on SQLite; the API tests read `WEND_TEST_PG`.)

- [ ] **Step 2 — full green build:** `dotnet build` (0 warnings), `dotnet test` (147).
- [ ] **Step 3 — manual acceptance** (native service running): `dotnet run --project Wend.Api`, `GET http://127.0.0.1:5174/api/health` → `{ "status": "ok" }`, create boards/lists/cards, restart, reload → persisted (now in PostgreSQL). Sanity: `psql -U postgres -d wend -c "\dt"` lists the Wend tables + `__EFMigrationsHistory`.
- [ ] **Step 4 — README** dev-setup: native PostgreSQL required (winget command + user-secrets), tests need the service running; note CI uses a Postgres service container.
- [ ] **Step 5 — commit:** `Run PostgreSQL integration tests on CI service container; document native dev setup`

---

## Definition of done

- `dotnet test` green — all **147**: repository unit tests on in-memory SQLite; API integration tests on a real PostgreSQL server (native service locally, service container on CI), each isolated in its own created-and-dropped database.
- `dotnet build` clean (0 warnings).
- App runs on PostgreSQL via `ConnectionStrings:WendDb`; boards/lists/cards persist across restarts; no user-visible behaviour changed.
- Schema created/evolved by EF **migrations** (`InitialCreate` committed); `EnsureCreated()` gone from the app.
- **No Docker or Testcontainers anywhere** — on either dev machine or in the repo. CI uses a Postgres service container on its Linux runner.
- New dev dependency documented: a native PostgreSQL service, with exact commands, in the README.
- Foundation ready for **Plan 2 (Identity + `WendUser` + `Board.OwnerId` + per-user scoping)**.
