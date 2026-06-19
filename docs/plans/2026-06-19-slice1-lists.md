# Wend Slice 1 — Plan 2: Lists Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Inside a board, create / rename / delete / **reorder** its lists, accessibly — adding the `List` entity, its repository + endpoints, and the board-view navigation the rest of Slice 1 builds on.

**Architecture:** A `List` entity (`Board ──1:*── List`) behind a new `IListRepository` seam (EF Core → SQLite), with `Position` as a 0-based contiguous index. Minimal-API endpoints mirror Plan 1's validated style; `GET /api/boards/{id}` grows a nested `lists` array. The frontend gains a small navigation coordinator (overview ↔ board view) and a `lists/` MVC module mirroring `boards/`. Backend is TDD'd with NUnit; the frontend is verified by a manual browser pass.

**Tech Stack:** `net10.0`, EF Core 10 SQLite, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`, vanilla JS (ES modules), the shared design-system.

**Reference:** Design spec at [`docs/2026-06-19-wend-lists-design.md`](../2026-06-19-wend-lists-design.md); Plan 1 at [`docs/plans/2026-06-16-slice1-foundation-boards.md`](2026-06-16-slice1-foundation-boards.md).

---

## Notes for the implementer

- **No build step.** Frontend is hand-authored JS/CSS in `Wend.Api/wwwroot`, served static. `dotnet run --project Wend.Api` is the only thing to run.
- **Delete the dev DB once before manual runs.** Slice 1 creates the schema with `EnsureCreated()`, which does **not** add the new `Lists` table to an existing `%LOCALAPPDATA%\Wend\data.db`. Before the first `dotnet run` in this plan, delete that file so it rebuilds with the `Lists` table: `Remove-Item "$env:LOCALAPPDATA\Wend\data.db" -ErrorAction SilentlyContinue`. (Tests are unaffected — each boots a throwaway DB.)
- **Tests never touch the real database.** API tests boot the app against a temp DB via `WendApiFactory` (Plan 1, Task 4). Repository tests use an in-memory SQLite connection.
- **The entity is named `List`.** It matches the domain ("a board's lists"). To avoid the eye-crossing `List<List>`, always use `var` for locals and `[]` for the navigation initialiser — never write `new List<List>()`. The non-generic `Wend.Core.List` and generic `System.Collections.Generic.List<T>` differ by arity, so they don't actually clash, but the discipline keeps it readable.
- **JSON casing.** ASP.NET Core serialises `{ Id, Title, Position }` as `{ "id", "title", "position" }` and binds request bodies case-insensitively, so the frontend uses `l.id` / `l.title` / `l.position` and posts `{ title }` / `{ position }`.
- **Cascade.** `List.BoardId` is a required FK, so deleting a board deletes its lists (EF Core default; SQLite enforces it because EF enables `PRAGMA foreign_keys = ON`).
- **Commits:** one per task, authored under your own account, **no co-author / no AI attribution** (house rule). Run from the repo root unless noted.

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Core/List.cs` | new | List entity (`Id`, `BoardId`, `Title`, `Position`) |
| `Wend.Core/Board.cs` | modify | Add `Lists` navigation collection |
| `Wend.Core/WendDbContext.cs` | modify | Add `Lists` DbSet |
| `Wend.Core/IListRepository.cs` | new | List persistence seam |
| `Wend.Core/EfListRepository.cs` | new | EF implementation (append / order / resequence / move) |
| `Wend.Api/ListEndpoints.cs` | new | `/api/boards/{id}/lists` + `/api/lists/{id}` group + DTOs + guard |
| `Wend.Api/BoardEndpoints.cs` | modify | `GET /{id}` returns the board with nested lists |
| `Wend.Api/Program.cs` | modify | Register `IListRepository`; map the list endpoints |
| `Wend.Api/wwwroot/js/main.js` | modify | Coordinator: overview ↔ board view, focus on transition |
| `Wend.Api/wwwroot/js/boards/view.js` | modify | Add an Open control + `focusOpen` |
| `Wend.Api/wwwroot/js/boards/controller.js` | modify | Forward the open action to `onOpen` |
| `Wend.Api/wwwroot/js/lists/model.js` | new | One board's lists: load + create/rename/delete/move |
| `Wend.Api/wwwroot/js/lists/view.js` | new | Renders the board view; forwards events; focus helpers |
| `Wend.Api/wwwroot/js/lists/controller.js` | new | Wires view → model; announces; focus; confirm; move maths |
| `Wend.Api/wwwroot/css/app.css` | modify | Board-view + lists layout (mobile-first) |
| `Wend.Tests/ListRepositoryTests.cs` | new | Repository unit tests (in-memory SQLite) |
| `Wend.Tests/ListApiTests.cs` | new | API integration tests |

---

## Task 1: List entity + Board.Lists + DbSet (with cascade)

**Files:**
- Create: `Wend.Core/List.cs`
- Modify: `Wend.Core/Board.cs`
- Modify: `Wend.Core/WendDbContext.cs`
- Test: `Wend.Tests/ListRepositoryTests.cs`

- [ ] **Step 1: Write the failing tests** — `Wend.Tests/ListRepositoryTests.cs`

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class ListRepositoryTests
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
    public async Task Saved_list_belongs_to_its_board_and_keeps_its_position()
    {
        var board = new Board { Title = "Sprint 1" };
        _db.Boards.Add(board);
        await _db.SaveChangesAsync();

        _db.Lists.Add(new List { BoardId = board.Id, Title = "To do", Position = 0 });
        await _db.SaveChangesAsync();

        var list = await _db.Lists.SingleAsync();
        Assert.That(list.Id, Is.GreaterThan(0));
        Assert.That(list.BoardId, Is.EqualTo(board.Id));
        Assert.That(list.Title, Is.EqualTo("To do"));
        Assert.That(list.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Deleting_a_board_cascades_to_its_lists()
    {
        var board = new Board { Title = "Temp" };
        _db.Boards.Add(board);
        await _db.SaveChangesAsync();
        _db.Lists.Add(new List { BoardId = board.Id, Title = "To do", Position = 0 });
        await _db.SaveChangesAsync();

        _db.Boards.Remove(board);
        await _db.SaveChangesAsync();

        Assert.That(await _db.Lists.AnyAsync(), Is.False);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ListRepositoryTests"`
Expected: FAIL — does not compile (`List` and `WendDbContext.Lists` don't exist).

- [ ] **Step 3: Create the entity** — `Wend.Core/List.cs`

```csharp
namespace Wend.Core;

/// <summary>A list (column) within a board — holds its ordering position and, later, cards.</summary>
public class List
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Title { get; set; } = "";
    public int Position { get; set; }
}
```

- [ ] **Step 4: Add the navigation collection** — `Wend.Core/Board.cs`

```csharp
namespace Wend.Core;

/// <summary>A board — the top-level container for its lists and cards.</summary>
public class Board
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    // A board's lists. Required FK on List.BoardId → deleting a board cascades to them.
    public ICollection<List> Lists { get; set; } = [];
}
```

- [ ] **Step 5: Add the DbSet** — `Wend.Core/WendDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>EF Core context for Wend's SQLite database.</summary>
public class WendDbContext(DbContextOptions<WendDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<List> Lists => Set<List>();
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ListRepositoryTests"`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add Wend.Core/List.cs Wend.Core/Board.cs Wend.Core/WendDbContext.cs Wend.Tests/ListRepositoryTests.cs
git commit -m "Add List entity with board cascade"
```

---

## Task 2: Repository — create (append) & list-for-board (ordered)

**Files:**
- Create: `Wend.Core/IListRepository.cs`
- Create: `Wend.Core/EfListRepository.cs`
- Modify: `Wend.Tests/ListRepositoryTests.cs`

- [ ] **Step 1: Add the repo fields + failing tests** — `Wend.Tests/ListRepositoryTests.cs`

Add two fields and initialise them at the end of `SetUp` (after `_db.Database.EnsureCreated();`):

```csharp
    private EfListRepository _repo = null!;
    private EfBoardRepository _boards = null!;
    // ...at the end of SetUp:
    _repo = new EfListRepository(_db);
    _boards = new EfBoardRepository(_db);
```

Add a helper and the tests inside the class:

```csharp
    private async Task<int> NewBoardAsync(string title = "Board") =>
        (await _boards.CreateBoardAsync(title)).Id;

    [Test]
    public async Task Create_appends_each_list_at_the_next_position()
    {
        var boardId = await NewBoardAsync();

        var first = await _repo.CreateListAsync(boardId, "To do");
        var second = await _repo.CreateListAsync(boardId, "Doing");

        Assert.That(first.Position, Is.EqualTo(0));
        Assert.That(second.Position, Is.EqualTo(1));
    }

    [Test]
    public async Task Get_lists_for_board_returns_them_in_position_order()
    {
        var boardId = await NewBoardAsync();
        await _repo.CreateListAsync(boardId, "To do");
        await _repo.CreateListAsync(boardId, "Doing");

        var lists = await _repo.GetListsForBoardAsync(boardId);

        Assert.That(lists.Select(l => l.Title), Is.EqualTo(new[] { "To do", "Doing" }));
    }

    [Test]
    public async Task Positions_count_from_zero_per_board()
    {
        var boardA = await NewBoardAsync("A");
        var boardB = await NewBoardAsync("B");

        var a1 = await _repo.CreateListAsync(boardA, "A1");
        var b1 = await _repo.CreateListAsync(boardB, "B1");

        Assert.That(a1.Position, Is.EqualTo(0));
        Assert.That(b1.Position, Is.EqualTo(0));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ListRepositoryTests"`
Expected: FAIL — `EfListRepository` / `CreateListAsync` / `GetListsForBoardAsync` don't exist.

- [ ] **Step 3: Define the seam** — `Wend.Core/IListRepository.cs`

```csharp
namespace Wend.Core;

/// <summary>
/// Persistence seam for lists within a board. Position is a 0-based contiguous index;
/// the repository keeps it gapless on create, delete and move.
/// </summary>
public interface IListRepository
{
    Task<IReadOnlyList<List>> GetListsForBoardAsync(int boardId);
    Task<List> CreateListAsync(int boardId, string title);
    Task<bool> RenameListAsync(int id, string newTitle);
    Task<bool> DeleteListAsync(int id);
    Task<bool> MoveListAsync(int id, int position);
}
```

- [ ] **Step 4: Implement create & list** — `Wend.Core/EfListRepository.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfListRepository(WendDbContext db) : IListRepository
{
    public async Task<IReadOnlyList<List>> GetListsForBoardAsync(int boardId) =>
        await db.Lists.Where(l => l.BoardId == boardId)
                      .OrderBy(l => l.Position)
                      .ToListAsync();

    public async Task<List> CreateListAsync(int boardId, string title)
    {
        // Append: the next position is the current count for this board.
        var position = await db.Lists.CountAsync(l => l.BoardId == boardId);
        var list = new List { BoardId = boardId, Title = title, Position = position };
        db.Lists.Add(list);
        await db.SaveChangesAsync();
        return list;
    }

    // Rename / Delete / Move arrive in Tasks 3-4.
    public Task<bool> RenameListAsync(int id, string newTitle) => throw new NotImplementedException();
    public Task<bool> DeleteListAsync(int id) => throw new NotImplementedException();
    public Task<bool> MoveListAsync(int id, int position) => throw new NotImplementedException();
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ListRepositoryTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add Wend.Core/IListRepository.cs Wend.Core/EfListRepository.cs Wend.Tests/ListRepositoryTests.cs
git commit -m "Add list create and ordered list-for-board to repository"
```

---

## Task 3: Repository — rename & delete (with resequence)

**Files:**
- Modify: `Wend.Core/EfListRepository.cs`
- Modify: `Wend.Tests/ListRepositoryTests.cs`

- [ ] **Step 1: Add the failing tests** — append to `ListRepositoryTests`

```csharp
    [Test]
    public async Task Rename_changes_the_title_and_reports_missing()
    {
        var boardId = await NewBoardAsync();
        var list = await _repo.CreateListAsync(boardId, "Old");

        Assert.That(await _repo.RenameListAsync(list.Id, "New"), Is.True);
        var lists = await _repo.GetListsForBoardAsync(boardId);
        Assert.That(lists.Single().Title, Is.EqualTo("New"));
        Assert.That(await _repo.RenameListAsync(9999, "X"), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_list_and_resequences_the_rest()
    {
        var boardId = await NewBoardAsync();
        await _repo.CreateListAsync(boardId, "A");           // 0
        var b = await _repo.CreateListAsync(boardId, "B");   // 1
        await _repo.CreateListAsync(boardId, "C");           // 2

        Assert.That(await _repo.DeleteListAsync(b.Id), Is.True);

        var lists = await _repo.GetListsForBoardAsync(boardId);
        Assert.That(lists.Select(l => l.Title), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(lists.Select(l => l.Position), Is.EqualTo(new[] { 0, 1 })); // gapless
    }

    [Test]
    public async Task Delete_reports_missing()
    {
        Assert.That(await _repo.DeleteListAsync(9999), Is.False);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ListRepositoryTests"`
Expected: FAIL — `NotImplementedException` from the rename/delete stubs.

- [ ] **Step 3: Replace the rename & delete stubs** — `Wend.Core/EfListRepository.cs`

Replace the `RenameListAsync` and `DeleteListAsync` stub lines with:

```csharp
    public async Task<bool> RenameListAsync(int id, string newTitle)
    {
        var list = await db.Lists.FindAsync(id);
        if (list is null) return false;
        list.Title = newTitle;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteListAsync(int id)
    {
        var list = await db.Lists.FindAsync(id);
        if (list is null) return false;
        db.Lists.Remove(list);
        await db.SaveChangesAsync();
        await ResequenceAsync(list.BoardId); // keep the survivors gapless (0,1,2,…)
        return true;
    }

    // Rewrites a board's list positions to a gapless 0-based sequence in current order.
    private async Task ResequenceAsync(int boardId)
    {
        var lists = await db.Lists.Where(l => l.BoardId == boardId)
                                  .OrderBy(l => l.Position)
                                  .ToListAsync();
        for (var i = 0; i < lists.Count; i++) lists[i].Position = i;
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ListRepositoryTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Core/EfListRepository.cs Wend.Tests/ListRepositoryTests.cs
git commit -m "Add list rename and delete with position resequence"
```

---

## Task 4: Repository — move (clamp & resequence)

**Files:**
- Modify: `Wend.Core/EfListRepository.cs`
- Modify: `Wend.Tests/ListRepositoryTests.cs`

- [ ] **Step 1: Add the failing tests** — append to `ListRepositoryTests`

```csharp
    [Test]
    public async Task Move_reorders_within_the_board_and_resequences()
    {
        var boardId = await NewBoardAsync();
        var a = await _repo.CreateListAsync(boardId, "A"); // 0
        await _repo.CreateListAsync(boardId, "B");          // 1
        await _repo.CreateListAsync(boardId, "C");          // 2

        Assert.That(await _repo.MoveListAsync(a.Id, 2), Is.True);

        var lists = await _repo.GetListsForBoardAsync(boardId);
        Assert.That(lists.Select(l => l.Title), Is.EqualTo(new[] { "B", "C", "A" }));
        Assert.That(lists.Select(l => l.Position), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public async Task Move_clamps_an_out_of_range_position()
    {
        var boardId = await NewBoardAsync();
        var a = await _repo.CreateListAsync(boardId, "A");
        await _repo.CreateListAsync(boardId, "B");

        Assert.That(await _repo.MoveListAsync(a.Id, 99), Is.True);

        var lists = await _repo.GetListsForBoardAsync(boardId);
        Assert.That(lists.Select(l => l.Title), Is.EqualTo(new[] { "B", "A" }));
    }

    [Test]
    public async Task Move_reports_missing_list()
    {
        Assert.That(await _repo.MoveListAsync(9999, 0), Is.False);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ListRepositoryTests"`
Expected: FAIL — `NotImplementedException` from the move stub.

- [ ] **Step 3: Replace the move stub** — `Wend.Core/EfListRepository.cs`

Replace the `MoveListAsync` stub line with:

```csharp
    public async Task<bool> MoveListAsync(int id, int position)
    {
        var list = await db.Lists.FindAsync(id);
        if (list is null) return false;

        // Pull the board's lists in order, lift this one out, drop it back at the
        // clamped target index, then renumber so positions stay gapless.
        var siblings = await db.Lists.Where(l => l.BoardId == list.BoardId)
                                     .OrderBy(l => l.Position)
                                     .ToListAsync();
        siblings.Remove(siblings.First(l => l.Id == id));
        var target = Math.Clamp(position, 0, siblings.Count);
        siblings.Insert(target, list);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ListRepositoryTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Core/EfListRepository.cs Wend.Tests/ListRepositoryTests.cs
git commit -m "Add list move with clamp and resequence"
```

---

## Task 5: Wire the repo + create endpoint (POST)

**Files:**
- Create: `Wend.Api/ListEndpoints.cs`
- Modify: `Wend.Api/Program.cs`
- Test: `Wend.Tests/ListApiTests.cs`

- [ ] **Step 1: Write the failing tests** — `Wend.Tests/ListApiTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class ListApiTests
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

    // Test-only shapes for the JSON the API returns.
    private record BoardDto(int Id, string Title);
    private record ListDto(int Id, string Title, int Position);
    private record BoardDetailDto(int Id, string Title, List<ListDto> Lists);

    private async Task<BoardDto> CreateBoardAsync(string title)
    {
        var res = await _client.PostAsJsonAsync("/api/boards", new { title });
        return (await res.Content.ReadFromJsonAsync<BoardDto>())!;
    }

    private async Task<ListDto> CreateListAsync(int boardId, string title)
    {
        var res = await _client.PostAsJsonAsync($"/api/boards/{boardId}/lists", new { title });
        return (await res.Content.ReadFromJsonAsync<ListDto>())!;
    }

    [Test]
    public async Task Posting_a_list_creates_it_at_the_next_position()
    {
        var board = await CreateBoardAsync("Sprint 1");

        var response = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/lists", new { title = "To do" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await response.Content.ReadFromJsonAsync<ListDto>();
        Assert.That(created!.Title, Is.EqualTo("To do"));
        Assert.That(created.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Posting_a_blank_list_title_is_rejected()
    {
        var board = await CreateBoardAsync("Sprint 1");
        var response = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/lists", new { title = "   " });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_over_long_list_title_is_rejected()
    {
        var board = await CreateBoardAsync("Sprint 1");
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/lists", new { title = new string('x', 201) });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_a_list_to_a_missing_board_is_404()
    {
        var response = await _client.PostAsJsonAsync("/api/boards/9999/lists", new { title = "X" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ListApiTests"`
Expected: FAIL — `POST /api/boards/{id}/lists` returns 404 (no route).

- [ ] **Step 3: Create the endpoint group** — `Wend.Api/ListEndpoints.cs`

```csharp
using Wend.Core;

namespace Wend.Api;

public static class ListEndpoints
{
    private const int MaxTitleLength = 200;

    public static IEndpointRouteBuilder MapListEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/boards/{boardId:int}/lists",
            async (int boardId, CreateListRequest req, IBoardRepository boards, IListRepository lists) =>
            {
                var title = req.Title?.Trim() ?? "";
                if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
                if (await boards.GetBoardAsync(boardId) is null) return Results.NotFound();
                var list = await lists.CreateListAsync(boardId, title);
                return Results.Created($"/api/lists/{list.Id}", list);
            });

        // PUT (rename), DELETE and move arrive in Tasks 7-8.
        return app;
    }
}

public record CreateListRequest(string Title);
```

- [ ] **Step 4: Register the repo + map the group** — `Wend.Api/Program.cs`

After the line `builder.Services.AddScoped<IBoardRepository, EfBoardRepository>();` add:

```csharp
builder.Services.AddScoped<IListRepository, EfListRepository>();
```

After the line `app.MapGroup("/api/boards").MapBoardEndpoints();` add:

```csharp
app.MapListEndpoints();
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ListApiTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/ListEndpoints.cs Wend.Api/Program.cs Wend.Tests/ListApiTests.cs
git commit -m "Wire IListRepository and expose POST list create"
```

---

## Task 6: Nest lists in the board detail (GET /api/boards/{id})

**Files:**
- Modify: `Wend.Api/BoardEndpoints.cs`
- Modify: `Wend.Tests/ListApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append the two methods inside `ListApiTests`

```csharp
    [Test]
    public async Task Board_detail_includes_its_lists_in_order()
    {
        var board = await CreateBoardAsync("Sprint");
        await CreateListAsync(board.Id, "To do");
        await CreateListAsync(board.Id, "Doing");

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");

        Assert.That(detail!.Title, Is.EqualTo("Sprint"));
        Assert.That(detail.Lists.Select(l => l.Title), Is.EqualTo(new[] { "To do", "Doing" }));
        Assert.That(detail.Lists.Select(l => l.Position), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public async Task Board_detail_for_a_missing_board_is_404()
    {
        var res = await _client.GetAsync("/api/boards/9999");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
```

- [ ] **Step 2: Run to verify the first one fails**

Run: `dotnet test --filter "FullyQualifiedName~ListApiTests.Board_detail_includes_its_lists_in_order"`
Expected: FAIL — Plan 1's response carries no `lists`, so `detail.Lists` comes back null and the `Lists` assertions throw. (The separate 404 test already passes against Plan 1's endpoint; it stays green to prove we didn't regress it.)

- [ ] **Step 3: Return the board with its lists** — `Wend.Api/BoardEndpoints.cs`

Replace the existing `group.MapGet("/{id:int}", …)` handler with:

```csharp
        group.MapGet("/{id:int}", async (int id, IBoardRepository boards, IListRepository lists) =>
        {
            if (await boards.GetBoardAsync(id) is not { } board) return Results.NotFound();
            var summaries = (await lists.GetListsForBoardAsync(id))
                .Select(l => new ListSummary(l.Id, l.Title, l.Position))
                .ToList();
            return Results.Ok(new BoardDetail(board.Id, board.Title, summaries));
        });
```

Add at the bottom of the file (outside the class), next to `CreateBoardRequest` / `RenameBoardRequest`:

```csharp
public record BoardDetail(int Id, string Title, IReadOnlyList<ListSummary> Lists);
public record ListSummary(int Id, string Title, int Position);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ListApiTests"`
Expected: PASS (6 tests). Also run `dotnet test --filter "FullyQualifiedName~BoardApiTests"` → still PASS (the board GET/PUT tests read `id`/`title`, which the new shape still carries).

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/BoardEndpoints.cs Wend.Tests/ListApiTests.cs
git commit -m "Return boards with their nested lists"
```

---

## Task 7: Rename (PUT) & delete (DELETE) endpoints

**Files:**
- Modify: `Wend.Api/ListEndpoints.cs`
- Modify: `Wend.Tests/ListApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append inside `ListApiTests`

```csharp
    [Test]
    public async Task Put_renames_a_list()
    {
        var board = await CreateBoardAsync("B");
        var list = await CreateListAsync(board.Id, "Old");

        var put = await _client.PutAsJsonAsync($"/api/lists/{list.Id}", new { title = "New" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists.Single().Title, Is.EqualTo("New"));
    }

    [Test]
    public async Task Put_rejects_a_blank_list_title()
    {
        var board = await CreateBoardAsync("B");
        var list = await CreateListAsync(board.Id, "Old");

        var put = await _client.PutAsJsonAsync($"/api/lists/{list.Id}", new { title = "  " });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Put_to_a_missing_list_is_404()
    {
        var put = await _client.PutAsJsonAsync("/api/lists/9999", new { title = "X" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_removes_a_list()
    {
        var board = await CreateBoardAsync("B");
        var list = await CreateListAsync(board.Id, "Temp");

        var del = await _client.DeleteAsync($"/api/lists/{list.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists, Is.Empty);
    }

    [Test]
    public async Task Delete_a_missing_list_is_404()
    {
        var del = await _client.DeleteAsync("/api/lists/9999");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ListApiTests"`
Expected: FAIL — `PUT` / `DELETE /api/lists/{id}` return 404 (no routes).

- [ ] **Step 3: Add the routes + rename DTO** — `Wend.Api/ListEndpoints.cs`

Inside `MapListEndpoints`, before `return app;`, add:

```csharp
        app.MapPut("/api/lists/{id:int}", async (int id, RenameListRequest req, IListRepository lists) =>
        {
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            return await lists.RenameListAsync(id, title) ? Results.NoContent() : Results.NotFound();
        });

        app.MapDelete("/api/lists/{id:int}", async (int id, IListRepository lists) =>
            await lists.DeleteListAsync(id) ? Results.NoContent() : Results.NotFound());
```

Add at the bottom of the file (outside the class), next to `CreateListRequest`:

```csharp
public record RenameListRequest(string Title);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ListApiTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/ListEndpoints.cs Wend.Tests/ListApiTests.cs
git commit -m "Add PUT rename and DELETE for lists"
```

---

## Task 8: Move endpoint (PUT /api/lists/{id}/move)

**Files:**
- Modify: `Wend.Api/ListEndpoints.cs`
- Modify: `Wend.Tests/ListApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append inside `ListApiTests`

```csharp
    [Test]
    public async Task Moving_a_list_reorders_and_resequences()
    {
        var board = await CreateBoardAsync("Sprint");
        var todo = await CreateListAsync(board.Id, "To do"); // 0
        await CreateListAsync(board.Id, "Doing");            // 1
        await CreateListAsync(board.Id, "Done");             // 2

        var move = await _client.PutAsJsonAsync($"/api/lists/{todo.Id}/move", new { position = 2 });
        Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists.Select(l => l.Title), Is.EqualTo(new[] { "Doing", "Done", "To do" }));
        Assert.That(detail.Lists.Select(l => l.Position), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public async Task Moving_past_the_end_clamps_to_the_last_position()
    {
        var board = await CreateBoardAsync("Sprint");
        var a = await CreateListAsync(board.Id, "A");
        await CreateListAsync(board.Id, "B");

        var move = await _client.PutAsJsonAsync($"/api/lists/{a.Id}/move", new { position = 99 });
        Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists.Select(l => l.Title), Is.EqualTo(new[] { "B", "A" }));
    }

    [Test]
    public async Task Moving_a_missing_list_is_404()
    {
        var move = await _client.PutAsJsonAsync("/api/lists/9999/move", new { position = 0 });
        Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ListApiTests"`
Expected: FAIL — `PUT /api/lists/{id}/move` returns 404 (no route).

- [ ] **Step 3: Add the move route + DTO** — `Wend.Api/ListEndpoints.cs`

Inside `MapListEndpoints`, before `return app;`, add:

```csharp
        app.MapPut("/api/lists/{id:int}/move", async (int id, MoveListRequest req, IListRepository lists) =>
            await lists.MoveListAsync(id, req.Position) ? Results.NoContent() : Results.NotFound());
```

Add at the bottom of the file (outside the class):

```csharp
public record MoveListRequest(int Position);
```

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: PASS — all tests (Plan 1's 14 + this plan's repository and list-API tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/ListEndpoints.cs Wend.Tests/ListApiTests.cs
git commit -m "Add PUT list move endpoint"
```

---

## Task 9: Frontend navigation — overview ↔ board view

**Files:**
- Modify: `Wend.Api/wwwroot/js/boards/view.js`
- Modify: `Wend.Api/wwwroot/js/boards/controller.js`
- Modify: `Wend.Api/wwwroot/js/main.js`
- Modify: `Wend.Api/wwwroot/css/app.css`

No automated test — verify by eye in the browser. This task wires opening a board and coming back, with focus managed; the board view shows a placeholder until Task 10 fills in the lists.

- [ ] **Step 1: Add an Open control to the boards view** — replace `Wend.Api/wwwroot/js/boards/view.js` with:

```js
// Renders boards (labelled controls) and forwards events via data-action. No fetch, no logic.
export function createBoardsView(root) {
  function render(boards) {
    const items = boards.length
      ? boards
          .map(
            (b) => `
        <li>
          <button class="board-open" data-action="open" data-id="${b.id}"
            aria-label="Open board: ${escapeHtml(b.title)}">${escapeHtml(b.title)}</button>
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

  function focusNewBoardInput() {
    root.querySelector(".board-form input")?.focus();
  }

  // Send focus to a specific board's Open button (used when returning from its view).
  function focusOpen(id) {
    root.querySelector(`[data-action="open"][data-id="${id}"]`)?.focus();
  }

  function bindActions(handlers) {
    root.addEventListener("submit", async (e) => {
      if (e.target.dataset.action !== "create") return;
      e.preventDefault();
      const title = e.target.title.value.trim();
      const submit = e.target.querySelector("button[type=submit]");
      submit.disabled = true;
      try {
        await handlers.create(title);
      } finally {
        submit.disabled = false;
      }
    });
    root.addEventListener("click", (e) => {
      const btn = e.target.closest("button[data-action]");
      if (!btn || btn.dataset.action === "create") return;
      const id = Number(btn.dataset.id);
      if (btn.dataset.action === "open") handlers.open(id);
      else if (btn.dataset.action === "rename") handlers.rename(id);
      else if (btn.dataset.action === "delete") handlers.delete(id);
    });
  }

  return { render, focusNewBoardInput, focusOpen, bindActions };
}

function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]
  );
}
```

- [ ] **Step 2: Forward the open action** — replace `Wend.Api/wwwroot/js/boards/controller.js` with:

```js
// Wires board actions to the model, announces results, returns focus, surfaces failures.
// onOpen(boardId) is called when a board is opened — main.js navigates to its view.
export function createBoardsController(model, view, announce, { onOpen } = {}) {
  view.bindActions({
    open: (id) => onOpen?.(id),
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

- [ ] **Step 3: Make main.js a coordinator** — replace `Wend.Api/wwwroot/js/main.js` with:

```js
import { createBoardsModel } from "./boards/model.js";
import { createBoardsView } from "./boards/view.js";
import { createBoardsController } from "./boards/controller.js";
import { createAnnouncer } from "./announce.js";

const announce = createAnnouncer(document.getElementById("status"));
const app = document.getElementById("app");

// Each navigation mounts its module on a FRESH root element. The previous module's
// delegated listeners are discarded with the old element — no cross-talk, no leaks.
function mount(build) {
  app.replaceChildren();
  const root = document.createElement("div");
  app.append(root);
  build(root);
}

function showOverview(focusBoardId) {
  mount((root) => {
    const model = createBoardsModel();
    const view = createBoardsView(root);
    createBoardsController(model, view, announce, { onOpen: showBoard });
    // After (re)load, return focus to the board we came back from — but not on first paint.
    model.load().then(() => {
      if (focusBoardId) view.focusOpen(focusBoardId);
    });
  });
}

// Placeholder board view — Task 10 swaps in the real lists module.
function showBoard(boardId) {
  mount((root) => {
    root.innerHTML = `
      <div class="board-view">
        <button class="back-link" data-action="back">← Boards</button>
        <h2 class="board-heading" tabindex="-1">Board #${boardId}</h2>
        <p class="empty">Lists coming in the next step…</p>
      </div>`;
    root.querySelector('[data-action="back"]')
        .addEventListener("click", () => showOverview(boardId));
    root.querySelector(".board-heading").focus();
  });
}

showOverview(); // first paint: no forced focus, skip link is available
```

- [ ] **Step 4: Style the board view + open control** — append to `Wend.Api/wwwroot/css/app.css`:

```css
/* Board overview — the board name is a button that opens the board. */
.board-open {
  flex: 1;
  text-align: left;
}

/* Board view (one board's lists). */
.board-view { display: flex; flex-direction: column; }

.back-link { align-self: flex-start; margin-bottom: 1rem; }

.board-heading {
  margin: 0 0 1rem;
  font-size: 1.25rem;
}
```

- [ ] **Step 5: Verify navigation in the browser**

If you haven't yet this plan, delete the dev DB so the `Lists` table is created:
`Remove-Item "$env:LOCALAPPDATA\Wend\data.db" -ErrorAction SilentlyContinue`

Run: `dotnet run --project Wend.Api`, open `http://127.0.0.1:5174`, and check:
- Add a board, then click its name → the view swaps to "Board #<id>" with a **← Boards** link; focus lands on the heading.
- Click **← Boards** → back to the overview; focus lands on that board's **Open** button (Tab is not needed to find your place).
- Keyboard-only: the board name, Rename, Delete and the back link are all reachable with visible focus rings.

Stop with `Ctrl+C`.

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/wwwroot/js/ Wend.Api/wwwroot/css/app.css
git commit -m "Add board-view navigation with focus management"
```

---

## Task 10: Frontend lists module — create / rename / delete / reorder

**Files:**
- Create: `Wend.Api/wwwroot/js/lists/model.js`
- Create: `Wend.Api/wwwroot/js/lists/view.js`
- Create: `Wend.Api/wwwroot/js/lists/controller.js`
- Modify: `Wend.Api/wwwroot/js/main.js`
- Modify: `Wend.Api/wwwroot/css/app.css`

No automated test — verify by browser pass (final step). Mirrors the `boards/` MVC trio.

- [ ] **Step 1: Model (one board's lists)** — `Wend.Api/wwwroot/js/lists/model.js`

```js
import { api } from "../api.js";

// State + data for a single board's lists. Re-fetches the board detail after each change
// so positions always come straight from the server. No DOM. Subscribers notified on change.
export function createListsModel(boardId) {
  let board = { id: boardId, title: "", lists: [] };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(board));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(board);
    },
    async load() {
      board = await api(`/api/boards/${boardId}`);
      notify();
    },
    async create(title) {
      await api(`/api/boards/${boardId}/lists`, { method: "POST", body: JSON.stringify({ title }) });
      await this.load();
    },
    async rename(id, title) {
      await api(`/api/lists/${id}`, { method: "PUT", body: JSON.stringify({ title }) });
      await this.load();
    },
    async remove(id) {
      await api(`/api/lists/${id}`, { method: "DELETE" });
      await this.load();
    },
    async move(id, position) {
      await api(`/api/lists/${id}/move`, { method: "PUT", body: JSON.stringify({ position }) });
      await this.load();
    },
  };
}
```

- [ ] **Step 2: View (board view + list controls)** — `Wend.Api/wwwroot/js/lists/view.js`

```js
// Renders one board's view: back link, title, add-list form, and the lists with
// move/rename/delete controls. Forwards events via data-action. No fetch, no logic.
export function createListsView(root) {
  function render(board) {
    const lists = board.lists;
    const items = lists.length
      ? lists
          .map((l, i) => {
            const first = i === 0;
            const last = i === lists.length - 1;
            return `
        <li class="list-card" data-list-id="${l.id}">
          <span class="list-title">${escapeHtml(l.title)}</span>
          <div class="list-actions">
            <button data-action="move-left" data-id="${l.id}" ${first ? "disabled" : ""}
              aria-label="Move list left: ${escapeHtml(l.title)}">◀</button>
            <button data-action="move-right" data-id="${l.id}" ${last ? "disabled" : ""}
              aria-label="Move list right: ${escapeHtml(l.title)}">▶</button>
            <button data-action="rename" data-id="${l.id}"
              aria-label="Rename list: ${escapeHtml(l.title)}">Rename</button>
            <button data-action="delete" data-id="${l.id}"
              aria-label="Delete list: ${escapeHtml(l.title)}">Delete</button>
          </div>
        </li>`;
          })
          .join("")
      : `<li class="empty">No lists yet — add one above.</li>`;

    root.innerHTML = `
      <div class="board-view">
        <button class="back-link" data-action="back">← Boards</button>
        <h2 class="board-heading" tabindex="-1">${escapeHtml(board.title)}</h2>
        <form class="list-form" data-action="create">
          <input name="title" aria-label="New list name" placeholder="New list…" required />
          <button type="submit">Add list</button>
        </form>
        <ul class="list-columns">${items}</ul>
      </div>`;
  }

  function focusHeading() {
    root.querySelector(".board-heading")?.focus();
  }
  function focusNewListInput() {
    root.querySelector(".list-form input")?.focus();
  }

  // After a move, land focus on a sensible enabled control in the moved list (the move
  // button just pressed may now be disabled at an end), so keyboard users keep their place.
  function focusListAction(id, preferred) {
    const card = root.querySelector(`.list-card[data-list-id="${id}"]`);
    if (!card) return;
    const order = preferred === "move-left"
      ? ["move-left", "move-right", "rename"]
      : ["move-right", "move-left", "rename"];
    for (const action of order) {
      const btn = card.querySelector(`[data-action="${action}"]`);
      if (btn && !btn.disabled) { btn.focus(); return; }
    }
  }

  function bindActions(handlers) {
    root.addEventListener("submit", async (e) => {
      if (e.target.dataset.action !== "create") return;
      e.preventDefault();
      const title = e.target.title.value.trim();
      const submit = e.target.querySelector("button[type=submit]");
      submit.disabled = true;
      try {
        await handlers.create(title);
      } finally {
        submit.disabled = false;
      }
    });
    root.addEventListener("click", (e) => {
      const btn = e.target.closest("button[data-action]");
      if (!btn || btn.dataset.action === "create") return;
      const action = btn.dataset.action;
      if (action === "back") return handlers.back();
      const id = Number(btn.dataset.id);
      if (action === "rename") handlers.rename(id);
      else if (action === "delete") handlers.delete(id);
      else if (action === "move-left") handlers.moveLeft(id);
      else if (action === "move-right") handlers.moveRight(id);
    });
  }

  return { render, focusHeading, focusNewListInput, focusListAction, bindActions };
}

function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]
  );
}
```

- [ ] **Step 3: Controller (wire, announce, focus, move maths)** — `Wend.Api/wwwroot/js/lists/controller.js`

```js
// Wires the board view to the model: announces results, manages focus, confirms deletes,
// and turns move-left/right into a target position. onBack() returns to the overview.
export function createListsController(model, view, announce, { onBack } = {}) {
  let lists = [];

  view.bindActions({
    back: () => onBack?.(),
    create: async (title) => {
      if (!title) return;
      try {
        await model.create(title);
        announce("List added.");
        view.focusNewListInput();
      } catch {
        announce("Couldn't add the list — please try again.");
      }
    },
    rename: async (id) => {
      const title = prompt("New list name?");
      if (!title || !title.trim()) return;
      try {
        await model.rename(id, title.trim());
        announce("List renamed.");
        view.focusNewListInput();
      } catch {
        announce("Couldn't rename the list — please try again.");
      }
    },
    delete: async (id) => {
      if (!confirm("Delete this list?")) return;
      try {
        await model.remove(id);
        announce("List deleted.");
        view.focusNewListInput();
      } catch {
        announce("Couldn't delete the list — please try again.");
      }
    },
    moveLeft: (id) => move(id, -1, "move-left"),
    moveRight: (id) => move(id, +1, "move-right"),
  });

  async function move(id, delta, action) {
    const index = lists.findIndex((l) => l.id === id);
    if (index < 0) return;
    const target = index + delta;
    if (target < 0 || target >= lists.length) return; // already at an end (button is disabled)
    try {
      await model.move(id, target);
      announce(delta < 0 ? "List moved left." : "List moved right.");
      view.focusListAction(id, action);
    } catch {
      announce("Couldn't move the list — please try again.");
    }
  }

  model.subscribe((board) => {
    lists = board.lists;
    view.render(board);
  });
}
```

- [ ] **Step 4: Use the lists module in main.js** — in `Wend.Api/wwwroot/js/main.js`, add these imports under the existing ones:

```js
import { createListsModel } from "./lists/model.js";
import { createListsView } from "./lists/view.js";
import { createListsController } from "./lists/controller.js";
```

Replace the placeholder `showBoard` function with:

```js
function showBoard(boardId) {
  mount((root) => {
    const model = createListsModel(boardId);
    const view = createListsView(root);
    createListsController(model, view, announce, { onBack: () => showOverview(boardId) });
    model.load().then(() => view.focusHeading());
  });
}
```

- [ ] **Step 5: Style the lists** — append to `Wend.Api/wwwroot/css/app.css`:

```css
.list-form {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1.5rem;
}
.list-form input { flex: 1; }

/* Lists: stacked on mobile, horizontal columns on wider screens (mobile-first). */
.list-columns {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.list-card {
  border: 1px solid;
  border-color: color-mix(in srgb, currentColor 15%, transparent);
  border-radius: 0.5rem;
  padding: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.list-title { font-weight: 600; }

.list-actions {
  display: flex;
  gap: 0.25rem;
  flex-wrap: wrap;
}

@media (min-width: 768px) {
  .list-columns {
    flex-direction: row;
    align-items: flex-start;
    overflow-x: auto;
  }
  .list-card { flex: 0 0 16rem; }
}
```

- [ ] **Step 6: Verify the full loop in the browser**

Run: `dotnet run --project Wend.Api`, open `http://127.0.0.1:5174`, open a board, and check:
- Empty state shows "No lists yet". Add a list → it appears; the input clears and **keeps focus** (add several without the mouse).
- Rename → the prompt updates the title.
- Delete → the confirm removes it.
- **Move left / right** reorder the list; the end buttons are disabled at the first/last position; after a move, focus stays on a control in the moved list; each move **announces** ("List moved left.").
- On a phone width lists stack; at ≥768px they become horizontal columns.
- **← Boards** returns to the overview with focus on the board's Open button.
- Reload → lists persist and keep their order (they're in SQLite).
- With a screen reader (or watching `#status` in DevTools), add/rename/delete/move each announce.

Stop with `Ctrl+C`.

- [ ] **Step 7: Commit**

```bash
git add Wend.Api/wwwroot/js/ Wend.Api/wwwroot/css/app.css
git commit -m "Add lists MVC: create, rename, delete and accessible reorder"
```

---

## Task 11: Acceptance pass + README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Full test run**

Run: `dotnet test`
Expected: PASS — Plan 1's 14 tests plus this plan's 11 repository and 14 list-API tests; 0 warnings on `dotnet build`.

- [ ] **Step 2: Manual acceptance** (`dotnet run --project Wend.Api`)
- Open a board; create / rename / delete / reorder its lists; all persist across a restart.
- The page is dark on first paint (no light flash).
- Keyboard-only: open a board, add/rename/delete/reorder lists, and return — visible focus throughout, focus returns sensibly after each action.
- Screen reader announces open/back, add/rename/delete and each move; move buttons are disabled at the ends.
- Phone width stacks the lists; ≥768px shows horizontal columns.

- [ ] **Step 3: Update the README status** — `README.md`

Change the **Status** section to:

```markdown
## Status

**Slice 1 — local single-user board** (in progress). Boards and lists work end to end: create, rename, delete, and reorder lists inside a board, saved to SQLite, accessible and dark-mode-first. Cards come next.
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Lists working end to end; update README status"
```

---

## Definition of done

- `dotnet test` green: Plan 1's 14 tests + 11 list-repository tests + 14 list-API tests.
- `dotnet build` clean (0 warnings).
- A board opens into its own view; lists can be created, renamed, deleted and reordered from the browser and persist across restarts.
- `GET /api/boards/{id}` returns the board with its lists in position order; positions stay gapless (0,1,2,…) through create, delete and move; out-of-range moves clamp.
- Deleting a board cascades to its lists; blank and over-long list titles are rejected; tests never touch the real DB.
- Dark-mode-first; keyboard-operable with focus managed across navigation and after each action; move/CRUD announce via the `aria-live` region; list delete is confirmed.
- **Named trade-off (carried from Plan 1):** schema is created with `EnsureCreated`; the dev DB at `%LOCALAPPDATA%\Wend\data.db` must be deleted once so the `Lists` table is added. Migrations are adopted at the Slice 1 → 2 boundary.
- The accessible move-button pattern (buttons + a single `/move` endpoint + announcements) is in place for Plan 5 (card moves) to reuse.

