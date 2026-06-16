# Wend Slice 1 — Plan 1: Foundation + Boards

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A working local app where you can create, rename, and delete boards — establishing the full model → repository → API → frontend → test pattern (and the accessibility foundation) that every later Slice 1 feature copies.

**Architecture:** EF Core → SQLite behind `IBoardRepository` (in `Wend.Core`); minimal-API endpoint group in `Wend.Api` mirroring kenaz's `MapCheckInEndpoints` pattern; vanilla-JS MVC served straight from `wwwroot` (no build step). Backend is TDD'd with NUnit (repository unit tests on an in-memory SQLite DB, API integration tests via `WebApplicationFactory`); the frontend is verified by a manual browser pass.

**Tech stack:** `net10.0`, EF Core 10 SQLite, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`, vanilla JS (ES modules), the shared design-system.

**Reference:** Signed-off spec at [`docs/2026-06-15-wend-slice1-design.md`](../2026-06-15-wend-slice1-design.md).

---

## Notes for the implementer

- **No build step.** The frontend is hand-authored JS/CSS in `Wend.Api/wwwroot`, served as static files. There is no Node/npm/Vite. `dotnet run --project Wend.Api` is the only thing to run. (This is where Wend deliberately differs from kenaz, whose `wwwroot` is a Vite build output.)
- **Tests must never touch the real database.** The app's real DB is `%LOCALAPPDATA%\Wend\data.db`. Every test boots the app against a throwaway temp DB via the `Wend:DbPath` config seam (Task 4). Do not skip Task 4 before the API tests.
- **JSON casing.** ASP.NET Core serializes `Board { Id, Title }` as `{ "id", "title" }` (camelCase) and binds request bodies case-insensitively, so the frontend uses `b.id` / `b.title` and posts `{ title }`.
- **Schema & migrations (named trade-off).** Slice 1 creates the schema with `EnsureCreated()`, which does *not* update an existing `data.db` when later plans add tables. If the schema changes during Slice 1, delete `%LOCALAPPDATA%\Wend\data.db` and let it rebuild. EF Core migrations come in at the Slice 1 → 2 boundary, before any real data exists.
- **Accessibility is built in from the start.** Tasks 8–9 establish the a11y foundation every later feature reuses: an `aria-live` status region with an `announce()` helper, focus returned after each re-render, and board-specific accessible names on controls. Don't strip these when copying the pattern.
- **Commits:** one per task, authored under your own account, **no co-author / no AI attribution** (house rule). Run from the repo root unless noted.

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Core/Board.cs` | new | Board entity (`Id`, `Title`) |
| `Wend.Core/WendDbContext.cs` | new | EF Core context; `Boards` DbSet |
| `Wend.Core/IBoardRepository.cs` | modify | Board persistence methods |
| `Wend.Core/EfBoardRepository.cs` | new | EF implementation of the seam |
| `Wend.Api/BoardEndpoints.cs` | new | `/api/boards` minimal-API group + DTOs + title-length guard |
| `Wend.Api/Program.cs` | modify | Register DbContext + repo, `EnsureCreated`, map the group |
| `Wend.Api/wwwroot/index.html` | modify | App shell: theme snippet, design-system links, `#app`, `aria-live` status region, module script |
| `Wend.Api/wwwroot/css/app.css` | new | Project layout styles (mobile-first) + `.visually-hidden` |
| `Wend.Api/wwwroot/js/api.js` | new | `fetch` wrapper |
| `Wend.Api/wwwroot/js/announce.js` | new | Screen-reader announcements via the live region |
| `Wend.Api/wwwroot/js/boards/model.js` | new | Boards state + API calls + subscribe/notify |
| `Wend.Api/wwwroot/js/boards/view.js` | new | Renders boards (labelled controls); forwards events; post-render focus |
| `Wend.Api/wwwroot/js/boards/controller.js` | new | Wires view → model; announces; manages focus; handles errors |
| `Wend.Api/wwwroot/js/main.js` | new | Boot: wire announcer + model/view/controller, load |
| `Wend.Api/wwwroot/design-system/` | new | Bundled copy of `_template/design-system` |
| `Wend.Tests/WendApiFactory.cs` | new | Boots the app against a throwaway temp DB |
| `Wend.Tests/ApiSmokeTests.cs` | modify | Use `WendApiFactory` |
| `Wend.Tests/BoardRepositoryTests.cs` | new | Repository unit tests (in-memory SQLite) |
| `Wend.Tests/BoardApiTests.cs` | new | API integration tests |

---

## Task 1: Board entity + WendDbContext

**Files:**
- Create: `Wend.Core/Board.cs`
- Create: `Wend.Core/WendDbContext.cs`
- Test: `Wend.Tests/BoardRepositoryTests.cs`

- [ ] **Step 1: Write the failing test** — `Wend.Tests/BoardRepositoryTests.cs`

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class BoardRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;

    [SetUp]
    public void SetUp()
    {
        // In-memory SQLite lives only as long as the connection is open.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WendDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new WendDbContext(options);
        _db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task Saved_board_can_be_read_back()
    {
        _db.Boards.Add(new Board { Title = "Sprint 1" });
        await _db.SaveChangesAsync();

        var board = await _db.Boards.SingleAsync();

        Assert.That(board.Id, Is.GreaterThan(0));
        Assert.That(board.Title, Is.EqualTo("Sprint 1"));
    }
}
```

> If `Microsoft.Data.Sqlite` does not resolve, add it to the test project: `dotnet add Wend.Tests package Microsoft.Data.Sqlite` (it is otherwise transitive via `Wend.Core`'s EF Core SQLite reference).

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BoardRepositoryTests"`
Expected: FAIL — does not compile (`Board` and `WendDbContext` don't exist).

- [ ] **Step 3: Create the entity** — `Wend.Core/Board.cs`

```csharp
namespace Wend.Core;

/// <summary>A board — the top-level container for its lists and cards.</summary>
public class Board
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}
```

- [ ] **Step 4: Create the context** — `Wend.Core/WendDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>EF Core context for Wend's SQLite database.</summary>
public class WendDbContext(DbContextOptions<WendDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~BoardRepositoryTests"`
Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add Wend.Core/Board.cs Wend.Core/WendDbContext.cs Wend.Tests/BoardRepositoryTests.cs
git commit -m "Add Board entity and WendDbContext"
```

---

## Task 2: Repository — create & list

**Files:**
- Modify: `Wend.Core/IBoardRepository.cs`
- Create: `Wend.Core/EfBoardRepository.cs`
- Test: `Wend.Tests/BoardRepositoryTests.cs`

- [ ] **Step 1: Add the failing test** — append inside `BoardRepositoryTests` and add a repo field

Add a field and initialise it in `SetUp` (after the `_db` line):

```csharp
    private EfBoardRepository _repo = null!;
    // ...in SetUp, after creating _db:
    _repo = new EfBoardRepository(_db);
```

Add the test:

```csharp
    [Test]
    public async Task Create_adds_a_board_and_list_returns_it()
    {
        var created = await _repo.CreateBoardAsync("Sprint 1");

        var all = await _repo.GetBoardsAsync();

        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(all.Select(b => b.Title), Is.EqualTo(new[] { "Sprint 1" }));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BoardRepositoryTests"`
Expected: FAIL — `EfBoardRepository` / `CreateBoardAsync` / `GetBoardsAsync` don't exist.

- [ ] **Step 3: Define the seam methods** — `Wend.Core/IBoardRepository.cs`

Replace the empty interface body with:

```csharp
namespace Wend.Core;

/// <summary>
/// Persistence seam for the board domain. Slice 1 implements this with EF Core → SQLite;
/// the API depends only on this interface, so storage can be swapped without touching board logic.
/// </summary>
public interface IBoardRepository
{
    Task<IReadOnlyList<Board>> GetBoardsAsync();
    Task<Board?> GetBoardAsync(int id);
    Task<Board> CreateBoardAsync(string title);
    Task<bool> RenameBoardAsync(int id, string newTitle);
    Task<bool> DeleteBoardAsync(int id);
}
```

- [ ] **Step 4: Implement create & list** — `Wend.Core/EfBoardRepository.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfBoardRepository(WendDbContext db) : IBoardRepository
{
    public async Task<IReadOnlyList<Board>> GetBoardsAsync() =>
        await db.Boards.OrderBy(b => b.Id).ToListAsync();

    public async Task<Board> CreateBoardAsync(string title)
    {
        var board = new Board { Title = title };
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        return board;
    }

    // GetBoardAsync / RenameBoardAsync / DeleteBoardAsync arrive in Task 3.
    public Task<Board?> GetBoardAsync(int id) => throw new NotImplementedException();
    public Task<bool> RenameBoardAsync(int id, string newTitle) => throw new NotImplementedException();
    public Task<bool> DeleteBoardAsync(int id) => throw new NotImplementedException();
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~BoardRepositoryTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add Wend.Core/IBoardRepository.cs Wend.Core/EfBoardRepository.cs Wend.Tests/BoardRepositoryTests.cs
git commit -m "Add board create and list to repository"
```

---

## Task 3: Repository — get, rename, delete

**Files:**
- Modify: `Wend.Core/EfBoardRepository.cs`
- Test: `Wend.Tests/BoardRepositoryTests.cs`

- [ ] **Step 1: Add the failing tests** — append to `BoardRepositoryTests`

```csharp
    [Test]
    public async Task Get_returns_the_board_or_null()
    {
        var created = await _repo.CreateBoardAsync("Sprint 1");

        Assert.That((await _repo.GetBoardAsync(created.Id))?.Title, Is.EqualTo("Sprint 1"));
        Assert.That(await _repo.GetBoardAsync(9999), Is.Null);
    }

    [Test]
    public async Task Rename_changes_the_title_and_reports_missing()
    {
        var created = await _repo.CreateBoardAsync("Old");

        Assert.That(await _repo.RenameBoardAsync(created.Id, "New"), Is.True);
        Assert.That((await _repo.GetBoardAsync(created.Id))!.Title, Is.EqualTo("New"));
        Assert.That(await _repo.RenameBoardAsync(9999, "X"), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_board_and_reports_missing()
    {
        var created = await _repo.CreateBoardAsync("Temp");

        Assert.That(await _repo.DeleteBoardAsync(created.Id), Is.True);
        Assert.That(await _repo.GetBoardsAsync(), Is.Empty);
        Assert.That(await _repo.DeleteBoardAsync(9999), Is.False);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~BoardRepositoryTests"`
Expected: FAIL — `NotImplementedException` from the three stubs.

- [ ] **Step 3: Replace the three stubs** — `Wend.Core/EfBoardRepository.cs`

```csharp
    public async Task<Board?> GetBoardAsync(int id) =>
        await db.Boards.FindAsync(id);

    public async Task<bool> RenameBoardAsync(int id, string newTitle)
    {
        var board = await db.Boards.FindAsync(id);
        if (board is null) return false;
        board.Title = newTitle;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteBoardAsync(int id)
    {
        var board = await db.Boards.FindAsync(id);
        if (board is null) return false;
        db.Boards.Remove(board);
        await db.SaveChangesAsync();
        return true;
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~BoardRepositoryTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Core/EfBoardRepository.cs Wend.Tests/BoardRepositoryTests.cs
git commit -m "Add board get, rename and delete to repository"
```

---

## Task 4: Test factory that never touches the real DB

**Files:**
- Create: `Wend.Tests/WendApiFactory.cs`
- Modify: `Wend.Tests/ApiSmokeTests.cs`

- [ ] **Step 1: Create the factory** — `Wend.Tests/WendApiFactory.cs`

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Wend.Tests;

/// <summary>
/// Boots the real app against a throwaway SQLite file via the Wend:DbPath seam, so tests
/// never touch the real %LOCALAPPDATA%\Wend\data.db. Each instance gets its own database.
/// </summary>
public sealed class WendApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"wend-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Wend:DbPath", _dbPath);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Best-effort: the SQLite connection pool may still hold the file briefly on
        // Windows. If the delete fails, leave it — it's in the OS temp folder.
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }
    }
}
```

- [ ] **Step 2: Point the smoke tests at it** — `Wend.Tests/ApiSmokeTests.cs`

Replace both `new WebApplicationFactory<Program>()` occurrences with `new WendApiFactory()` and remove the now-unused `using Microsoft.AspNetCore.Mvc.Testing;`. Each test's first line becomes:

```csharp
        await using var factory = new WendApiFactory();
```

- [ ] **Step 3: Run the smoke tests**

Run: `dotnet test --filter "FullyQualifiedName~ApiSmokeTests"`
Expected: PASS (2 tests). (They still pass because the app has no DB wiring yet — Task 5 adds it, and from then on these run against the temp DB.)

- [ ] **Step 4: Commit**

```bash
git add Wend.Tests/WendApiFactory.cs Wend.Tests/ApiSmokeTests.cs
git commit -m "Add WendApiFactory so tests use a throwaway database"
```

---

## Task 5: Wire the DB + repo; list endpoint

**Files:**
- Create: `Wend.Api/BoardEndpoints.cs`
- Modify: `Wend.Api/Program.cs`
- Test: `Wend.Tests/BoardApiTests.cs`

- [ ] **Step 1: Write the failing test** — `Wend.Tests/BoardApiTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using Wend.Core;

namespace Wend.Tests;

public class BoardApiTests
{
    private WendApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Boards_start_empty()
    {
        var boards = await _client.GetFromJsonAsync<List<Board>>("/api/boards");

        Assert.That(boards, Is.Empty);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BoardApiTests"`
Expected: FAIL — `/api/boards` returns 404 (no endpoint), so the call throws / the list is not returned.

- [ ] **Step 3: Create the endpoint group** — `Wend.Api/BoardEndpoints.cs`

```csharp
using Wend.Core;

namespace Wend.Api;

public static class BoardEndpoints
{
    private const int MaxTitleLength = 200;

    public static RouteGroupBuilder MapBoardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (IBoardRepository repo) =>
            Results.Ok(await repo.GetBoardsAsync()));

        return group;
    }
}
```

- [ ] **Step 4: Wire DB, repo and the group** — replace `Wend.Api/Program.cs` with:

```csharp
using Microsoft.EntityFrameworkCore;
using Wend.Api;
using Wend.Core;

var builder = WebApplication.CreateBuilder(args);

// Config seam — DB path and port are overridable by tests and manual runs.
var dbPath = builder.Configuration["Wend:DbPath"] ?? WendPaths.DefaultDbPath();
var port = int.TryParse(builder.Configuration["Wend:Port"], out var p) ? p : 5174;

builder.Services.AddDbContext<WendDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<IBoardRepository, EfBoardRepository>();

// Keep request paths and bodies out of the framework logs; quiet the startup banner.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

// Local-first: listen on 127.0.0.1 + [::1] only, never the public network.
builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(port));

var app = builder.Build();

// Create the SQLite schema on first run. NOTE: EnsureCreated does NOT migrate an existing
// database when later plans add tables — see "Schema & migrations" in the notes. Slice 1
// adopts EF Core migrations at the Slice 1 -> 2 boundary.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<WendDbContext>().Database.EnsureCreated();

// Unhandled failures → bodyless 500 (no developer exception page over the wire).
app.UseExceptionHandler(b => b.Run(ctx => { ctx.Response.StatusCode = 500; return Task.CompletedTask; }));

// Serve the vanilla-JS frontend (wwwroot) same-origin.
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGroup("/api/boards").MapBoardEndpoints();

// Any non-API path renders the SPA shell; the client handles routing from there.
app.MapFallbackToFile("index.html");

Console.WriteLine($"Wend → http://127.0.0.1:{port}");

app.Run();

// Exposed so Wend.Tests can boot the real app with WebApplicationFactory<Program>.
public partial class Program;
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~BoardApiTests"`
Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/BoardEndpoints.cs Wend.Api/Program.cs Wend.Tests/BoardApiTests.cs
git commit -m "Wire SQLite and expose GET /api/boards"
```

---

## Task 6: Create endpoint (POST) + title-length guard

**Files:**
- Modify: `Wend.Api/BoardEndpoints.cs`
- Test: `Wend.Tests/BoardApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append to `BoardApiTests`

```csharp
    [Test]
    public async Task Posting_a_board_creates_it()
    {
        var response = await _client.PostAsJsonAsync("/api/boards", new { title = "Sprint 1" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var boards = await _client.GetFromJsonAsync<List<Board>>("/api/boards");
        Assert.That(boards!.Single().Title, Is.EqualTo("Sprint 1"));
    }

    [Test]
    public async Task Posting_a_blank_title_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/boards", new { title = "   " });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_over_long_title_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/boards", new { title = new string('x', 201) });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~BoardApiTests"`
Expected: FAIL — POST returns 404 (no route).

- [ ] **Step 3: Add the POST route + request DTO** — `Wend.Api/BoardEndpoints.cs`

Add inside `MapBoardEndpoints`, before `return group;` (`MaxTitleLength` was defined in Task 5):

```csharp
        group.MapPost("/", async (CreateBoardRequest req, IBoardRepository repo) =>
        {
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            var board = await repo.CreateBoardAsync(title);
            return Results.Created($"/api/boards/{board.Id}", board);
        });
```

Add at the bottom of the file (outside the class):

```csharp
public record CreateBoardRequest(string Title);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~BoardApiTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/BoardEndpoints.cs Wend.Tests/BoardApiTests.cs
git commit -m "Add POST /api/boards with a title-length guard"
```

---

## Task 7: Get-one, rename (PUT), delete (DELETE)

**Files:**
- Modify: `Wend.Api/BoardEndpoints.cs`
- Test: `Wend.Tests/BoardApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append to `BoardApiTests`

```csharp
    [Test]
    public async Task Get_one_returns_the_board_or_404()
    {
        var created = await (await _client.PostAsJsonAsync("/api/boards", new { title = "A" }))
            .Content.ReadFromJsonAsync<Board>();

        var found = await _client.GetAsync($"/api/boards/{created!.Id}");
        Assert.That(found.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var missing = await _client.GetAsync("/api/boards/9999");
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Put_renames_the_board()
    {
        var created = await (await _client.PostAsJsonAsync("/api/boards", new { title = "Old" }))
            .Content.ReadFromJsonAsync<Board>();

        var put = await _client.PutAsJsonAsync($"/api/boards/{created!.Id}", new { title = "New" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var board = await _client.GetFromJsonAsync<Board>($"/api/boards/{created.Id}");
        Assert.That(board!.Title, Is.EqualTo("New"));
    }

    [Test]
    public async Task Delete_removes_the_board()
    {
        var created = await (await _client.PostAsJsonAsync("/api/boards", new { title = "Temp" }))
            .Content.ReadFromJsonAsync<Board>();

        var deleted = await _client.DeleteAsync($"/api/boards/{created!.Id}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var boards = await _client.GetFromJsonAsync<List<Board>>("/api/boards");
        Assert.That(boards, Is.Empty);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~BoardApiTests"`
Expected: FAIL — these routes return 404.

- [ ] **Step 3: Add the routes + rename DTO** — `Wend.Api/BoardEndpoints.cs`

Add inside `MapBoardEndpoints`, before `return group;`:

```csharp
        group.MapGet("/{id:int}", async (int id, IBoardRepository repo) =>
            await repo.GetBoardAsync(id) is { } board ? Results.Ok(board) : Results.NotFound());

        group.MapPut("/{id:int}", async (int id, RenameBoardRequest req, IBoardRepository repo) =>
        {
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            return await repo.RenameBoardAsync(id, title)
                ? Results.NoContent() : Results.NotFound();
        });

        group.MapDelete("/{id:int}", async (int id, IBoardRepository repo) =>
            await repo.DeleteBoardAsync(id) ? Results.NoContent() : Results.NotFound());
```

Add at the bottom of the file (outside the class), next to `CreateBoardRequest`:

```csharp
public record RenameBoardRequest(string Title);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test`
Expected: PASS (all tests — repository, smoke, and API).

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/BoardEndpoints.cs Wend.Tests/BoardApiTests.cs
git commit -m "Add GET by id, PUT and DELETE for boards"
```

---

## Task 8: Frontend shell — design-system, live region, app shell

**Files:**
- Create: `Wend.Api/wwwroot/design-system/` (bundled copy)
- Modify: `Wend.Api/wwwroot/index.html`
- Create: `Wend.Api/wwwroot/css/app.css`

This task has no automated test — verify it by eye in the browser.

- [ ] **Step 1: Bundle the design-system** (PowerShell, from the repo root)

```powershell
$src = 'C:\Users\Nugget\Documents\Development\_template\design-system'
$dst = 'Wend.Api\wwwroot\design-system'
foreach ($part in 'tokens','base','primitives','components','compositions','utilities','theme') {
  Copy-Item "$src\$part" "$dst\$part" -Recurse -Force
}
Copy-Item "$src\VERSION" "$dst\VERSION" -Force
```

- [ ] **Step 2: Replace the app shell** — `Wend.Api/wwwroot/index.html`

```html
<!doctype html>
<html lang="en" data-theme="dark">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Wend</title>

  <!-- Inline theme init BEFORE stylesheets — prevents a flash of light theme. -->
  <script>
    (function () {
      var saved = localStorage.getItem("theme");
      document.documentElement.dataset.theme = saved || "dark";
    })();
  </script>

  <link rel="stylesheet" href="/design-system/tokens/index.css" />
  <link rel="stylesheet" href="/design-system/base/reset.css" />
  <link rel="stylesheet" href="/design-system/base/base.css" />
  <link rel="stylesheet" href="/design-system/primitives/index.css" />
  <link rel="stylesheet" href="/design-system/components/index.css" />
  <link rel="stylesheet" href="/design-system/compositions/index.css" />
  <link rel="stylesheet" href="/design-system/utilities/index.css" />
  <link rel="stylesheet" href="/css/app.css" />
</head>
<body>
  <header class="app-header"><h1>Wend</h1></header>
  <main id="app"></main>

  <!-- Screen-reader announcements (create/rename/delete now; move/undo later). Lives OUTSIDE
       #app so the view's re-render never wipes it. -->
  <div id="status" class="visually-hidden" role="status" aria-live="polite"></div>

  <script type="module" src="/js/main.js"></script>
</body>
</html>
```

- [ ] **Step 3: Create project styles** — `Wend.Api/wwwroot/css/app.css`

```css
/* Wend — project layout styles. Mobile-first; layered up at min-width breakpoints.
   The design-system (linked in index.html) provides the dark-mode tokens, reset,
   focus rings and base element styling. This file is layout only; the polish slice
   adopts design-system component classes. */

.app-header {
  padding: 1rem;
  border-bottom: 1px solid;
  border-color: color-mix(in srgb, currentColor 15%, transparent);
}

.app-header h1 {
  margin: 0;
  font-size: 1.5rem;
}

#app {
  max-width: 40rem;
  margin: 0 auto;
  padding: 1rem;
}

.board-form {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1.5rem;
}

.board-form input {
  flex: 1;
}

.board-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.board-list li {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.board-title {
  flex: 1;
}

.empty {
  opacity: 0.7;
}

/* Screen-reader-only: present in the accessibility tree, invisible on screen. */
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  padding: 0;
  border: 0;
  clip-path: inset(50%);
  overflow: hidden;
  white-space: nowrap;
}

@media (min-width: 768px) {
  #app { padding: 2rem; }
}
```

- [ ] **Step 4: Verify in the browser**

Run: `dotnet run --project Wend.Api`
Open `http://127.0.0.1:5174`. Expected: a dark page with a **Wend** header and an empty `<main>` (the boards UI arrives in Task 9). No console errors; all `/design-system/*` stylesheets return 200 in the Network tab. Stop with `Ctrl+C`.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/wwwroot/
git commit -m "Add design-system bundle, live region and dark app shell"
```

---

## Task 9: Frontend — boards MVC (labelled, announced, focus-managed)

**Files:**
- Create: `Wend.Api/wwwroot/js/api.js`
- Create: `Wend.Api/wwwroot/js/announce.js`
- Create: `Wend.Api/wwwroot/js/boards/model.js`
- Create: `Wend.Api/wwwroot/js/boards/view.js`
- Create: `Wend.Api/wwwroot/js/boards/controller.js`
- Create: `Wend.Api/wwwroot/js/main.js`

Verified by browser pass (final step).

- [ ] **Step 1: fetch wrapper** — `Wend.Api/wwwroot/js/api.js`

```js
// Thin fetch wrapper: JSON in/out, throws on non-2xx, returns null for 204.
export async function api(path, options = {}) {
  const res = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.status === 204 ? null : res.json();
}
```

- [ ] **Step 2: announcer** — `Wend.Api/wwwroot/js/announce.js`

```js
// Writes messages to the shared aria-live region for screen readers.
export function createAnnouncer(region) {
  return (message) => {
    region.textContent = "";
    // Next frame so the change registers even for a repeated message.
    requestAnimationFrame(() => {
      region.textContent = message;
    });
  };
}
```

- [ ] **Step 3: model (state + data only)** — `Wend.Api/wwwroot/js/boards/model.js`

```js
import { api } from "../api.js";

// State and data only — no DOM. Subscribers are notified on every change.
export function createBoardsModel() {
  let boards = [];
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(boards));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(boards);
    },
    async load() {
      boards = await api("/api/boards");
      notify();
    },
    async create(title) {
      await api("/api/boards", { method: "POST", body: JSON.stringify({ title }) });
      await this.load();
    },
    async rename(id, title) {
      await api(`/api/boards/${id}`, { method: "PUT", body: JSON.stringify({ title }) });
      await this.load();
    },
    async remove(id) {
      await api(`/api/boards/${id}`, { method: "DELETE" });
      await this.load();
    },
  };
}
```

- [ ] **Step 4: view (renders state; labelled controls; post-render focus)** — `Wend.Api/wwwroot/js/boards/view.js`

```js
// Renders HTML from state and forwards events via data-action. No fetch, no business logic.
export function createBoardsView(root) {
  function render(boards) {
    const items = boards.length
      ? boards
          .map(
            (b) => `
        <li>
          <span class="board-title">${escapeHtml(b.title)}</span>
          <button data-action="rename" data-id="${b.id}"
            aria-label="Rename board: ${escapeHtml(b.title)}">Rename</button>
          <button data-action="delete" data-id="${b.id}"
            aria-label="Delete board: ${escapeHtml(b.title)}">Delete</button>
        </li>`
          )
          .join("")
      : `<li class="empty">No boards yet — add one above.</li>`;

    root.innerHTML = `
      <form class="board-form" data-action="create">
        <input name="title" aria-label="New board name" placeholder="New board…" required />
        <button type="submit">Add board</button>
      </form>
      <ul class="board-list">${items}</ul>`;
  }

  // Called after an action so keyboard/SR focus never falls back to <body>.
  function focusNewBoardInput() {
    root.querySelector(".board-form input")?.focus();
  }

  function bindActions(handlers) {
    root.addEventListener("submit", async (e) => {
      if (e.target.dataset.action !== "create") return;
      e.preventDefault();
      const title = e.target.title.value.trim();
      const submit = e.target.querySelector("button[type=submit]");
      submit.disabled = true; // guard against a double-submit while the request is in flight
      try {
        await handlers.create(title);
      } finally {
        submit.disabled = false; // no-op if the form was re-rendered on success
      }
    });
    root.addEventListener("click", (e) => {
      const btn = e.target.closest("button[data-action]");
      if (!btn) return;
      const id = Number(btn.dataset.id);
      if (btn.dataset.action === "rename") handlers.rename(id);
      if (btn.dataset.action === "delete") handlers.delete(id);
    });
  }

  return { render, focusNewBoardInput, bindActions };
}

function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]
  );
}
```

- [ ] **Step 5: controller (wires view → model; announces; focus; errors)** — `Wend.Api/wwwroot/js/boards/controller.js`

```js
// Wires view actions to the model, announces results to screen readers, returns focus,
// and surfaces failures instead of letting them become silent unhandled rejections.
export function createBoardsController(model, view, announce) {
  view.bindActions({
    create: async (title) => {
      if (!title) return;
      try {
        await model.create(title);
        announce("Board added.");
        view.focusNewBoardInput();
      } catch {
        announce("Couldn't add the board — please try again.");
      }
    },
    rename: async (id) => {
      const title = prompt("New board name?");
      if (!title || !title.trim()) return;
      try {
        await model.rename(id, title.trim());
        announce("Board renamed.");
        view.focusNewBoardInput();
      } catch {
        announce("Couldn't rename the board — please try again.");
      }
    },
    delete: async (id) => {
      // Deleting a whole board is a big destructive action → confirm (per spec).
      if (!confirm("Delete this board and everything in it?")) return;
      try {
        await model.remove(id);
        announce("Board deleted.");
        view.focusNewBoardInput();
      } catch {
        announce("Couldn't delete the board — please try again.");
      }
    },
  });
  model.subscribe((boards) => view.render(boards));
}
```

- [ ] **Step 6: boot** — `Wend.Api/wwwroot/js/main.js`

```js
import { createBoardsModel } from "./boards/model.js";
import { createBoardsView } from "./boards/view.js";
import { createBoardsController } from "./boards/controller.js";
import { createAnnouncer } from "./announce.js";

const announce = createAnnouncer(document.getElementById("status"));
const model = createBoardsModel();
const view = createBoardsView(document.getElementById("app"));
createBoardsController(model, view, announce);
model.load();
```

- [ ] **Step 7: Verify the full loop in the browser**

Run: `dotnet run --project Wend.Api`, open `http://127.0.0.1:5174`, and check:
- Empty state shows "No boards yet".
- Add a board → it appears; the input clears and **keeps focus** (add another without reaching for the mouse).
- Rename → the prompt updates the title.
- Delete → the confirm removes it; empty state returns.
- After add/rename/delete, focus is on the **New board** field — never lost to the page.
- With a screen reader (or by watching `#status` in DevTools), each add/rename/delete **announces** ("Board added." etc.).
- Reload the page → boards persist (they're in SQLite).
- Tab through the form and buttons → visible focus rings; Rename/Delete read with the board name.

Stop with `Ctrl+C`.

- [ ] **Step 8: Commit**

```bash
git add Wend.Api/wwwroot/js/
git commit -m "Add boards MVC frontend: labelled controls, announcements, focus, error handling"
```

---

## Task 10: Acceptance pass + README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Full test run**

Run: `dotnet test`
Expected: PASS — all repository, smoke, and API tests green; 0 warnings on `dotnet build`.

- [ ] **Step 2: Manual acceptance** (`dotnet run --project Wend.Api`)
- Create / rename / delete boards all work and persist across a restart.
- The page is dark on first paint (no light flash).
- Keyboard-only: add, rename, delete, and navigate with visible focus; focus returns to the input after each action.
- Screen reader announces each add/rename/delete; Rename/Delete buttons read with the board name.
- Confirm the real DB now exists at `%LOCALAPPDATA%\Wend\data.db`.

- [ ] **Step 3: Update the README status** — `README.md`

Change the **Status** section to:

```markdown
## Status

**Slice 1 — local single-user board** (in progress). Boards work end to end: create, rename, and delete, saved to SQLite, accessible and dark-mode-first. Lists and cards come next.
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Boards CRUD working end to end; update README status"
```

---

## Definition of done

- `dotnet test` green: 5 repository tests, 2 smoke tests, 7 API tests.
- `dotnet build` clean (0 warnings).
- Boards can be created, renamed, and deleted from the browser and persist across restarts.
- Dark-mode-first; keyboard-operable with focus returned after every action; Rename/Delete have board-specific names; add/rename/delete announce via the `aria-live` region.
- Over-long and blank titles are rejected by the API; tests never touch the real DB.
- **Named trade-off:** schema is created with `EnsureCreated`; if it changes during Slice 1, delete `%LOCALAPPDATA%\Wend\data.db`. Migrations adopted at the Slice 1 → 2 boundary.
- The model → repository → API → frontend → test pattern (and the a11y foundation) is in place for Lists (Plan 2) to copy.
