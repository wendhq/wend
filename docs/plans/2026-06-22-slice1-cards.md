# Wend Slice 1 — Plan 3: Cards + task view Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Inside a list, create / read / edit / delete its cards, plus a focused **task view** screen — adding the `Card` entity, its repository + endpoints, and a third navigation screen (Overview → Board → Card).

**Architecture:** A `Card` entity (`List ──1:*── Card`) behind a new `ICardRepository` seam (EF Core → SQLite), with `Position` as a 0-based contiguous index and the full Slice-1 column set created up front (lifecycle timestamps carried, unused). Minimal-API endpoints mirror the validated style of Plans 1–2; `GET /api/boards/{id}` grows nested `cards`. The frontend renames the `lists/` module to `board/`, renders cards inside it, and gains a `card/` MVC trio for the task view. Backend is TDD'd with NUnit; the frontend is verified by a manual browser pass.

**Tech Stack:** `net10.0`, EF Core 10 SQLite, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`, vanilla JS (ES modules), the shared design-system.

**Reference:** Design spec at [`docs/2026-06-22-wend-cards-design.md`](../2026-06-22-wend-cards-design.md); Plan 2 at [`docs/plans/2026-06-19-slice1-lists.md`](2026-06-19-slice1-lists.md).

---

## Notes for the implementer

- **No build step.** Frontend is hand-authored JS/CSS in `Wend.Api/wwwroot`, served static. `dotnet run --project Wend.Api` is the only thing to run.
- **Delete the dev DB once before manual runs.** Slice 1 creates the schema with `EnsureCreated()`, which does **not** add the new `Cards` table to an existing `%LOCALAPPDATA%\Wend\data.db`. Before the first `dotnet run` in this plan, delete that file so it rebuilds with the `Cards` table: `Remove-Item "$env:LOCALAPPDATA\Wend\data.db" -ErrorAction SilentlyContinue`. (Tests are unaffected — each boots a throwaway DB.)
- **Tests never touch the real database.** API tests boot the app against a temp DB via `WendApiFactory`; repository tests use an in-memory SQLite connection.
- **Cascade mirrors Plan 2.** A `List.Cards` navigation collection + the required `Card.ListId` FK give EF its default cascade (just like `Board.Lists`). Deleting a list deletes its cards; deleting a board already deletes its lists (Plan 2), so the whole chain cleans up. SQLite enforces it because EF enables `PRAGMA foreign_keys = ON`.
- **Query filter.** `Card` carries a global query filter `DeletedAt == null && ArchivedAt == null`, so every LINQ query over `db.Cards` hides soft-deleted / archived cards. Nothing sets those timestamps in Plan 3 — the filter is inert but correct, ready for Plans 6–7. (`FindAsync` bypasses query filters by design; that's fine here.)
- **Dates.** `DueDate` is a `DateOnly?`. System.Text.Json serialises it as `"2026-06-25"` and binds the same string back; the HTML `<input type="date">` value is exactly that format. An empty date field posts `null`.
- **JSON casing.** ASP.NET Core serialises `{ Id, Title, … }` as `{ "id", "title", … }` and binds request bodies case-insensitively, so the frontend uses `c.id` / `c.title` / `c.dueDate` and posts `{ title }` / `{ title, description, dueDate }`.
- **Commits:** one per task, authored under your own account, **no co-author / no AI attribution** (house rule). Run from the repo root unless noted.

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Core/Card.cs` | new | Card entity (core fields + carried lifecycle timestamps) |
| `Wend.Core/List.cs` | modify | Add `Cards` navigation collection (cascade) |
| `Wend.Core/WendDbContext.cs` | modify | Add `Cards` DbSet + `OnModelCreating` query filter |
| `Wend.Core/ICardRepository.cs` | new | Card persistence seam |
| `Wend.Core/EfCardRepository.cs` | new | EF implementation (append / order / get / edit / delete+resequence) |
| `Wend.Core/IListRepository.cs` | modify | Add `GetListAsync(int id)` (for the card detail's list name) |
| `Wend.Core/EfListRepository.cs` | modify | Implement `GetListAsync` |
| `Wend.Api/CardEndpoints.cs` | new | `/api/lists/{id}/cards` + `/api/cards/{id}` group + DTOs + guards |
| `Wend.Api/BoardEndpoints.cs` | modify | `GET /{id}` nests each list's cards |
| `Wend.Api/Program.cs` | modify | Register `ICardRepository`; map the card endpoints |
| `Wend.Api/wwwroot/js/escape.js` | new | Shared `escapeHtml` (extracted — 3rd copy) |
| `Wend.Api/wwwroot/js/boards/view.js` | modify | Import shared `escapeHtml` |
| `Wend.Api/wwwroot/js/lists/` → `js/board/` | move | Rename the board-screen module |
| `Wend.Api/wwwroot/js/board/model.js` | modify | Add `createCard` |
| `Wend.Api/wwwroot/js/board/view.js` | modify | Render card chips + add-card form; open-card |
| `Wend.Api/wwwroot/js/board/controller.js` | modify | Wire create-card + open-card |
| `Wend.Api/wwwroot/js/card/model.js` | new | One card: load / save / remove |
| `Wend.Api/wwwroot/js/card/view.js` | new | Task-view screen; forwards events; focus |
| `Wend.Api/wwwroot/js/card/controller.js` | new | Wires view → model; announces; navigates back |
| `Wend.Api/wwwroot/js/main.js` | modify | Renamed imports; `showCard` screen + focus |
| `Wend.Api/wwwroot/css/app.css` | modify | Card chips, add-card form, task-view layout |
| `Wend.Tests/CardRepositoryTests.cs` | new | Repository unit tests (in-memory SQLite) |
| `Wend.Tests/CardApiTests.cs` | new | API integration tests |

---

## Task 1: Card entity + List.Cards + DbSet + query filter

**Files:**
- Create: `Wend.Core/Card.cs`
- Modify: `Wend.Core/List.cs`
- Modify: `Wend.Core/WendDbContext.cs`
- Test: `Wend.Tests/CardRepositoryTests.cs`

- [ ] **Step 1: Write the failing tests** — `Wend.Tests/CardRepositoryTests.cs`

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class CardRepositoryTests
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

    // Adds a board + one list directly, returning the list id, so card tests have a parent.
    private async Task<int> SeedListAsync()
    {
        var board = new Board { Title = "Board" };
        _db.Boards.Add(board);
        await _db.SaveChangesAsync();
        var list = new List { BoardId = board.Id, Title = "List", Position = 0 };
        _db.Lists.Add(list);
        await _db.SaveChangesAsync();
        return list.Id;
    }

    [Test]
    public async Task Saved_card_belongs_to_its_list_and_keeps_its_position()
    {
        var listId = await SeedListAsync();

        _db.Cards.Add(new Card { ListId = listId, Title = "Email Rebecka", Position = 0, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var card = await _db.Cards.SingleAsync();
        Assert.That(card.Id, Is.GreaterThan(0));
        Assert.That(card.ListId, Is.EqualTo(listId));
        Assert.That(card.Title, Is.EqualTo("Email Rebecka"));
        Assert.That(card.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Deleting_a_list_cascades_to_its_cards()
    {
        var listId = await SeedListAsync();
        _db.Cards.Add(new Card { ListId = listId, Title = "Card", Position = 0, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var list = await _db.Lists.SingleAsync(l => l.Id == listId);
        _db.Lists.Remove(list);
        await _db.SaveChangesAsync();

        Assert.That(await _db.Cards.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Deleted_or_archived_cards_are_hidden_from_queries()
    {
        var listId = await SeedListAsync();
        _db.Cards.Add(new Card { ListId = listId, Title = "Visible", Position = 0, CreatedAt = DateTime.UtcNow });
        _db.Cards.Add(new Card { ListId = listId, Title = "Gone", Position = 1, CreatedAt = DateTime.UtcNow, DeletedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var titles = await _db.Cards.Select(c => c.Title).ToListAsync();
        Assert.That(titles, Is.EqualTo(new[] { "Visible" }));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests"`
Expected: FAIL — does not compile (`Card` and `WendDbContext.Cards` don't exist).

- [ ] **Step 3: Create the entity** — `Wend.Core/Card.cs`

```csharp
namespace Wend.Core;

/// <summary>A card within a list — the unit of work. Carries its ordering position and the
/// lifecycle timestamps later plans use (Done / Archive / soft-delete); only the core fields
/// are written in Plan 3.</summary>
public class Card
{
    public int Id { get; set; }
    public int ListId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }   // Plan 6 (Done)
    public DateTime? ArchivedAt { get; set; }    // later slice (Archive)
    public DateTime? DeletedAt { get; set; }     // Plan 7 (undo-delete)
}
```

- [ ] **Step 4: Add the navigation collection** — `Wend.Core/List.cs`

Add the `Cards` collection (mirrors `Board.Lists`). The full file becomes:

```csharp
namespace Wend.Core;

/// <summary>A list (column) within a board — holds its ordering position and its cards.</summary>
public class List
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Title { get; set; } = "";
    public int Position { get; set; }

    // A list's cards. Required FK on Card.ListId → deleting a list cascades to them.
    public ICollection<Card> Cards { get; set; } = [];
}
```

- [ ] **Step 5: Add the DbSet + query filter** — `Wend.Core/WendDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>EF Core context for Wend's SQLite database.</summary>
public class WendDbContext(DbContextOptions<WendDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<List> Lists => Set<List>();
    public DbSet<Card> Cards => Set<Card>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Hide soft-deleted / archived cards from every query. Plans 6-7 set these timestamps;
        // until then the filter is inert (no card ever has them set).
        modelBuilder.Entity<Card>().HasQueryFilter(c => c.DeletedAt == null && c.ArchivedAt == null);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add Wend.Core/Card.cs Wend.Core/List.cs Wend.Core/WendDbContext.cs Wend.Tests/CardRepositoryTests.cs
git commit -m "Add Card entity with list cascade and soft-delete query filter"
```

---

## Task 2: Repository — create (append), list-for-list (ordered), get-by-id

**Files:**
- Create: `Wend.Core/ICardRepository.cs`
- Create: `Wend.Core/EfCardRepository.cs`
- Modify: `Wend.Tests/CardRepositoryTests.cs`

- [ ] **Step 1: Add the repo fields + failing tests** — `Wend.Tests/CardRepositoryTests.cs`

Add three fields and initialise them at the end of `SetUp` (after `_db.Database.EnsureCreated();`):

```csharp
    private EfCardRepository _repo = null!;
    private EfBoardRepository _boards = null!;
    private EfListRepository _lists = null!;
    // ...at the end of SetUp:
    _repo = new EfCardRepository(_db);
    _boards = new EfBoardRepository(_db);
    _lists = new EfListRepository(_db);
```

Replace the `SeedListAsync` helper from Task 1 with a repository-based one, and add the tests:

```csharp
    private async Task<int> NewListAsync()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        return list.Id;
    }

    [Test]
    public async Task Create_appends_each_card_at_the_next_position()
    {
        var listId = await NewListAsync();

        var first = await _repo.CreateCardAsync(listId, "First");
        var second = await _repo.CreateCardAsync(listId, "Second");

        Assert.That(first.Position, Is.EqualTo(0));
        Assert.That(second.Position, Is.EqualTo(1));
        Assert.That(first.CreatedAt, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public async Task Get_cards_for_list_returns_them_in_position_order()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "First");
        await _repo.CreateCardAsync(listId, "Second");

        var cards = await _repo.GetCardsForListAsync(listId);

        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Positions_count_from_zero_per_list()
    {
        var listA = await NewListAsync();
        var listB = await NewListAsync();

        var a1 = await _repo.CreateCardAsync(listA, "A1");
        var b1 = await _repo.CreateCardAsync(listB, "B1");

        Assert.That(a1.Position, Is.EqualTo(0));
        Assert.That(b1.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Get_card_returns_it_or_null()
    {
        var listId = await NewListAsync();
        var created = await _repo.CreateCardAsync(listId, "Find me");

        Assert.That((await _repo.GetCardAsync(created.Id))!.Title, Is.EqualTo("Find me"));
        Assert.That(await _repo.GetCardAsync(9999), Is.Null);
    }
```

Note: the Task 1 `SeedListAsync` helper is now replaced by `NewListAsync`; the three Task 1 tests used `SeedListAsync`, so update those three call sites to `await NewListAsync()` (they only need a list id) — except `Saved_card_belongs_to_its_list_and_keeps_its_position`, `Deleting_a_list_cascades_to_its_cards`, and `Deleted_or_archived_cards_are_hidden_from_queries` all just need a list id, so `NewListAsync()` works for all three.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests"`
Expected: FAIL — `EfCardRepository` / `CreateCardAsync` / `GetCardsForListAsync` / `GetCardAsync` don't exist.

- [ ] **Step 3: Define the seam** — `Wend.Core/ICardRepository.cs`

```csharp
namespace Wend.Core;

/// <summary>
/// Persistence seam for cards within a list. Position is a 0-based contiguous index; the
/// repository keeps it gapless on create and delete. (Moving cards arrives in Plan 5.)
/// </summary>
public interface ICardRepository
{
    Task<IReadOnlyList<Card>> GetCardsForListAsync(int listId);
    Task<Card?> GetCardAsync(int id);
    Task<Card> CreateCardAsync(int listId, string title);
    Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate);
    Task<bool> DeleteCardAsync(int id);
}
```

- [ ] **Step 4: Implement create / list / get** — `Wend.Core/EfCardRepository.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfCardRepository(WendDbContext db) : ICardRepository
{
    public async Task<IReadOnlyList<Card>> GetCardsForListAsync(int listId) =>
        await db.Cards.Where(c => c.ListId == listId)
                      .OrderBy(c => c.Position)
                      .ToListAsync();

    public async Task<Card?> GetCardAsync(int id) => await db.Cards.FindAsync(id);

    public async Task<Card> CreateCardAsync(int listId, string title)
    {
        // Append: the next position is the current card count for this list.
        var position = await db.Cards.CountAsync(c => c.ListId == listId);
        var card = new Card
        {
            ListId = listId,
            Title = title,
            Position = position,
            CreatedAt = DateTime.UtcNow,
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card;
    }

    // Edit / Delete arrive in Task 3.
    public Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate) =>
        throw new NotImplementedException();
    public Task<bool> DeleteCardAsync(int id) => throw new NotImplementedException();
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests"`
Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

```bash
git add Wend.Core/ICardRepository.cs Wend.Core/EfCardRepository.cs Wend.Tests/CardRepositoryTests.cs
git commit -m "Add card create, ordered list-for-list and get-by-id"
```

---

## Task 3: Repository — edit & delete (with resequence)

**Files:**
- Modify: `Wend.Core/EfCardRepository.cs`
- Modify: `Wend.Tests/CardRepositoryTests.cs`

- [ ] **Step 1: Add the failing tests** — append to `CardRepositoryTests`

```csharp
    [Test]
    public async Task Edit_updates_the_fields_and_reports_missing()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Old");

        var due = new DateOnly(2026, 6, 25);
        Assert.That(await _repo.EditCardAsync(card.Id, "New", "Some notes", due), Is.True);

        var saved = (await _repo.GetCardAsync(card.Id))!;
        Assert.That(saved.Title, Is.EqualTo("New"));
        Assert.That(saved.Description, Is.EqualTo("Some notes"));
        Assert.That(saved.DueDate, Is.EqualTo(due));

        Assert.That(await _repo.EditCardAsync(9999, "X", null, null), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_card_and_resequences_the_rest()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "A");           // 0
        var b = await _repo.CreateCardAsync(listId, "B");   // 1
        await _repo.CreateCardAsync(listId, "C");           // 2

        Assert.That(await _repo.DeleteCardAsync(b.Id), Is.True);

        var cards = await _repo.GetCardsForListAsync(listId);
        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(cards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 })); // gapless
    }

    [Test]
    public async Task Delete_reports_missing()
    {
        Assert.That(await _repo.DeleteCardAsync(9999), Is.False);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests"`
Expected: FAIL — `NotImplementedException` from the edit/delete stubs.

- [ ] **Step 3: Replace the edit & delete stubs** — `Wend.Core/EfCardRepository.cs`

Replace the two stub lines with:

```csharp
    public async Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate)
    {
        var card = await db.Cards.FindAsync(id);
        if (card is null) return false;
        card.Title = title;
        card.Description = description;
        card.DueDate = dueDate;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteCardAsync(int id)
    {
        var card = await db.Cards.FindAsync(id);
        if (card is null) return false;
        db.Cards.Remove(card);
        await db.SaveChangesAsync();
        await ResequenceAsync(card.ListId); // keep the survivors gapless (0,1,2,…)
        return true;
    }

    // Rewrites a list's card positions to a gapless 0-based sequence in current order.
    private async Task ResequenceAsync(int listId)
    {
        var cards = await db.Cards.Where(c => c.ListId == listId)
                                  .OrderBy(c => c.Position)
                                  .ToListAsync();
        for (var i = 0; i < cards.Count; i++) cards[i].Position = i;
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests"`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Core/EfCardRepository.cs Wend.Tests/CardRepositoryTests.cs
git commit -m "Add card edit and delete with position resequence"
```

---

## Task 4: Wire the repo + create endpoint (POST)

**Files:**
- Create: `Wend.Api/CardEndpoints.cs`
- Modify: `Wend.Api/Program.cs`
- Test: `Wend.Tests/CardApiTests.cs`

- [ ] **Step 1: Write the failing tests** — `Wend.Tests/CardApiTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class CardApiTests
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
    private record CardDto(int Id, string Title, int Position);
    private record CardSummaryDto(int Id, string Title, string? DueDate, int Position);
    private record ListWithCardsDto(int Id, string Title, int Position, List<CardSummaryDto> Cards);
    private record BoardWithCardsDto(int Id, string Title, List<ListWithCardsDto> Lists);
    private record CardDetailDto(int Id, int ListId, string ListTitle, string Title, string? Description, string? DueDate, int Position);

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

    private async Task<CardDto> CreateCardAsync(int listId, string title)
    {
        var res = await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title });
        return (await res.Content.ReadFromJsonAsync<CardDto>())!;
    }

    private async Task<int> NewListAsync()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        return list.Id;
    }

    [Test]
    public async Task Posting_a_card_creates_it_at_the_next_position()
    {
        var listId = await NewListAsync();

        var response = await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title = "Email Rebecka" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await response.Content.ReadFromJsonAsync<CardDto>();
        Assert.That(created!.Title, Is.EqualTo("Email Rebecka"));
        Assert.That(created.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Posting_a_blank_card_title_is_rejected()
    {
        var listId = await NewListAsync();
        var response = await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title = "   " });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_over_long_card_title_is_rejected()
    {
        var listId = await NewListAsync();
        var response = await _client.PostAsJsonAsync(
            $"/api/lists/{listId}/cards", new { title = new string('x', 201) });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_a_card_to_a_missing_list_is_404()
    {
        var response = await _client.PostAsJsonAsync("/api/lists/9999/cards", new { title = "X" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests"`
Expected: FAIL — `POST /api/lists/{id}/cards` returns 404 (no route).

- [ ] **Step 3: Create the endpoint group** — `Wend.Api/CardEndpoints.cs`

```csharp
using Wend.Core;

namespace Wend.Api;

public static class CardEndpoints
{
    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 5000;

    public static IEndpointRouteBuilder MapCardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lists/{listId:int}/cards",
            async (int listId, CreateCardRequest req, IListRepository lists, ICardRepository cards) =>
            {
                var title = req.Title?.Trim() ?? "";
                if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
                if (await lists.GetListAsync(listId) is null) return Results.NotFound();
                var card = await cards.CreateCardAsync(listId, title);
                return Results.Created($"/api/cards/{card.Id}", card);
            });

        // GET / PUT / DELETE arrive in Tasks 6-7.
        return app;
    }
}

public record CreateCardRequest(string Title);
```

This calls `lists.GetListAsync(listId)` to 404 on a missing list — add that seam method now:

`Wend.Core/IListRepository.cs` — add to the interface:

```csharp
    Task<List?> GetListAsync(int id);
```

`Wend.Core/EfListRepository.cs` — add the implementation (next to `GetListsForBoardAsync`):

```csharp
    public async Task<List?> GetListAsync(int id) => await db.Lists.FindAsync(id);
```

- [ ] **Step 4: Register the repo + map the group** — `Wend.Api/Program.cs`

After the line that registers `IListRepository`, add:

```csharp
builder.Services.AddScoped<ICardRepository, EfCardRepository>();
```

After the line `app.MapListEndpoints();` add:

```csharp
app.MapCardEndpoints();
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add Wend.Core/IListRepository.cs Wend.Core/EfListRepository.cs Wend.Api/CardEndpoints.cs Wend.Api/Program.cs Wend.Tests/CardApiTests.cs
git commit -m "Wire ICardRepository and expose POST card create"
```

---

## Task 5: Nest cards in the board detail (GET /api/boards/{id})

**Files:**
- Modify: `Wend.Api/BoardEndpoints.cs`
- Modify: `Wend.Tests/CardApiTests.cs`

- [ ] **Step 1: Add the failing test** — append inside `CardApiTests`

```csharp
    [Test]
    public async Task Board_detail_nests_each_lists_cards_in_order()
    {
        var board = await CreateBoardAsync("Sprint");
        var list = await CreateListAsync(board.Id, "To do");
        await CreateCardAsync(list.Id, "First");
        await CreateCardAsync(list.Id, "Second");

        var detail = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");

        var cards = detail!.Lists.Single().Cards;
        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "First", "Second" }));
        Assert.That(cards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 }));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests.Board_detail_nests_each_lists_cards_in_order"`
Expected: FAIL — the current `ListSummary` carries no `cards`, so `Cards` deserialises to null and `.Single().Cards` throws.

- [ ] **Step 3: Nest cards in the board detail** — `Wend.Api/BoardEndpoints.cs`

Replace the existing `group.MapGet("/{id:int}", …)` handler with one that also takes `ICardRepository` and fills each list's cards:

```csharp
        group.MapGet("/{id:int}", async (int id, IBoardRepository boards, IListRepository lists, ICardRepository cards) =>
        {
            if (await boards.GetBoardAsync(id) is not { } board) return Results.NotFound();

            var summaries = new List<ListSummary>();
            foreach (var l in await lists.GetListsForBoardAsync(id))
            {
                var cardSummaries = (await cards.GetCardsForListAsync(l.Id))
                    .Select(c => new CardSummary(c.Id, c.Title, c.DueDate, c.Position))
                    .ToList();
                summaries.Add(new ListSummary(l.Id, l.Title, l.Position, cardSummaries));
            }
            return Results.Ok(new BoardDetail(board.Id, board.Title, summaries));
        });
```

Update the `ListSummary` record to carry its cards, and add `CardSummary`, at the bottom of the file (replace the existing `ListSummary` line):

```csharp
public record ListSummary(int Id, string Title, int Position, IReadOnlyList<CardSummary> Cards);
public record CardSummary(int Id, string Title, DateOnly? DueDate, int Position);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests"`
Expected: PASS (5 tests). Also run `dotnet test --filter "FullyQualifiedName~ListApiTests"` → still PASS — the Lists tests read `id`/`title`/`position`, and the added `cards` field is simply ignored by their `ListDto`.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/BoardEndpoints.cs Wend.Tests/CardApiTests.cs
git commit -m "Nest each list's cards in the board detail"
```

---

## Task 6: Card detail endpoint (GET /api/cards/{id})

**Files:**
- Modify: `Wend.Api/CardEndpoints.cs`
- Modify: `Wend.Tests/CardApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append inside `CardApiTests`

```csharp
    [Test]
    public async Task Get_card_returns_its_detail_with_the_list_name()
    {
        var board = await CreateBoardAsync("Sprint");
        var list = await CreateListAsync(board.Id, "To do");
        var card = await CreateCardAsync(list.Id, "Email Rebecka");

        var detail = await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}");

        Assert.That(detail!.Title, Is.EqualTo("Email Rebecka"));
        Assert.That(detail.ListId, Is.EqualTo(list.Id));
        Assert.That(detail.ListTitle, Is.EqualTo("To do"));
        Assert.That(detail.Description, Is.Null);
        Assert.That(detail.DueDate, Is.Null);
    }

    [Test]
    public async Task Get_a_missing_card_is_404()
    {
        var res = await _client.GetAsync("/api/cards/9999");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests"`
Expected: FAIL — `GET /api/cards/{id}` returns 404 for the real card too (no route).

- [ ] **Step 3: Add the GET route + detail DTO** — `Wend.Api/CardEndpoints.cs`

Inside `MapCardEndpoints`, before `return app;`, add:

```csharp
        app.MapGet("/api/cards/{id:int}", async (int id, ICardRepository cards, IListRepository lists) =>
        {
            if (await cards.GetCardAsync(id) is not { } c) return Results.NotFound();
            var list = await lists.GetListAsync(c.ListId);
            return Results.Ok(new CardDetail(c.Id, c.ListId, list?.Title ?? "", c.Title, c.Description, c.DueDate, c.Position));
        });
```

Add at the bottom of the file (outside the class), next to `CreateCardRequest`:

```csharp
public record CardDetail(int Id, int ListId, string ListTitle, string Title, string? Description, DateOnly? DueDate, int Position);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/CardEndpoints.cs Wend.Tests/CardApiTests.cs
git commit -m "Add GET card detail with its list name"
```

---

## Task 7: Edit (PUT) & delete (DELETE) endpoints

**Files:**
- Modify: `Wend.Api/CardEndpoints.cs`
- Modify: `Wend.Tests/CardApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append inside `CardApiTests`

```csharp
    [Test]
    public async Task Put_edits_a_cards_title_notes_and_due_date()
    {
        var listId = await NewListAsync();
        var card = await CreateCardAsync(listId, "Old");

        var put = await _client.PutAsJsonAsync($"/api/cards/{card.Id}",
            new { title = "New", description = "Some notes", dueDate = "2026-06-25" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}");
        Assert.That(detail!.Title, Is.EqualTo("New"));
        Assert.That(detail.Description, Is.EqualTo("Some notes"));
        Assert.That(detail.DueDate, Is.EqualTo("2026-06-25"));
    }

    [Test]
    public async Task Put_rejects_a_blank_card_title()
    {
        var listId = await NewListAsync();
        var card = await CreateCardAsync(listId, "Old");

        var put = await _client.PutAsJsonAsync($"/api/cards/{card.Id}", new { title = "  ", description = (string?)null, dueDate = (string?)null });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Put_to_a_missing_card_is_404()
    {
        var put = await _client.PutAsJsonAsync("/api/cards/9999", new { title = "X", description = (string?)null, dueDate = (string?)null });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_removes_a_card()
    {
        var board = await CreateBoardAsync("B");
        var list = await CreateListAsync(board.Id, "L");
        var card = await CreateCardAsync(list.Id, "Temp");

        var del = await _client.DeleteAsync($"/api/cards/{card.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists.Single().Cards, Is.Empty);
    }

    [Test]
    public async Task Delete_a_missing_card_is_404()
    {
        var del = await _client.DeleteAsync("/api/cards/9999");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests"`
Expected: FAIL — `PUT` / `DELETE /api/cards/{id}` return 404 (no routes).

- [ ] **Step 3: Add the routes + edit DTO** — `Wend.Api/CardEndpoints.cs`

Inside `MapCardEndpoints`, before `return app;`, add:

```csharp
        app.MapPut("/api/cards/{id:int}", async (int id, EditCardRequest req, ICardRepository cards) =>
        {
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            var description = req.Description?.Trim();
            if (description is { Length: > MaxDescriptionLength }) return Results.BadRequest();
            return await cards.EditCardAsync(id, title, description, req.DueDate)
                ? Results.NoContent() : Results.NotFound();
        });

        app.MapDelete("/api/cards/{id:int}", async (int id, ICardRepository cards) =>
            await cards.DeleteCardAsync(id) ? Results.NoContent() : Results.NotFound());
```

Add at the bottom of the file (outside the class):

```csharp
public record EditCardRequest(string Title, string? Description, DateOnly? DueDate);
```

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: PASS — Plans 1–2 tests plus this plan's card repository (10) and card API (12) tests.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/CardEndpoints.cs Wend.Tests/CardApiTests.cs
git commit -m "Add PUT edit and DELETE for cards"
```

---

## Task 8: Frontend refactor — rename lists→board, extract escapeHtml

**Files:**
- Create: `Wend.Api/wwwroot/js/escape.js`
- Modify: `Wend.Api/wwwroot/js/boards/view.js`
- Move: `Wend.Api/wwwroot/js/lists/` → `Wend.Api/wwwroot/js/board/`
- Modify: `Wend.Api/wwwroot/js/board/model.js`, `view.js`, `controller.js`
- Modify: `Wend.Api/wwwroot/js/main.js`

No automated test — verify by eye in the browser. **No behaviour change** in this task: it renames the board-screen module and pulls the duplicated `escapeHtml` into one shared file (this is the 3rd copy once the card view lands, the project's documented trigger to extract).

- [ ] **Step 1: Create the shared helper** — `Wend.Api/wwwroot/js/escape.js`

```js
// Shared HTML-escaping for view modules — escapes the five characters that matter in our
// templates. Imported wherever a view interpolates user text into innerHTML.
export function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]
  );
}
```

- [ ] **Step 2: Use it in the boards view** — `Wend.Api/wwwroot/js/boards/view.js`

At the top of the file add:

```js
import { escapeHtml } from "../escape.js";
```

Then delete the local `escapeHtml` function at the bottom of the file (the `function escapeHtml(s) { … }` block).

- [ ] **Step 3: Rename the module folder** — `js/lists/` → `js/board/`

Rename the directory `Wend.Api/wwwroot/js/lists` to `Wend.Api/wwwroot/js/board` (the three files keep their names: `model.js`, `view.js`, `controller.js`).

- [ ] **Step 4: Rename the exported factories** — in the moved files

- `js/board/model.js`: change `export function createListsModel(boardId)` → `export function createBoardModel(boardId)`. (The `../api.js` import is unchanged.)
- `js/board/controller.js`: change `export function createListsController(model, view, announce, { onBack } = {})` → `export function createBoardController(model, view, announce, { onBack } = {})`.
- `js/board/view.js`: change `export function createListsView(root)` → `export function createBoardView(root)`; add `import { escapeHtml } from "../escape.js";` at the top; delete the local `escapeHtml` function at the bottom.

- [ ] **Step 5: Update the coordinator** — `Wend.Api/wwwroot/js/main.js`

Replace the three `lists/` imports:

```js
import { createListsModel } from "./lists/model.js";
import { createListsView } from "./lists/view.js";
import { createListsController } from "./lists/controller.js";
```

with:

```js
import { createBoardModel } from "./board/model.js";
import { createBoardView } from "./board/view.js";
import { createBoardController } from "./board/controller.js";
```

And update the three call sites inside `showBoard`:

```js
function showBoard(boardId) {
  mount((root) => {
    const model = createBoardModel(boardId);
    const view = createBoardView(root);
    createBoardController(model, view, announce, { onBack: () => showOverview(boardId) });
    model.load().then(() => view.focusHeading());
  });
}
```

- [ ] **Step 6: Verify nothing changed in the browser**

Run: `dotnet run --project Wend.Api`, open `http://127.0.0.1:5174`:
- The overview lists boards; opening a board still shows its lists; create / rename / delete / reorder lists all still work; back still returns to the overview with focus on the board's Open button.

Stop with `Ctrl+C`.

- [ ] **Step 7: Commit**

```bash
git add Wend.Api/wwwroot/js/
git commit -m "Rename lists module to board and extract shared escapeHtml"
```

---

## Task 9: Board screen — render cards + add a card

**Files:**
- Modify: `Wend.Api/wwwroot/js/board/model.js`
- Modify: `Wend.Api/wwwroot/js/board/view.js`
- Modify: `Wend.Api/wwwroot/js/board/controller.js`
- Modify: `Wend.Api/wwwroot/js/main.js`
- Modify: `Wend.Api/wwwroot/css/app.css`

No automated test — verify by browser. Cards render inside each list, a persistent "Add a card…" input appends one, and clicking a card opens a placeholder task view (Task 10 fills it in).

- [ ] **Step 1: Add createCard to the model** — `Wend.Api/wwwroot/js/board/model.js`

Add this method inside the returned object (e.g. after `create`):

```js
    async createCard(listId, title) {
      await api(`/api/lists/${listId}/cards`, { method: "POST", body: JSON.stringify({ title }) });
      await this.load();
    },
```

- [ ] **Step 2: Render cards + add-card form** — replace `Wend.Api/wwwroot/js/board/view.js` with:

```js
import { escapeHtml } from "../escape.js";

// Renders one board's view: back link, title, add-list form, and each list with its
// move/rename/delete controls, its cards, and an add-card form. Events via data-action.
export function createBoardView(root) {
  function render(board) {
    const lists = board.lists;
    const items = lists.length
      ? lists
          .map((l, i) => {
            const first = i === 0;
            const last = i === lists.length - 1;
            const cards = (l.cards ?? [])
              .map(
                (c) => `
            <li>
              <button class="card-chip" data-action="open-card" data-card-id="${c.id}"
                aria-label="Open card: ${escapeHtml(c.title)}">
                <span class="card-title">${escapeHtml(c.title)}</span>
                ${c.dueDate ? `<span class="card-due">${escapeHtml(c.dueDate)}</span>` : ""}
              </button>
            </li>`
              )
              .join("");
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
          <ul class="card-list">${cards}</ul>
          <form class="card-form" data-action="create-card" data-list-id="${l.id}">
            <input name="title" aria-label="Add a card to ${escapeHtml(l.title)}" placeholder="Add a card…" required />
            <button type="submit">Add</button>
          </form>
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
  function focusNewCardInput(listId) {
    root.querySelector(`.card-form[data-list-id="${listId}"] input`)?.focus();
  }
  // Return focus to a specific card's chip (used when coming back from its task view).
  function focusCard(cardId) {
    root.querySelector(`.card-chip[data-card-id="${cardId}"]`)?.focus();
  }

  // After a move, land focus on a sensible enabled control in the moved list.
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
      const action = e.target.dataset.action;
      if (action !== "create" && action !== "create-card") return;
      e.preventDefault();
      const title = e.target.title.value.trim();
      const submit = e.target.querySelector("button[type=submit]");
      submit.disabled = true;
      try {
        if (action === "create") await handlers.create(title);
        else await handlers.createCard(Number(e.target.dataset.listId), title);
      } finally {
        submit.disabled = false;
      }
    });
    root.addEventListener("click", (e) => {
      const btn = e.target.closest("button[data-action]");
      if (!btn || btn.dataset.action === "create" || btn.dataset.action === "create-card") return;
      const action = btn.dataset.action;
      if (action === "back") return handlers.back();
      if (action === "open-card") return handlers.openCard(Number(btn.dataset.cardId));
      const id = Number(btn.dataset.id);
      if (action === "rename") handlers.rename(id);
      else if (action === "delete") handlers.delete(id);
      else if (action === "move-left") handlers.moveLeft(id);
      else if (action === "move-right") handlers.moveRight(id);
    });
  }

  return { render, focusHeading, focusNewListInput, focusNewCardInput, focusCard, focusListAction, bindActions };
}
```

- [ ] **Step 3: Wire create-card + open-card** — `Wend.Api/wwwroot/js/board/controller.js`

Change the controller signature to accept `onOpenCard`, and add the two handlers. Replace the file with:

```js
// Wires the board view to the model: announces results, manages focus, confirms list deletes,
// turns move-left/right into a target position, and forwards card actions.
// onBack() returns to the overview; onOpenCard(cardId) opens a card's task view.
export function createBoardController(model, view, announce, { onBack, onOpenCard } = {}) {
  let lists = [];

  view.bindActions({
    back: () => onBack?.(),
    openCard: (cardId) => onOpenCard?.(cardId),
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
    createCard: async (listId, title) => {
      if (!title) return;
      try {
        await model.createCard(listId, title);
        announce("Card added.");
        view.focusNewCardInput(listId);
      } catch {
        announce("Couldn't add the card — please try again.");
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

- [ ] **Step 4: Wire open-card to a placeholder card screen** — `Wend.Api/wwwroot/js/main.js`

In `showBoard`, pass `onOpenCard`:

```js
function showBoard(boardId) {
  mount((root) => {
    const model = createBoardModel(boardId);
    const view = createBoardView(root);
    createBoardController(model, view, announce, {
      onBack: () => showOverview(boardId),
      onOpenCard: (cardId) => showCard(cardId, boardId),
    });
    model.load().then(() => view.focusHeading());
  });
}

// Placeholder task view — Task 10 swaps in the real card module.
function showCard(cardId, boardId) {
  mount((root) => {
    root.innerHTML = `
      <div class="card-view">
        <button class="back-link" data-action="back">← Board</button>
        <h2 class="card-heading" tabindex="-1">Card #${cardId}</h2>
        <p class="empty">Task view coming in the next step…</p>
      </div>`;
    root.querySelector('[data-action="back"]').addEventListener("click", () => showBoard(boardId));
    root.querySelector(".card-heading").focus();
  });
}
```

- [ ] **Step 5: Style cards + add-card form** — append to `Wend.Api/wwwroot/css/app.css`:

```css
/* Cards inside a list. */
.card-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.card-chip {
  width: 100%;
  text-align: left;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  padding: 0.5rem 0.6rem;
  border: 1px solid;
  border-color: color-mix(in srgb, currentColor 18%, transparent);
  border-radius: 0.5rem;
}

.card-due {
  align-self: flex-start;
  font-size: 0.7rem;
  padding: 0.05rem 0.4rem;
  border-radius: 999px;
  background: color-mix(in srgb, currentColor 20%, transparent);
}

.card-form {
  display: flex;
  gap: 0.4rem;
}
.card-form input { flex: 1; }
```

- [ ] **Step 6: Verify in the browser**

If you haven't this plan yet, delete the dev DB so the `Cards` table is created:
`Remove-Item "$env:LOCALAPPDATA\Wend\data.db" -ErrorAction SilentlyContinue`

Run: `dotnet run --project Wend.Api`, open `http://127.0.0.1:5174`, open a board, and check:
- Each list shows an "Add a card…" input. Add a card → it appears at the bottom; the input clears and **keeps focus** (add several without the mouse). Each add **announces** ("Card added.").
- A card with no due date shows just its title.
- Click a card → the screen swaps to the placeholder "Card #<id>" with a **← Board** link; focus lands on the heading. Back returns to the board.
- Reload → cards persist in their lists and order.

Stop with `Ctrl+C`.

- [ ] **Step 7: Commit**

```bash
git add Wend.Api/wwwroot/js/ Wend.Api/wwwroot/css/app.css
git commit -m "Render cards in the board and add a card per list"
```

---

## Task 10: Card task-view trio (the real screen)

**Files:**
- Create: `Wend.Api/wwwroot/js/card/model.js`
- Create: `Wend.Api/wwwroot/js/card/view.js`
- Create: `Wend.Api/wwwroot/js/card/controller.js`
- Modify: `Wend.Api/wwwroot/js/main.js`
- Modify: `Wend.Api/wwwroot/css/app.css`

No automated test — verify by browser pass (final step). Mirrors the existing MVC trios; the model holds one card, the view renders the editable task view, the controller wires save / delete / back.

- [ ] **Step 1: Model (one card)** — `Wend.Api/wwwroot/js/card/model.js`

```js
import { api } from "../api.js";

// State + data for a single card. Re-fetches after a save so the view shows server truth.
// No DOM. Subscribers notified on change.
export function createCardModel(cardId) {
  let card = { id: cardId, listId: 0, listTitle: "", title: "", description: "", dueDate: null, position: 0 };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(card));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(card);
    },
    async load() {
      card = await api(`/api/cards/${cardId}`);
      notify();
    },
    async save({ title, description, dueDate }) {
      await api(`/api/cards/${cardId}`, {
        method: "PUT",
        body: JSON.stringify({ title, description, dueDate }),
      });
      await this.load();
    },
    async remove() {
      await api(`/api/cards/${cardId}`, { method: "DELETE" });
    },
  };
}
```

- [ ] **Step 2: View (the task view)** — `Wend.Api/wwwroot/js/card/view.js`

```js
import { escapeHtml } from "../escape.js";

// Renders the task view for one card: back link, heading, an edit form (title, due date, notes),
// Save, and Delete. Forwards events via data-action. No fetch, no logic.
export function createCardView(root) {
  function render(card) {
    root.innerHTML = `
      <div class="card-view">
        <button class="back-link" data-action="back">← Board</button>
        <h2 class="card-heading" tabindex="-1">${escapeHtml(card.title)}</h2>
        <p class="card-list-name">In list: <strong>${escapeHtml(card.listTitle)}</strong></p>
        <form class="card-detail" data-action="save">
          <label class="field">
            <span>Title</span>
            <input name="title" value="${escapeHtml(card.title)}" aria-label="Card title" required />
          </label>
          <label class="field">
            <span>Due date</span>
            <input name="dueDate" type="date" value="${card.dueDate ?? ""}" aria-label="Due date" />
          </label>
          <label class="field">
            <span>Notes</span>
            <textarea name="description" aria-label="Notes">${escapeHtml(card.description ?? "")}</textarea>
          </label>
          <button type="submit">Save changes</button>
        </form>
        <button class="card-delete" data-action="delete">Delete card</button>
      </div>`;
  }

  function focusHeading() {
    root.querySelector(".card-heading")?.focus();
  }

  function bindActions(handlers) {
    root.addEventListener("submit", async (e) => {
      if (e.target.dataset.action !== "save") return;
      e.preventDefault();
      const form = e.target;
      const title = form.title.value.trim();
      const description = form.description.value;
      const dueDate = form.dueDate.value || null; // "" → null
      const submit = form.querySelector("button[type=submit]");
      submit.disabled = true;
      try {
        await handlers.save({ title, description, dueDate });
      } finally {
        submit.disabled = false;
      }
    });
    root.addEventListener("click", (e) => {
      const btn = e.target.closest("button[data-action]");
      if (!btn || btn.dataset.action === "save") return;
      if (btn.dataset.action === "back") return handlers.back();
      if (btn.dataset.action === "delete") return handlers.delete();
    });
  }

  return { render, focusHeading, bindActions };
}
```

- [ ] **Step 3: Controller (wire, announce, navigate)** — `Wend.Api/wwwroot/js/card/controller.js`

```js
// Wires the task view to the model: announces results, surfaces failures, navigates on
// delete. Delete is immediate in Plan 3 (no confirm; undo arrives in Plan 7).
// onBack() returns to the board (focusing this card); onDeleted() returns to the board.
export function createCardController(model, view, announce, { onBack, onDeleted } = {}) {
  view.bindActions({
    back: () => onBack?.(),
    save: async ({ title, description, dueDate }) => {
      if (!title) return;
      try {
        await model.save({ title, description, dueDate });
        announce("Card saved.");
      } catch {
        announce("Couldn't save the card — please try again.");
      }
    },
    delete: async () => {
      try {
        await model.remove();
        announce("Card deleted.");
        onDeleted?.();
      } catch {
        announce("Couldn't delete the card — please try again.");
      }
    },
  });

  model.subscribe((card) => view.render(card));
}
```

- [ ] **Step 4: Use the card module in main.js** — `Wend.Api/wwwroot/js/main.js`

Add these imports under the existing ones:

```js
import { createCardModel } from "./card/model.js";
import { createCardView } from "./card/view.js";
import { createCardController } from "./card/controller.js";
```

Give `showBoard` an optional card to focus on return, and replace the placeholder `showCard` with the real trio:

```js
function showBoard(boardId, focusCardId) {
  mount((root) => {
    const model = createBoardModel(boardId);
    const view = createBoardView(root);
    createBoardController(model, view, announce, {
      onBack: () => showOverview(boardId),
      onOpenCard: (cardId) => showCard(cardId, boardId),
    });
    model.load().then(() => {
      if (focusCardId) view.focusCard(focusCardId);
      else view.focusHeading();
    });
  });
}

function showCard(cardId, boardId) {
  mount((root) => {
    const model = createCardModel(cardId);
    const view = createCardView(root);
    createCardController(model, view, announce, {
      onBack: () => showBoard(boardId, cardId), // return → focus the card we opened
      onDeleted: () => showBoard(boardId),      // deleted → card is gone, focus the heading
    });
    model.load().then(() => view.focusHeading());
  });
}
```

- [ ] **Step 5: Style the task view** — append to `Wend.Api/wwwroot/css/app.css`:

```css
/* Task view (one card). */
.card-view { display: flex; flex-direction: column; }

.card-list-name { margin: 0 0 1rem; }

.card-detail {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.card-detail .field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.card-detail textarea {
  min-height: 6rem;
  resize: vertical;
  font: inherit;
}

.card-delete { align-self: flex-start; }
```

- [ ] **Step 6: Verify the full loop in the browser**

Run: `dotnet run --project Wend.Api`, open `http://127.0.0.1:5174`, open a board, and check:
- Click a card → the task view shows its title (heading + editable field), "In list: <name>", an empty due date and notes. Focus lands on the heading.
- Edit the title, set a due date, type notes → **Save changes** → "Card saved." announces; the heading updates.
- **← Board** returns to the board with focus on the card's chip; the chip shows the new title and a due-date pill.
- Open the card again → **Delete card** removes it immediately (no confirm) and returns to the board; "Card deleted." announces.
- Reload → edits persist; deleted cards stay gone.
- Keyboard-only and with a screen reader: every control is reachable with a visible focus ring; save/delete/back announce.

Stop with `Ctrl+C`.

- [ ] **Step 7: Commit**

```bash
git add Wend.Api/wwwroot/js/ Wend.Api/wwwroot/css/app.css
git commit -m "Add card task view: open, edit, save and delete"
```

---

## Task 11: Acceptance pass + README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Full test run**

Run: `dotnet test`
Expected: PASS — Plans 1–2 tests plus this plan's 10 card-repository and 12 card-API tests; 0 warnings on `dotnet build`.

- [ ] **Step 2: Manual acceptance** (`dotnet run --project Wend.Api`)
- Open a board; add cards to its lists; open a card; edit title / notes / due date and save; delete a card — all persist across a restart.
- The page is dark on first paint (no light flash).
- Keyboard-only: add a card, open it, edit + save, delete, and navigate board ↔ card — visible focus throughout, focus returns sensibly (to the card's chip on back, to the heading after delete).
- Screen reader announces add / save / delete; card chips read "Open card: <title>".
- Phone width: lists stack with their cards; ≥768px shows horizontal columns.

- [ ] **Step 3: Update the README status** — `README.md`

Change the **Status** section to:

```markdown
## Status

**Slice 1 — local single-user board** (in progress). Boards, lists, and cards work end to end: open a board, add lists, add cards, and open a card into a focused task view to edit its title, notes, and due date — saved to SQLite, accessible and dark-mode-first. Card moving, the Done checkmark, labels, and a checklist come next.
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Cards working end to end; update README status"
```

---

## Definition of done

- `dotnet test` green: Plans 1–2 tests + 10 card-repository tests + 12 card-API tests.
- `dotnet build` clean (0 warnings).
- A card can be created in a list, opened into the task view, edited (title / notes / due date) and deleted from the browser, all persisted across restarts.
- `GET /api/boards/{id}` returns each list with its cards in position order; positions stay gapless (0,1,2,…) through create and delete; `GET /api/cards/{id}` returns the card with its list name.
- Deleting a list cascades to its cards (and deleting a board to its lists and their cards); blank and over-long card titles are rejected; soft-deleted/archived cards are filtered out (inert in Plan 3); tests never touch the real DB.
- `escapeHtml` lives in one shared module; the board-screen module is named `board/`; a new `card/` trio renders the task view.
- Dark-mode-first; keyboard-operable with focus managed across board ↔ card and after each action; add / save / delete announce via the `aria-live` region; card delete is immediate (no confirm — undo is Plan 7).
- **Named trade-off (carried from Plans 1–2):** schema is created with `EnsureCreated`; the dev DB must be deleted once so the `Cards` table is added. Migrations are adopted at the Slice 1 → 2 boundary.
- The `Card` schema is complete for Slice 1 (lifecycle timestamps carried), so Plans 6 (Done) and 7 (undo-delete) add behaviour without a further DB reset.
