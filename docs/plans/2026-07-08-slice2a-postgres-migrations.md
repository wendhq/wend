# Wend Slice 2a — Plan 1: PostgreSQL + Migrations + Test Harness

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Wend's persistence from SQLite to PostgreSQL and adopt EF Core migrations, with the app behaving exactly as it does today — no auth, no schema change beyond the engine swap — and all 147 tests green.

**Architecture:** The app reads its connection string from `ConnectionStrings:WendDb` (the direct evolution of Slice 1's `Wend:DbPath` seam) and talks to PostgreSQL through Npgsql. Repository *unit* tests stay on fast in-memory SQLite (engine-agnostic CRUD). API *integration* tests run against a single throwaway PostgreSQL container (Testcontainers), each test getting its own freshly-created database via the same config seam — so the "fresh DB per test" isolation from Slice 1 is preserved without any change to the API test classes.

**Tech stack:** `net10.0`, EF Core 10, Npgsql `10.0.2`, `Microsoft.EntityFrameworkCore.Design`, `Testcontainers.PostgreSql` `4.13.0`, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`, Docker.

**Reference:** Signed-off spec at [`docs/2026-07-08-wend-slice2a-accounts-design.md`](../2026-07-08-wend-slice2a-accounts-design.md).

---

## Notes for the implementer

- **This is Plan 1 of Slice 2a — infrastructure only.** The spec's "Foundation" is split into two plans: **Plan 1 (this one)** does the pure PostgreSQL + migrations + test-harness swap with no behaviour change; **Plan 2** adds Identity, `WendUser`, `Board.OwnerId`, and per-user scoping. Keeping the engine swap separate keeps this a reviewable PR.
- **Docker is now required to run and to test.** The app needs a reachable PostgreSQL; the API tests spin one up with Testcontainers. Start a local dev database once (leave it running):
  ```bash
  docker run --name wend-postgres -e POSTGRES_PASSWORD=wenddev -e POSTGRES_DB=wend -p 5432:5432 -d postgres:17-alpine
  ```
  The repository *unit* tests need no Docker (they stay on in-memory SQLite). CI's `ubuntu-latest` ships Docker preinstalled, so the API tests run there unchanged.
- **New packages** (your ask-first rule — approving this plan approves these): add `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Design`; **remove** `Microsoft.EntityFrameworkCore.Sqlite` from `Wend.Core` and add it (plus `Testcontainers.PostgreSql`) to `Wend.Tests`.
- **Test strategy (Malin's call, 2026-07-08):** repo unit tests keep in-memory SQLite via `EnsureCreated()`; API integration tests move to PostgreSQL. The engines are independent — the SQLite unit tests build their schema from the model, the Postgres app + tests build theirs from migrations — so there is no EnsureCreated-vs-migrations conflict (that only bites when both hit the *same* database).
- **Migrations replace `EnsureCreated()`.** From Task 2 the app calls `Database.Migrate()` at startup. That's the pragmatic dev choice; Microsoft flags startup-migrate as unfit for real production, so the deployment plan (later) will switch to migration bundles / SQL scripts. Noted, not needed yet.
- **Gotcha — stop the running app before `dotnet build`/`test`.** The process is `Wend.Api`, not `Wend`: `Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force`. A wildcard-less `Get-Process Wend` matches nothing and the build fails with a copy-lock error.
- **Commits:** one per task, authored under your own account, **no co-author / no AI attribution** (house rule). Run every command from the repo root.

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Core/Wend.Core.csproj` | modify | Drop the SQLite provider; add the Npgsql provider |
| `Wend.Api/Wend.Api.csproj` | modify | Add `Microsoft.EntityFrameworkCore.Design` (migration tooling) |
| `Wend.Tests/Wend.Tests.csproj` | modify | Add SQLite provider (repo unit tests) + `Testcontainers.PostgreSql` |
| `Wend.Api/Program.cs` | modify | Read `ConnectionStrings:WendDb`; `UseNpgsql`; `EnsureCreated` → `Migrate` |
| `Wend.Tests/DatabaseFixture.cs` | new | One shared throwaway PostgreSQL container for the whole test run |
| `Wend.Tests/WendApiFactory.cs` | modify | Give each factory its own fresh database on the shared container |
| `Wend.Core/Migrations/` | new | The `InitialCreate` migration (generated) |
| `README.md` | modify | Development setup: Docker PostgreSQL now required |

---

## Task 1: Move persistence to PostgreSQL (app + API test harness)

**Files:**
- Modify: `Wend.Core/Wend.Core.csproj`, `Wend.Api/Wend.Api.csproj`, `Wend.Tests/Wend.Tests.csproj`
- Modify: `Wend.Api/Program.cs`
- Create: `Wend.Tests/DatabaseFixture.cs`
- Modify: `Wend.Tests/WendApiFactory.cs`

- [ ] **Step 1: Swap the provider packages**

Run (from the repo root):

```bash
dotnet remove Wend.Core package Microsoft.EntityFrameworkCore.Sqlite
dotnet add    Wend.Core package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.2
dotnet add    Wend.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.9
dotnet add    Wend.Tests package Testcontainers.PostgreSql --version 4.13.0
```

`Wend.Core.csproj` now references Npgsql (not SQLite); `Wend.Tests.csproj` gains SQLite (for the repo unit tests, which no longer get it transitively from `Wend.Core`) and Testcontainers.

- [ ] **Step 2: Start the dev database** (once — leave it running)

```bash
docker run --name wend-postgres -e POSTGRES_PASSWORD=wenddev -e POSTGRES_DB=wend -p 5432:5432 -d postgres:17-alpine
```

Store the dev connection string in user-secrets (keeps it out of the repo, per the spec's secrets stance):

```bash
dotnet user-secrets init --project Wend.Api
dotnet user-secrets set "ConnectionStrings:WendDb" "Host=localhost;Port=5432;Database=wend;Username=postgres;Password=wenddev" --project Wend.Api
```

- [ ] **Step 3: Point the app at PostgreSQL** — `Wend.Api/Program.cs`

Replace the SQLite config + registration (the `dbPath` line and the `AddDbContext(... UseSqlite ...)` line) with:

```csharp
// Config seam — connection string comes from user-secrets (dev) or environment (prod).
var connectionString = builder.Configuration.GetConnectionString("WendDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:WendDb is not configured. Set it via user-secrets (dev) or environment (prod).");
var port = int.TryParse(builder.Configuration["Wend:Port"], out var p) ? p : 5174;

builder.Services.AddDbContext<WendDbContext>(options => options.UseNpgsql(connectionString));
```

`UseNpgsql` lives in the `Microsoft.EntityFrameworkCore` namespace, which `Program.cs` already imports — no new `using`. Leave `EnsureCreated()` in place for this task (Task 2 replaces it). `WendPaths.DefaultDbPath()` is now unused; leave the file, it's removed in a later housekeeping pass.

- [ ] **Step 4: Add the shared test container** — `Wend.Tests/DatabaseFixture.cs`

```csharp
using Testcontainers.PostgreSql;

namespace Wend.Tests;

/// <summary>
/// One throwaway PostgreSQL container for the whole test run — started before any test, disposed
/// after the last. Each API test creates its own database on it (see WendApiFactory). Requires a
/// running Docker daemon; CI's ubuntu-latest has one preinstalled.
/// </summary>
[SetUpFixture]
public sealed class DatabaseFixture
{
    private static PostgreSqlContainer _container = null!;

    /// <summary>Connection string to the container's default maintenance database.</summary>
    public static string ConnectionString { get; private set; } = "";

    [OneTimeSetUp]
    public async Task StartContainer()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    [OneTimeTearDown]
    public async Task StopContainer() => await _container.DisposeAsync();
}
```

A `[SetUpFixture]` with the `Wend.Tests` namespace runs its `[OneTimeSetUp]` once, before every test in the assembly.

- [ ] **Step 5: Give each factory its own database** — replace `Wend.Tests/WendApiFactory.cs` with:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Wend.Tests;

/// <summary>
/// Boots the real app against a throwaway PostgreSQL database on the shared test container.
/// Each factory instance creates its OWN empty database, so tests stay isolated exactly as they
/// were with per-test SQLite files. The app builds the schema on startup (EnsureCreated now,
/// Migrate from Plan 1 Task 2).
/// </summary>
public sealed class WendApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"wend_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Create this instance's empty database on the shared container.
        using (var admin = new NpgsqlConnection(DatabaseFixture.ConnectionString))
        {
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_dbName}\"";
            cmd.ExecuteNonQuery();
        }

        // Point the app at it through the same seam Program.cs reads.
        var perTest = new NpgsqlConnectionStringBuilder(DatabaseFixture.ConnectionString)
        {
            Database = _dbName,
        };
        builder.UseSetting("ConnectionStrings:WendDb", perTest.ConnectionString);
    }
}
```

The API test classes (`BoardApiTests`, `ListApiTests`, `CardApiTests`, `LabelApiTests`, `ChecklistItemApiTests`, `ApiSmokeTests`) still just `new WendApiFactory()` in their `[SetUp]` — **no changes to any test class.** The repository unit tests are untouched (still in-memory SQLite).

- [ ] **Step 6: Run the whole suite on the new setup**

Make sure the dev container from Step 2 is running (`docker ps`), then:

Run: `dotnet test`
Expected: PASS — all 147 tests (repo unit tests on SQLite, API integration tests on the Testcontainers PostgreSQL). First run is a few seconds slower while Testcontainers pulls `postgres:17-alpine`.

- [ ] **Step 7: Commit**

```bash
git add Wend.Core/Wend.Core.csproj Wend.Api/Wend.Api.csproj Wend.Tests/Wend.Tests.csproj Wend.Api/Program.cs Wend.Tests/DatabaseFixture.cs Wend.Tests/WendApiFactory.cs
git commit -m "Move persistence to PostgreSQL; API tests on Testcontainers"
```

---

## Task 2: Adopt EF Core migrations

**Files:**
- Modify: `Wend.Api/Wend.Api.csproj`
- Create: `Wend.Core/Migrations/` (generated)
- Modify: `Wend.Api/Program.cs`

- [ ] **Step 1: Add the design-time tooling package**

```bash
dotnet add Wend.Api package Microsoft.EntityFrameworkCore.Design
```

`Microsoft.EntityFrameworkCore.Design` must be on the **startup** project (`Wend.Api`) for the CLI to build and inspect the app.

- [ ] **Step 2: Install / update the EF CLI**

```bash
dotnet tool install --global dotnet-ef
dotnet ef --version
```

If a `9.x`-or-older `dotnet-ef` is already installed, run `dotnet tool update --global dotnet-ef` instead — the tool major must be ≥ the EF Core 10 runtime.

- [ ] **Step 3: Generate the initial migration**

The DbContext lives in `Wend.Core` (where the `Migrations/` folder is written); EF builds and runs `Wend.Api` to read the connection string and DI:

```bash
dotnet ef migrations add InitialCreate --project Wend.Core --startup-project Wend.Api
```

Expected: a new `Wend.Core/Migrations/` folder with `<timestamp>_InitialCreate.cs` + a model snapshot. (`migrations add` reads the model; it does not touch the database.) If it errors with the "ConnectionStrings:WendDb is not configured" message, the design-time build isn't loading user-secrets — set `ASPNETCORE_ENVIRONMENT=Development` in the shell first, then re-run.

- [ ] **Step 4: Migrate at startup instead of EnsureCreated** — `Wend.Api/Program.cs`

Replace the `EnsureCreated()` block:

```csharp
// Apply pending EF Core migrations on startup (dev-simple; the deployment plan switches this to
// migration bundles / scripts, which Microsoft recommends for production).
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<WendDbContext>().Database.Migrate();
```

`Migrate()` is in the `Microsoft.EntityFrameworkCore` namespace (already imported). It creates the schema from the migration on a fresh database and applies any pending migrations on an existing one — so both the dev database and each test's fresh database now get their schema this way.

- [ ] **Step 5: Apply the migration to the dev database**

The next app run migrates it automatically, but do it explicitly once to confirm the tooling round-trips:

```bash
dotnet ef database update --project Wend.Core --startup-project Wend.Api
```

Expected: "Applying migration '<timestamp>_InitialCreate'." Done.

- [ ] **Step 6: Run the suite**

Run: `dotnet test`
Expected: PASS — all 147. The API tests' fresh databases are now schema'd by `Migrate()` on startup; the repo unit tests still build their SQLite schema from the model via `EnsureCreated()`, unaffected.

- [ ] **Step 7: Commit** (include the generated migration)

```bash
git add Wend.Api/Wend.Api.csproj Wend.Api/Program.cs Wend.Core/Migrations/
git commit -m "Adopt EF Core migrations; replace EnsureCreated with Migrate"
```

---

## Task 3: Acceptance pass + dev-setup docs

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Full green build**

Run: `dotnet build`  → 0 warnings.
Run: `dotnet test` → PASS, all 147.

- [ ] **Step 2: Manual acceptance** (dev container running)

```bash
dotnet run --project Wend.Api
```
Open `http://127.0.0.1:5174`, then:
- `GET http://127.0.0.1:5174/api/health` returns `{ "status": "ok" }`.
- Create a couple of boards, lists, and cards in the UI.
- Stop the app (`Ctrl+C`), start it again, reload → everything persisted (it's now in PostgreSQL, not `%LOCALAPPDATA%\Wend\data.db`).
- Optional sanity: `docker exec -it wend-postgres psql -U postgres -d wend -c '\dt'` lists the Wend tables plus `__EFMigrationsHistory`.

- [ ] **Step 3: Update the development setup in the README** — `README.md`

Add (or update) a development-setup note so a contributor knows Docker + PostgreSQL are now required:

```markdown
### Running locally

Wend now stores data in **PostgreSQL**. Start a local database with Docker:

```bash
docker run --name wend-postgres -e POSTGRES_PASSWORD=wenddev -e POSTGRES_DB=wend -p 5432:5432 -d postgres:17-alpine
```

Set the connection string once via user-secrets, then run the app:

```bash
dotnet user-secrets set "ConnectionStrings:WendDb" "Host=localhost;Port=5432;Database=wend;Username=postgres;Password=wenddev" --project Wend.Api
dotnet run --project Wend.Api
```

The schema is created and kept current by EF Core migrations on startup. Tests need Docker running (the API integration tests spin up a throwaway PostgreSQL container).
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Verify the PostgreSQL migration end to end; document dev setup"
```

---

## Definition of done

- `dotnet test` green — all **147** tests: repository unit tests on in-memory SQLite, API integration tests on a throwaway Testcontainers PostgreSQL, each API test isolated in its own fresh database.
- `dotnet build` clean (0 warnings).
- The app runs on PostgreSQL via the `ConnectionStrings:WendDb` seam; boards/lists/cards persist across restarts. No user-visible behaviour changed.
- Schema is created and evolved by EF Core **migrations** (`InitialCreate` committed under `Wend.Core/Migrations/`); `EnsureCreated()` is gone from the app.
- New dev dependency documented: Docker + PostgreSQL, with the exact commands, in the README.
- **Named trade-offs:** repo unit tests stay on SQLite (engine-agnostic CRUD; the shipping path is Postgres-tested via the API suite); `Database.Migrate()` runs at startup for dev simplicity — the deployment plan switches to migration bundles for production.
- The engine + migrations foundation is in place for **Plan 2 (Identity + `WendUser` + `Board.OwnerId` + per-user scoping)** to build on.
```