# Wend Slice 1 — Plan 4: Labels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Board-scoped, reusable **labels** (name + colour) that any card can carry many-to-many — managed inline from the card, shown as soft-tint chips on the board fronts and in the task view.

**Architecture:** Two new entities — `Label` (`Board ──1:*── Label`) and a `CardLabel` join (`Card ──*:*── Label`) — behind a new `ILabelRepository` seam (EF Core → SQLite). Colour is a validated palette key. Minimal-API endpoints mirror the validated style of Plans 1–3; `GET /api/boards/{id}` grows a `labels` palette + per-card `labelIds`, and `GET /api/cards/{id}` grows `boardId` + attached `labels`. The frontend renders chips on the board and adds a non-modal label **picker** to the task view (a focused `card/labels.js` render helper composed by the card view). Backend is TDD'd with NUnit; the frontend is verified by a manual browser + screen-reader pass.

**Tech Stack:** `net10.0`, EF Core 10 SQLite, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`, vanilla JS (ES modules), the shared design-system.

**Reference:** Design spec at [`docs/2026-06-23-wend-labels-design.md`](../2026-06-23-wend-labels-design.md); Plan 3 at [`docs/plans/2026-06-22-slice1-cards.md`](2026-06-22-slice1-cards.md).

---

## Notes for the implementer

- **No build step.** Frontend is hand-authored JS/CSS in `Wend.Api/wwwroot`, served static. `dotnet run --project Wend.Api` is the only thing to run.
- **Delete the dev DB once before manual runs.** `EnsureCreated()` does **not** add the new `Labels` / `CardLabels` tables to an existing `%LOCALAPPDATA%\Wend\data.db`. Before the first `dotnet run` in this plan, delete it so it rebuilds with the new tables: `Remove-Item "$env:LOCALAPPDATA\Wend\data.db" -ErrorAction SilentlyContinue`. (Tests are unaffected — each boots a throwaway DB.)
- **Tests never touch the real database.** API tests boot the app against a temp DB via `WendApiFactory`; repository tests use an in-memory SQLite connection.
- **Cascade.** `Board.Labels` + the required `Label.BoardId` FK cascade-delete labels with their board (mirrors `Board.Lists`). The `CardLabel` join is configured with a composite key + two required FKs, so deleting a **card** or a **label** removes its join rows (EF default cascade; SQLite enforces it via `PRAGMA foreign_keys = ON`). Two cascade paths reach `CardLabel` (via Card and via Label) — fine on SQLite (no multiple-cascade-path restriction).
- **EF query-filter note.** `Card` has a soft-delete query filter and `CardLabel` references `Card`. If EF logs a one-time query-filter-interaction warning at model build, it is harmless here (we never join through soft-deleted cards) and is **not** a `dotnet build` warning.
- **Colour is a palette key**, never a hex: `"mint" | "cyan" | "amber" | "rose" | "lilac" | "slate"`, validated server-side by `LabelColours.IsValid`. The CSS maps the key to a fixed class `label-chip--{key}` — the value is never interpolated into a `style` string.
- **Escaping.** Label `name` is user input. Every render site escapes it via the shared `escapeHtml` (the Plan 3 helper). This matters now (self-XSS) and more under Slice 2 sharing.
- **JSON casing.** ASP.NET Core serialises `{ Id, Name, Colour }` as `{ "id", "name", "colour" }` and binds request bodies case-insensitively. Adding fields to existing responses is backwards-compatible — older test DTOs simply ignore the new fields.
- **Commits:** one per task, authored under your own account, **no co-author / no AI attribution** (house rule). Run from the repo root unless noted.

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Core/Label.cs` | new | Label entity (board-scoped name + colour key) |
| `Wend.Core/CardLabel.cs` | new | Join row (composite key CardId+LabelId) |
| `Wend.Core/LabelColours.cs` | new | The curated palette keys + `IsValid` |
| `Wend.Core/Board.cs` | modify | Add `Labels` navigation collection (cascade) |
| `Wend.Core/WendDbContext.cs` | modify | Add `Labels` / `CardLabels` DbSets + join config |
| `Wend.Core/ILabelRepository.cs` | new | Label persistence seam |
| `Wend.Core/EfLabelRepository.cs` | new | EF implementation |
| `Wend.Api/LabelEndpoints.cs` | new | Label + card-label endpoints + DTOs + guards |
| `Wend.Api/BoardEndpoints.cs` | modify | `GET /{id}` nests palette + per-card `labelIds` |
| `Wend.Api/CardEndpoints.cs` | modify | `GET /cards/{id}` adds `boardId` + attached labels |
| `Wend.Api/Program.cs` | modify | Register `ILabelRepository`; map label endpoints |
| `Wend.Api/wwwroot/css/app.css` | modify | Label chip palette + picker / swatch styles |
| `Wend.Api/wwwroot/js/board/view.js` | modify | Render label chips on card fronts |
| `Wend.Api/wwwroot/js/card/labels.js` | new | Labels-section render helper (chips + picker) |
| `Wend.Api/wwwroot/js/card/model.js` | modify | Load palette; attach/detach/create/edit/delete |
| `Wend.Api/wwwroot/js/card/view.js` | modify | Compose labels section + picker UI state + focus |
| `Wend.Api/wwwroot/js/card/controller.js` | modify | Wire label actions; announce; confirm delete |
| `Wend.Tests/LabelRepositoryTests.cs` | new | Repository unit tests (in-memory SQLite) |
| `Wend.Tests/LabelApiTests.cs` | new | API integration tests |

---

## Task 1: Label + CardLabel entities + Board.Labels + DbSet + join config

**Files:**
- Create: `Wend.Core/Label.cs`, `Wend.Core/CardLabel.cs`, `Wend.Core/LabelColours.cs`
- Modify: `Wend.Core/Board.cs`, `Wend.Core/WendDbContext.cs`
- Test: `Wend.Tests/LabelRepositoryTests.cs`

- [ ] **Step 1: Write the failing tests** — `Wend.Tests/LabelRepositoryTests.cs`

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class LabelRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;
    private EfBoardRepository _boards = null!;
    private EfListRepository _lists = null!;
    private EfCardRepository _cards = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WendDbContext>().UseSqlite(_connection).Options;
        _db = new WendDbContext(options);
        _db.Database.EnsureCreated();
        _boards = new EfBoardRepository(_db);
        _lists = new EfListRepository(_db);
        _cards = new EfCardRepository(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task A_label_belongs_to_its_board()
    {
        var board = await _boards.CreateBoardAsync("Board");
        _db.Labels.Add(new Label { BoardId = board.Id, Name = "Urgent", Colour = "rose" });
        await _db.SaveChangesAsync();

        var label = await _db.Labels.SingleAsync();
        Assert.That(label.Id, Is.GreaterThan(0));
        Assert.That(label.BoardId, Is.EqualTo(board.Id));
        Assert.That(label.Name, Is.EqualTo("Urgent"));
        Assert.That(label.Colour, Is.EqualTo("rose"));
    }

    [Test]
    public async Task Deleting_a_board_cascades_to_its_labels()
    {
        var board = await _boards.CreateBoardAsync("Board");
        _db.Labels.Add(new Label { BoardId = board.Id, Name = "Urgent", Colour = "rose" });
        await _db.SaveChangesAsync();

        await _boards.DeleteBoardAsync(board.Id);

        Assert.That(await _db.Labels.AnyAsync(), Is.False);
    }

    [Test]
    public void Palette_keys_are_validated()
    {
        Assert.That(LabelColours.IsValid("mint"), Is.True);
        Assert.That(LabelColours.IsValid("cyan"), Is.True);
        Assert.That(LabelColours.IsValid("scarlet"), Is.False);
        Assert.That(LabelColours.IsValid(null), Is.False);
        Assert.That(LabelColours.All.Count, Is.EqualTo(6));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LabelRepositoryTests"`
Expected: FAIL — does not compile (`Label`, `WendDbContext.Labels`, `LabelColours` don't exist).

- [ ] **Step 3: Create the entities** — `Wend.Core/Label.cs`

```csharp
namespace Wend.Core;

/// <summary>A board-scoped, reusable label — a {name, colour} tag any card on the board can
/// carry (many-to-many via CardLabel). Colour is a palette key, not a hex value.</summary>
public class Label
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "";
}
```

`Wend.Core/CardLabel.cs`:

```csharp
namespace Wend.Core;

/// <summary>Join row: a card carries a label. Composite key (CardId, LabelId); deleting either
/// the card or the label cascades this row away.</summary>
public class CardLabel
{
    public int CardId { get; set; }
    public int LabelId { get; set; }
}
```

`Wend.Core/LabelColours.cs`:

```csharp
namespace Wend.Core;

/// <summary>The curated label palette. Colours live in CSS; the database stores only these keys,
/// validated here so an unknown colour can never be persisted.</summary>
public static class LabelColours
{
    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { "mint", "cyan", "amber", "rose", "lilac", "slate" };

    public static bool IsValid(string? colour) => colour is not null && All.Contains(colour);
}
```

- [ ] **Step 4: Add the Board.Labels navigation** — `Wend.Core/Board.cs`

Add the `Labels` collection (mirrors `Lists`). The full file becomes:

```csharp
namespace Wend.Core;

/// <summary>A board — the top-level container for its lists and cards.</summary>
public class Board
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    // A board's lists. Required FK on List.BoardId → deleting a board cascades to them.
    public ICollection<List> Lists { get; set; } = [];

    // A board's labels. Required FK on Label.BoardId → deleting a board cascades to them.
    public ICollection<Label> Labels { get; set; } = [];
}
```

- [ ] **Step 5: Add the DbSets + join config** — `Wend.Core/WendDbContext.cs`

Replace the file with:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>EF Core context for Wend's SQLite database.</summary>
public class WendDbContext(DbContextOptions<WendDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<List> Lists => Set<List>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<CardLabel> CardLabels => Set<CardLabel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Hide soft-deleted / archived cards from every query. Plans 6-7 set these timestamps;
        // until then the filter is inert (no card ever has them set).
        modelBuilder.Entity<Card>().HasQueryFilter(c => c.DeletedAt == null && c.ArchivedAt == null);

        // Join table: composite key + two required FKs. Each principal (card, label) cascades
        // its join rows on delete (EF default for required relationships).
        modelBuilder.Entity<CardLabel>().HasKey(cl => new { cl.CardId, cl.LabelId });
        modelBuilder.Entity<CardLabel>().HasOne<Card>().WithMany().HasForeignKey(cl => cl.CardId);
        modelBuilder.Entity<CardLabel>().HasOne<Label>().WithMany().HasForeignKey(cl => cl.LabelId);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LabelRepositoryTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add Wend.Core/Label.cs Wend.Core/CardLabel.cs Wend.Core/LabelColours.cs Wend.Core/Board.cs Wend.Core/WendDbContext.cs Wend.Tests/LabelRepositoryTests.cs
git commit -m "Add Label and CardLabel entities with board cascade and palette keys"
```

---

## Task 2: Repository — create, get-for-board, get-by-id, edit, delete

**Files:**
- Create: `Wend.Core/ILabelRepository.cs`, `Wend.Core/EfLabelRepository.cs`
- Modify: `Wend.Tests/LabelRepositoryTests.cs`

- [ ] **Step 1: Add the repo field + failing tests** — `Wend.Tests/LabelRepositoryTests.cs`

Add a field and initialise it at the end of `SetUp`:

```csharp
    private EfLabelRepository _labels = null!;
    // ...at the end of SetUp:
    _labels = new EfLabelRepository(_db);
```

Append these tests to the class:

```csharp
    [Test]
    public async Task Create_returns_a_board_scoped_label()
    {
        var board = await _boards.CreateBoardAsync("Board");

        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");

        Assert.That(label.Id, Is.GreaterThan(0));
        Assert.That(label.BoardId, Is.EqualTo(board.Id));
        Assert.That(label.Name, Is.EqualTo("Urgent"));
        Assert.That(label.Colour, Is.EqualTo("rose"));
    }

    [Test]
    public async Task Get_for_board_lists_labels_in_creation_order()
    {
        var board = await _boards.CreateBoardAsync("Board");
        await _labels.CreateLabelAsync(board.Id, "First", "mint");
        await _labels.CreateLabelAsync(board.Id, "Second", "cyan");

        var labels = await _labels.GetBoardLabelsAsync(board.Id);

        Assert.That(labels.Select(l => l.Name), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Get_for_board_excludes_other_boards()
    {
        var a = await _boards.CreateBoardAsync("A");
        var b = await _boards.CreateBoardAsync("B");
        await _labels.CreateLabelAsync(a.Id, "OnA", "mint");
        await _labels.CreateLabelAsync(b.Id, "OnB", "cyan");

        var labels = await _labels.GetBoardLabelsAsync(a.Id);

        Assert.That(labels.Select(l => l.Name), Is.EqualTo(new[] { "OnA" }));
    }

    [Test]
    public async Task Get_label_returns_it_or_null()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var created = await _labels.CreateLabelAsync(board.Id, "Find me", "amber");

        Assert.That((await _labels.GetLabelAsync(created.Id))!.Name, Is.EqualTo("Find me"));
        Assert.That(await _labels.GetLabelAsync(9999), Is.Null);
    }

    [Test]
    public async Task Edit_updates_name_and_colour_and_reports_missing()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var label = await _labels.CreateLabelAsync(board.Id, "Old", "mint");

        Assert.That(await _labels.EditLabelAsync(label.Id, "New", "lilac"), Is.True);
        var saved = (await _labels.GetLabelAsync(label.Id))!;
        Assert.That(saved.Name, Is.EqualTo("New"));
        Assert.That(saved.Colour, Is.EqualTo("lilac"));

        Assert.That(await _labels.EditLabelAsync(9999, "X", "mint"), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_label_and_reports_missing()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var label = await _labels.CreateLabelAsync(board.Id, "Temp", "slate");

        Assert.That(await _labels.DeleteLabelAsync(label.Id), Is.True);
        Assert.That(await _labels.GetLabelAsync(label.Id), Is.Null);
        Assert.That(await _labels.DeleteLabelAsync(9999), Is.False);
    }

    [Test]
    public async Task Deleting_a_label_cascades_its_join_rows()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");
        _db.CardLabels.Add(new CardLabel { CardId = card.Id, LabelId = label.Id });
        await _db.SaveChangesAsync();

        await _labels.DeleteLabelAsync(label.Id);

        Assert.That(await _db.CardLabels.AnyAsync(), Is.False);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LabelRepositoryTests"`
Expected: FAIL — `EfLabelRepository` and its methods don't exist.

- [ ] **Step 3: Define the seam** — `Wend.Core/ILabelRepository.cs`

```csharp
namespace Wend.Core;

/// <summary>Persistence seam for board-scoped labels and the card↔label join.</summary>
public interface ILabelRepository
{
    Task<IReadOnlyList<Label>> GetBoardLabelsAsync(int boardId);
    Task<Label?> GetLabelAsync(int id);
    Task<Label> CreateLabelAsync(int boardId, string name, string colour);
    Task<bool> EditLabelAsync(int id, string name, string colour);
    Task<bool> DeleteLabelAsync(int id);

    // Card ↔ label (Task 3).
    Task AttachAsync(int cardId, int labelId);
    Task DetachAsync(int cardId, int labelId);
    Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId);
    Task<Dictionary<int, List<int>>> GetLabelIdsByCardAsync(int boardId);
}
```

- [ ] **Step 4: Implement create / get / edit / delete** — `Wend.Core/EfLabelRepository.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfLabelRepository(WendDbContext db) : ILabelRepository
{
    public async Task<IReadOnlyList<Label>> GetBoardLabelsAsync(int boardId) =>
        await db.Labels.Where(l => l.BoardId == boardId).OrderBy(l => l.Id).ToListAsync();

    public async Task<Label?> GetLabelAsync(int id) => await db.Labels.FindAsync(id);

    public async Task<Label> CreateLabelAsync(int boardId, string name, string colour)
    {
        var label = new Label { BoardId = boardId, Name = name, Colour = colour };
        db.Labels.Add(label);
        await db.SaveChangesAsync();
        return label;
    }

    public async Task<bool> EditLabelAsync(int id, string name, string colour)
    {
        var label = await db.Labels.FindAsync(id);
        if (label is null) return false;
        label.Name = name;
        label.Colour = colour;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteLabelAsync(int id)
    {
        var label = await db.Labels.FindAsync(id);
        if (label is null) return false;
        db.Labels.Remove(label);
        await db.SaveChangesAsync(); // CardLabel rows cascade at the DB level
        return true;
    }

    // Attach / detach / reads arrive in Task 3.
    public Task AttachAsync(int cardId, int labelId) => throw new NotImplementedException();
    public Task DetachAsync(int cardId, int labelId) => throw new NotImplementedException();
    public Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId) => throw new NotImplementedException();
    public Task<Dictionary<int, List<int>>> GetLabelIdsByCardAsync(int boardId) => throw new NotImplementedException();
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LabelRepositoryTests"`
Expected: PASS (10 tests).

- [ ] **Step 6: Commit**

```bash
git add Wend.Core/ILabelRepository.cs Wend.Core/EfLabelRepository.cs Wend.Tests/LabelRepositoryTests.cs
git commit -m "Add label create, board listing, get, edit and delete"
```

---

## Task 3: Repository — attach / detach / card labels / label-ids-by-card

**Files:**
- Modify: `Wend.Core/EfLabelRepository.cs`, `Wend.Tests/LabelRepositoryTests.cs`

- [ ] **Step 1: Add the failing tests** — append to `LabelRepositoryTests`

```csharp
    [Test]
    public async Task Attach_links_a_card_and_a_label_and_is_idempotent()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");

        await _labels.AttachAsync(card.Id, label.Id);
        await _labels.AttachAsync(card.Id, label.Id); // again — no duplicate, no throw

        Assert.That(await _db.CardLabels.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Detach_unlinks_and_is_a_no_op_when_not_attached()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");

        await _labels.DetachAsync(card.Id, label.Id); // nothing attached yet — no throw
        await _labels.AttachAsync(card.Id, label.Id);
        await _labels.DetachAsync(card.Id, label.Id);

        Assert.That(await _db.CardLabels.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Get_card_labels_returns_attached_labels_in_id_order()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var a = await _labels.CreateLabelAsync(board.Id, "A", "mint");
        var b = await _labels.CreateLabelAsync(board.Id, "B", "cyan");
        await _labels.AttachAsync(card.Id, b.Id);
        await _labels.AttachAsync(card.Id, a.Id);

        var attached = await _labels.GetCardLabelsAsync(card.Id);

        Assert.That(attached.Select(l => l.Name), Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public async Task Deleting_a_card_removes_its_join_rows()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");
        await _labels.AttachAsync(card.Id, label.Id);

        await _cards.DeleteCardAsync(card.Id);

        Assert.That(await _db.CardLabels.AnyAsync(), Is.False);
        Assert.That((await _labels.GetLabelAsync(label.Id)), Is.Not.Null); // label itself survives
    }

    [Test]
    public async Task Deleting_a_board_removes_its_labels_and_join_rows()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");
        await _labels.AttachAsync(card.Id, label.Id);

        await _boards.DeleteBoardAsync(board.Id);

        Assert.That(await _db.Labels.AnyAsync(), Is.False);
        Assert.That(await _db.CardLabels.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Label_ids_by_card_groups_only_this_boards_cards()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var a = await _labels.CreateLabelAsync(board.Id, "A", "mint");
        var b = await _labels.CreateLabelAsync(board.Id, "B", "cyan");
        await _labels.AttachAsync(card.Id, a.Id);
        await _labels.AttachAsync(card.Id, b.Id);

        var other = await _boards.CreateBoardAsync("Other");
        var otherList = await _lists.CreateListAsync(other.Id, "L");
        var otherCard = await _cards.CreateCardAsync(otherList.Id, "C");
        var c = await _labels.CreateLabelAsync(other.Id, "C", "amber");
        await _labels.AttachAsync(otherCard.Id, c.Id);

        var map = await _labels.GetLabelIdsByCardAsync(board.Id);

        Assert.That(map.Keys, Is.EquivalentTo(new[] { card.Id }));
        Assert.That(map[card.Id], Is.EquivalentTo(new[] { a.Id, b.Id }));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LabelRepositoryTests"`
Expected: FAIL — `NotImplementedException` from the four stubs.

- [ ] **Step 3: Replace the four stubs** — `Wend.Core/EfLabelRepository.cs`

Replace the four stub lines at the bottom of the class with:

```csharp
    public async Task AttachAsync(int cardId, int labelId)
    {
        var exists = await db.CardLabels.AnyAsync(cl => cl.CardId == cardId && cl.LabelId == labelId);
        if (exists) return; // idempotent — already attached
        db.CardLabels.Add(new CardLabel { CardId = cardId, LabelId = labelId });
        await db.SaveChangesAsync();
    }

    public async Task DetachAsync(int cardId, int labelId)
    {
        var row = await db.CardLabels.FirstOrDefaultAsync(cl => cl.CardId == cardId && cl.LabelId == labelId);
        if (row is null) return; // idempotent — nothing to remove
        db.CardLabels.Remove(row);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId) =>
        await (from cl in db.CardLabels
               where cl.CardId == cardId
               join l in db.Labels on cl.LabelId equals l.Id
               orderby l.Id
               select l).ToListAsync();

    public async Task<Dictionary<int, List<int>>> GetLabelIdsByCardAsync(int boardId)
    {
        // All (cardId, labelId) pairs for visible cards on this board, grouped per card.
        var pairs = await (
            from cl in db.CardLabels
            join card in db.Cards on cl.CardId equals card.Id
            join list in db.Lists on card.ListId equals list.Id
            where list.BoardId == boardId
            select new { cl.CardId, cl.LabelId }).ToListAsync();

        return pairs.GroupBy(p => p.CardId)
                    .ToDictionary(g => g.Key, g => g.Select(p => p.LabelId).ToList());
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LabelRepositoryTests"`
Expected: PASS (16 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Core/EfLabelRepository.cs Wend.Tests/LabelRepositoryTests.cs
git commit -m "Add card-label attach, detach and read queries"
```

---

## Task 4: Board-label endpoints (GET palette, POST create) + wiring

**Files:**
- Create: `Wend.Api/LabelEndpoints.cs`
- Modify: `Wend.Api/Program.cs`
- Test: `Wend.Tests/LabelApiTests.cs`

- [ ] **Step 1: Write the failing tests** — `Wend.Tests/LabelApiTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class LabelApiTests
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

    private record BoardDto(int Id, string Title);
    private record ListDto(int Id, string Title, int Position);
    private record CardDto(int Id, string Title, int Position);
    private record LabelDto(int Id, string Name, string Colour);

    private async Task<BoardDto> CreateBoardAsync(string title) =>
        (await (await _client.PostAsJsonAsync("/api/boards", new { title })).Content.ReadFromJsonAsync<BoardDto>())!;

    private async Task<ListDto> CreateListAsync(int boardId, string title) =>
        (await (await _client.PostAsJsonAsync($"/api/boards/{boardId}/lists", new { title })).Content.ReadFromJsonAsync<ListDto>())!;

    private async Task<CardDto> CreateCardAsync(int listId, string title) =>
        (await (await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title })).Content.ReadFromJsonAsync<CardDto>())!;

    private async Task<LabelDto> CreateLabelAsync(int boardId, string name, string colour) =>
        (await (await _client.PostAsJsonAsync($"/api/boards/{boardId}/labels", new { name, colour })).Content.ReadFromJsonAsync<LabelDto>())!;

    [Test]
    public async Task Posting_a_label_creates_it_on_the_board()
    {
        var board = await CreateBoardAsync("Board");

        var res = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/labels", new { name = "Urgent", colour = "rose" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await res.Content.ReadFromJsonAsync<LabelDto>();
        Assert.That(created!.Name, Is.EqualTo("Urgent"));
        Assert.That(created.Colour, Is.EqualTo("rose"));
    }

    [Test]
    public async Task Get_lists_the_boards_palette_in_order()
    {
        var board = await CreateBoardAsync("Board");
        await CreateLabelAsync(board.Id, "First", "mint");
        await CreateLabelAsync(board.Id, "Second", "cyan");

        var palette = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");

        Assert.That(palette!.Select(l => l.Name), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Posting_a_blank_label_name_is_rejected()
    {
        var board = await CreateBoardAsync("Board");
        var res = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/labels", new { name = "  ", colour = "mint" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_over_long_label_name_is_rejected()
    {
        var board = await CreateBoardAsync("Board");
        var res = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/labels", new { name = new string('x', 51), colour = "mint" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_unknown_colour_is_rejected()
    {
        var board = await CreateBoardAsync("Board");
        var res = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/labels", new { name = "Urgent", colour = "scarlet" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Labels_for_a_missing_board_are_404()
    {
        var get = await _client.GetAsync("/api/boards/9999/labels");
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var post = await _client.PostAsJsonAsync("/api/boards/9999/labels", new { name = "X", colour = "mint" });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LabelApiTests"`
Expected: FAIL — the label routes return 404 (no routes registered).

- [ ] **Step 3: Create the endpoint group** — `Wend.Api/LabelEndpoints.cs`

```csharp
using Wend.Core;

namespace Wend.Api;

public static class LabelEndpoints
{
    private const int MaxNameLength = 50;

    public static IEndpointRouteBuilder MapLabelEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/boards/{boardId:int}/labels",
            async (int boardId, IBoardRepository boards, ILabelRepository labels) =>
            {
                if (await boards.GetBoardAsync(boardId) is null) return Results.NotFound();
                var palette = (await labels.GetBoardLabelsAsync(boardId))
                    .Select(l => new LabelDto(l.Id, l.Name, l.Colour));
                return Results.Ok(palette);
            });

        app.MapPost("/api/boards/{boardId:int}/labels",
            async (int boardId, CreateLabelRequest req, IBoardRepository boards, ILabelRepository labels) =>
            {
                var name = req.Name?.Trim() ?? "";
                if (name.Length is 0 or > MaxNameLength) return Results.BadRequest();
                if (!LabelColours.IsValid(req.Colour)) return Results.BadRequest();
                if (await boards.GetBoardAsync(boardId) is null) return Results.NotFound();
                var label = await labels.CreateLabelAsync(boardId, name, req.Colour);
                return Results.Created($"/api/labels/{label.Id}", new LabelDto(label.Id, label.Name, label.Colour));
            });

        // PUT / DELETE / attach / detach arrive in Tasks 5-6.
        return app;
    }
}

public record LabelDto(int Id, string Name, string Colour);
public record CreateLabelRequest(string Name, string Colour);
```

- [ ] **Step 4: Register the repo + map the group** — `Wend.Api/Program.cs`

After the line registering `ICardRepository` (`builder.Services.AddScoped<ICardRepository, EfCardRepository>();`), add:

```csharp
builder.Services.AddScoped<ILabelRepository, EfLabelRepository>();
```

After `app.MapCardEndpoints();`, add:

```csharp
app.MapLabelEndpoints();
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LabelApiTests"`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/LabelEndpoints.cs Wend.Api/Program.cs Wend.Tests/LabelApiTests.cs
git commit -m "Add board label list and create endpoints"
```

---

## Task 5: Label edit (PUT) & delete (DELETE) endpoints

**Files:**
- Modify: `Wend.Api/LabelEndpoints.cs`, `Wend.Tests/LabelApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append inside `LabelApiTests`

```csharp
    [Test]
    public async Task Put_edits_a_labels_name_and_colour()
    {
        var board = await CreateBoardAsync("Board");
        var label = await CreateLabelAsync(board.Id, "Old", "mint");

        var put = await _client.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = "New", colour = "lilac" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var palette = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        Assert.That(palette!.Single().Name, Is.EqualTo("New"));
        Assert.That(palette.Single().Colour, Is.EqualTo("lilac"));
    }

    [Test]
    public async Task Put_rejects_a_bad_name_or_colour()
    {
        var board = await CreateBoardAsync("Board");
        var label = await CreateLabelAsync(board.Id, "Old", "mint");

        var blank = await _client.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = " ", colour = "mint" });
        Assert.That(blank.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var badColour = await _client.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = "Ok", colour = "scarlet" });
        Assert.That(badColour.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Put_to_a_missing_label_is_404()
    {
        var put = await _client.PutAsJsonAsync("/api/labels/9999", new { name = "X", colour = "mint" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_removes_a_label()
    {
        var board = await CreateBoardAsync("Board");
        var label = await CreateLabelAsync(board.Id, "Temp", "slate");

        var del = await _client.DeleteAsync($"/api/labels/{label.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var palette = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        Assert.That(palette, Is.Empty);
    }

    [Test]
    public async Task Delete_a_missing_label_is_404()
    {
        var del = await _client.DeleteAsync("/api/labels/9999");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LabelApiTests"`
Expected: FAIL — `PUT` / `DELETE /api/labels/{id}` return 404 (no routes).

- [ ] **Step 3: Add the routes + edit DTO** — `Wend.Api/LabelEndpoints.cs`

Inside `MapLabelEndpoints`, before `return app;`, add:

```csharp
        app.MapPut("/api/labels/{id:int}", async (int id, EditLabelRequest req, ILabelRepository labels) =>
        {
            var name = req.Name?.Trim() ?? "";
            if (name.Length is 0 or > MaxNameLength) return Results.BadRequest();
            if (!LabelColours.IsValid(req.Colour)) return Results.BadRequest();
            return await labels.EditLabelAsync(id, name, req.Colour)
                ? Results.NoContent() : Results.NotFound();
        });

        app.MapDelete("/api/labels/{id:int}", async (int id, ILabelRepository labels) =>
            await labels.DeleteLabelAsync(id) ? Results.NoContent() : Results.NotFound());
```

Add at the bottom of the file (next to `CreateLabelRequest`):

```csharp
public record EditLabelRequest(string Name, string Colour);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LabelApiTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/LabelEndpoints.cs Wend.Tests/LabelApiTests.cs
git commit -m "Add label edit and delete endpoints"
```

---

## Task 6: Card-label endpoints (attach / detach)

**Files:**
- Modify: `Wend.Api/LabelEndpoints.cs`, `Wend.Tests/LabelApiTests.cs`

- [ ] **Step 1: Add the failing tests** — append inside `LabelApiTests`

```csharp
    [Test]
    public async Task Attaching_a_label_to_a_card_succeeds_and_is_idempotent()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");

        var first = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var again = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); // idempotent
    }

    [Test]
    public async Task Attaching_a_label_from_another_board_is_rejected()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var other = await CreateBoardAsync("Other");
        var foreign = await CreateLabelAsync(other.Id, "Foreign", "mint");

        var res = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = foreign.Id });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Attaching_to_a_missing_card_or_label_is_404()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");

        var missingCard = await _client.PostAsJsonAsync("/api/cards/9999/labels", new { labelId = label.Id });
        Assert.That(missingCard.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var missingLabel = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = 9999 });
        Assert.That(missingLabel.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Detaching_is_always_204_including_when_not_attached()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");

        var notAttached = await _client.DeleteAsync($"/api/cards/{card.Id}/labels/{label.Id}");
        Assert.That(notAttached.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });
        var attachedThenRemoved = await _client.DeleteAsync($"/api/cards/{card.Id}/labels/{label.Id}");
        Assert.That(attachedThenRemoved.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LabelApiTests"`
Expected: FAIL — the attach/detach routes return 404 (no routes).

- [ ] **Step 3: Add the routes + attach DTO** — `Wend.Api/LabelEndpoints.cs`

Inside `MapLabelEndpoints`, before `return app;`, add:

```csharp
        app.MapPost("/api/cards/{cardId:int}/labels",
            async (int cardId, AttachLabelRequest req, ICardRepository cards, IListRepository lists, ILabelRepository labels) =>
            {
                if (await cards.GetCardAsync(cardId) is not { } card) return Results.NotFound();
                if (await labels.GetLabelAsync(req.LabelId) is not { } label) return Results.NotFound();
                var list = await lists.GetListAsync(card.ListId);
                if (list is null || list.BoardId != label.BoardId) return Results.BadRequest(); // cross-board
                await labels.AttachAsync(cardId, req.LabelId); // idempotent
                return Results.NoContent();
            });

        app.MapDelete("/api/cards/{cardId:int}/labels/{labelId:int}",
            async (int cardId, int labelId, ILabelRepository labels) =>
            {
                await labels.DetachAsync(cardId, labelId); // idempotent — always 204
                return Results.NoContent();
            });
```

Add at the bottom of the file:

```csharp
public record AttachLabelRequest(int LabelId);
```

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: PASS — all prior tests plus this plan's label repository (16) and label API (15) tests.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/LabelEndpoints.cs Wend.Tests/LabelApiTests.cs
git commit -m "Add card-label attach and detach endpoints"
```

---

## Task 7: Nest the palette + per-card labelIds in the board detail

**Files:**
- Modify: `Wend.Api/BoardEndpoints.cs`, `Wend.Tests/LabelApiTests.cs`

- [ ] **Step 1: Add the board-detail DTOs + failing test** — append inside `LabelApiTests`

Add these test-only record shapes next to the others at the top of the class:

```csharp
    private record CardSummaryDto(int Id, string Title, string? DueDate, int Position, List<int> LabelIds);
    private record ListWithCardsDto(int Id, string Title, int Position, List<CardSummaryDto> Cards);
    private record BoardDetailDto(int Id, string Title, List<LabelDto> Labels, List<ListWithCardsDto> Lists);
```

And the test:

```csharp
    [Test]
    public async Task Board_detail_includes_the_palette_and_each_cards_label_ids()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");
        await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");

        Assert.That(detail!.Labels.Select(l => l.Name), Is.EqualTo(new[] { "Urgent" }));
        Assert.That(detail.Lists.Single().Cards.Single().LabelIds, Is.EqualTo(new[] { label.Id }));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LabelApiTests.Board_detail_includes_the_palette_and_each_cards_label_ids"`
Expected: FAIL — `Labels` deserialises to null and the card has no `LabelIds`.

- [ ] **Step 3: Extend the board detail** — `Wend.Api/BoardEndpoints.cs`

Replace the existing `group.MapGet("/{id:int}", …)` handler with one that also takes `ILabelRepository` and fills the palette + per-card label ids:

```csharp
        group.MapGet("/{id:int}", async (int id, IBoardRepository boards, IListRepository lists, ICardRepository cards, ILabelRepository labels) =>
        {
            if (await boards.GetBoardAsync(id) is not { } board) return Results.NotFound();

            var palette = (await labels.GetBoardLabelsAsync(id))
                .Select(l => new LabelDto(l.Id, l.Name, l.Colour)).ToList();
            var labelIdsByCard = await labels.GetLabelIdsByCardAsync(id);

            var summaries = new List<ListSummary>();
            foreach (var l in await lists.GetListsForBoardAsync(id))
            {
                var cardSummaries = (await cards.GetCardsForListAsync(l.Id))
                    .Select(c => new CardSummary(c.Id, c.Title, c.DueDate, c.Position,
                        labelIdsByCard.TryGetValue(c.Id, out var ids) ? ids : new List<int>()))
                    .ToList();
                summaries.Add(new ListSummary(l.Id, l.Title, l.Position, cardSummaries));
            }
            return Results.Ok(new BoardDetail(board.Id, board.Title, palette, summaries));
        });
```

Replace the `BoardDetail` and `CardSummary` records at the bottom of the file (keep `CreateBoardRequest` / `RenameBoardRequest`):

```csharp
public record BoardDetail(int Id, string Title, IReadOnlyList<LabelDto> Labels, IReadOnlyList<ListSummary> Lists);
public record ListSummary(int Id, string Title, int Position, IReadOnlyList<CardSummary> Cards);
public record CardSummary(int Id, string Title, DateOnly? DueDate, int Position, IReadOnlyList<int> LabelIds);
```

(`LabelDto` is the record defined in `LabelEndpoints.cs` — same `Wend.Api` namespace, no import needed.)

- [ ] **Step 4: Run the related suites**

Run: `dotnet test --filter "FullyQualifiedName~LabelApiTests"` → PASS (16).
Run: `dotnet test --filter "FullyQualifiedName~CardApiTests"` → still PASS — `CardApiTests` deserialises board detail with its own DTOs that omit `labels` / `labelIds`, so the added fields are ignored.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/BoardEndpoints.cs Wend.Tests/LabelApiTests.cs
git commit -m "Nest label palette and per-card label ids in the board detail"
```

---

## Task 8: Add boardId + attached labels to the card detail

**Files:**
- Modify: `Wend.Api/CardEndpoints.cs`, `Wend.Tests/LabelApiTests.cs`

- [ ] **Step 1: Add the card-detail DTO + failing test** — append inside `LabelApiTests`

Add the DTO next to the others:

```csharp
    private record CardDetailDto(int Id, int ListId, string ListTitle, int BoardId, string Title, string? Description, string? DueDate, int Position, List<LabelDto> Labels);
```

And the test:

```csharp
    [Test]
    public async Task Card_detail_includes_board_id_and_attached_labels()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");
        await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });

        var detail = await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}");

        Assert.That(detail!.BoardId, Is.EqualTo(board.Id));
        Assert.That(detail.Labels.Select(l => l.Name), Is.EqualTo(new[] { "Urgent" }));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LabelApiTests.Card_detail_includes_board_id_and_attached_labels"`
Expected: FAIL — `BoardId` is 0 and `Labels` is null.

- [ ] **Step 3: Extend the card detail** — `Wend.Api/CardEndpoints.cs`

Replace the existing `app.MapGet("/api/cards/{id:int}", …)` handler with:

```csharp
        app.MapGet("/api/cards/{id:int}", async (int id, ICardRepository cards, IListRepository lists, ILabelRepository labels) =>
        {
            if (await cards.GetCardAsync(id) is not { } c) return Results.NotFound();
            var list = await lists.GetListAsync(c.ListId);
            var attached = (await labels.GetCardLabelsAsync(c.Id))
                .Select(l => new LabelDto(l.Id, l.Name, l.Colour)).ToList();
            return Results.Ok(new CardDetail(c.Id, c.ListId, list?.Title ?? "", list?.BoardId ?? 0,
                c.Title, c.Description, c.DueDate, c.Position, attached));
        });
```

Replace the `CardDetail` record at the bottom of the file:

```csharp
public record CardDetail(int Id, int ListId, string ListTitle, int BoardId, string Title, string? Description, DateOnly? DueDate, int Position, IReadOnlyList<LabelDto> Labels);
```

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: PASS — `CardApiTests`' own `CardDetailDto` omits the new fields and still binds the rest by name, so all prior tests stay green; label API tests now 17.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/CardEndpoints.cs Wend.Tests/LabelApiTests.cs
git commit -m "Add board id and attached labels to the card detail"
```

---

## Task 9: Label chip palette + picker styles (CSS)

**Files:**
- Modify: `Wend.Api/wwwroot/css/app.css`

No automated test — these styles are exercised visually from Task 10 on. **No behaviour change**; pure CSS. The colour values are brightened slightly from the brainstorm mockup so the *text* clears AA on the dark surface at a faint 10% wash (verified in Task 10).

- [ ] **Step 1: Append the label styles** — `Wend.Api/wwwroot/css/app.css`

```css
/* ---- Labels ----------------------------------------------------------- */

/* Curated palette. Each key sets a bright text colour (`--chip`) used for the
   label text, a faint same-hue wash behind it, and a matching border. The name
   is always rendered, so colour is never the only signal. */
.label-chip {
  --chip: #aab6c6;
  display: inline-flex;
  align-items: center;
  max-width: 100%;
  padding: 0.1rem 0.5rem;
  border-radius: 999px;
  font-size: 0.72rem;
  line-height: 1.5;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--chip);
  background: color-mix(in srgb, var(--chip) 10%, transparent);
  border: 1px solid color-mix(in srgb, var(--chip) 38%, transparent);
}
.label-chip--mint  { --chip: #6fe9c6; }
.label-chip--cyan  { --chip: #3fd0dc; }
.label-chip--amber { --chip: #f2b84b; }
.label-chip--rose  { --chip: #ff8fa3; }
.label-chip--lilac { --chip: #c3acff; }
.label-chip--slate { --chip: #aab6c6; }

/* Card-front label row (board view). */
.card-chip-labels { display: flex; flex-wrap: wrap; gap: 0.25rem; }

/* Task-view labels section + non-modal picker. */
.labels-section { margin: 0.25rem 0 1rem; }
.labels-attached { display: flex; flex-wrap: wrap; gap: 0.35rem; margin-bottom: 0.5rem; }
.labels-empty { font-size: 0.8rem; opacity: 0.6; }

.label-picker {
  margin-top: 0.6rem;
  padding: 0.6rem;
  border: 1px solid color-mix(in srgb, currentColor 18%, transparent);
  border-radius: 0.6rem;
}
.label-list { list-style: none; margin: 0 0 0.5rem; padding: 0; display: flex; flex-direction: column; gap: 0.3rem; }
.label-row { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; }
.label-toggle { display: inline-flex; align-items: center; gap: 0.5rem; cursor: pointer; min-height: 44px; }
.label-row-actions { display: flex; gap: 0.25rem; }
.labels-toggle, .label-create-open, .label-row-actions button { min-height: 44px; }

/* Create / edit form (name + colour swatches). */
.label-form .field { display: flex; flex-direction: column; gap: 0.25rem; margin-bottom: 0.5rem; }
.swatches { display: flex; flex-wrap: wrap; gap: 0.5rem; border: 0; padding: 0; margin: 0 0 0.6rem; }
.swatches legend { font-size: 0.8rem; opacity: 0.7; padding: 0; }
.swatch { display: inline-flex; flex-direction: column; align-items: center; gap: 0.2rem; cursor: pointer; min-width: 44px; }
.swatch-dot { width: 1.4rem; height: 1.4rem; border-radius: 50%; display: inline-block; background: var(--chip); }
.swatch-name { font-size: 0.65rem; opacity: 0.75; }
.label-form-actions { display: flex; gap: 0.5rem; }
```

- [ ] **Step 2: Commit**

```bash
git add Wend.Api/wwwroot/css/app.css
git commit -m "Add label chip palette and picker styles"
```

---

## Task 10: Board view — render label chips on card fronts

**Files:**
- Modify: `Wend.Api/wwwroot/js/board/view.js`

No automated test — verify by browser + a contrast check. Card chips render their labels (resolved from the board palette) above the title; the chip's accessible name also lists the label names, so the board front never reduces a label to colour-only for a screen reader.

- [ ] **Step 1: Replace the board view** — `Wend.Api/wwwroot/js/board/view.js`

```js
import { escapeHtml } from "../escape.js";

// Renders one board's view: back link, title, add-list form, and each list with its
// move/rename/delete controls, its cards (with label chips), and an add-card form.
export function createBoardView(root) {
  function render(board) {
    const lists = board.lists;
    const labelsById = new Map((board.labels ?? []).map((l) => [l.id, l]));

    // Soft-tint chips for a card's labels (skips ids missing from the palette).
    const labelChips = (ids) =>
      (ids ?? [])
        .map((id) => labelsById.get(id))
        .filter(Boolean)
        .map((l) => `<span class="label-chip label-chip--${l.colour}">${escapeHtml(l.name)}</span>`)
        .join("");

    const cardAria = (c) => {
      const names = (c.labelIds ?? []).map((id) => labelsById.get(id)).filter(Boolean).map((l) => l.name);
      return `Open card: ${c.title}${names.length ? `, labels: ${names.join(", ")}` : ""}`;
    };

    const items = lists.length
      ? lists
          .map((l, i) => {
            const first = i === 0;
            const last = i === lists.length - 1;
            const cards = (l.cards ?? [])
              .map((c) => {
                const chips = labelChips(c.labelIds);
                return `
            <li>
              <button class="card-chip" data-action="open-card" data-card-id="${c.id}"
                aria-label="${escapeHtml(cardAria(c))}">
                ${chips ? `<span class="card-chip-labels">${chips}</span>` : ""}
                <span class="card-title">${escapeHtml(c.title)}</span>
                ${c.dueDate ? `<span class="card-due">${escapeHtml(c.dueDate)}</span>` : ""}
              </button>
            </li>`;
              })
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
  function focusCard(cardId) {
    root.querySelector(`.card-chip[data-card-id="${cardId}"]`)?.focus();
  }

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

- [ ] **Step 2: Verify in the browser + check contrast**

If you haven't yet this plan, delete the dev DB so the new tables are created:
`Remove-Item "$env:LOCALAPPDATA\Wend\data.db" -ErrorAction SilentlyContinue`

Run: `dotnet run --project Wend.Api`, open `http://127.0.0.1:5174`. There's no UI to attach labels yet (Task 11), so seed one pair by hand to see the chip — in the browser console:

```js
// create a label on board 1, then attach it to card 1 (adjust ids to ones you have)
await fetch("/api/boards/1/labels", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: "Urgent", colour: "rose" }) }).then(r => r.json());
await fetch("/api/cards/1/labels", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ labelId: 1 }) });
location.reload();
```

Check:
- The card shows a soft-tint "Urgent" chip above its title.
- **Contrast gate:** with devtools, inspect a `.label-chip` and confirm its text/background contrast is **≥ 4.5:1**. Repeat for each colour (create one label per key). If any misses, brighten that `--chip` value in `app.css` (or lower the wash from `10%` toward `8%`) until all six pass. Slate and cyan are the tightest.
- A screen reader reads the card button as "Open card: <title>, labels: Urgent".

Stop with `Ctrl+C`.

- [ ] **Step 3: Commit**

```bash
git add Wend.Api/wwwroot/js/board/view.js Wend.Api/wwwroot/css/app.css
git commit -m "Render label chips on card fronts in the board view"
```

---

## Task 11: Task-view label picker (attach / detach / create / edit / delete)

**Files:**
- Create: `Wend.Api/wwwroot/js/card/labels.js`
- Modify: `Wend.Api/wwwroot/js/card/model.js`, `view.js`, `controller.js`

No automated test — verify by a browser + screen-reader pass (Step 5). These four files change together: the model gains the palette + label methods, `labels.js` renders the section, the view composes it and owns the picker's transient UI state, and the controller wires the server actions + focus. `main.js` needs **no change** (the existing `showCard` already wires `onBack` / `onDeleted`, and the model loads the palette itself).

- [ ] **Step 1: Extend the card model** — replace `Wend.Api/wwwroot/js/card/model.js`

```js
import { api } from "../api.js";

// State for one card plus its board's label palette. Re-fetches after every change so the view
// always shows server truth. No DOM. Subscribers get (card, palette).
export function createCardModel(cardId) {
  let card = { id: cardId, listId: 0, listTitle: "", boardId: 0, title: "", description: "", dueDate: null, position: 0, labels: [] };
  let palette = [];
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(card, palette));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(card, palette);
    },
    async load() {
      card = await api(`/api/cards/${cardId}`);
      palette = await api(`/api/boards/${card.boardId}/labels`);
      notify();
    },
    async save({ title, description, dueDate }) {
      await api(`/api/cards/${cardId}`, { method: "PUT", body: JSON.stringify({ title, description, dueDate }) });
      await this.load();
    },
    async remove() {
      await api(`/api/cards/${cardId}`, { method: "DELETE" });
    },
    async attachLabel(labelId) {
      await api(`/api/cards/${cardId}/labels`, { method: "POST", body: JSON.stringify({ labelId }) });
      await this.load();
    },
    async detachLabel(labelId) {
      await api(`/api/cards/${cardId}/labels/${labelId}`, { method: "DELETE" });
      await this.load();
    },
    async createLabel(name, colour) {
      const label = await api(`/api/boards/${card.boardId}/labels`, { method: "POST", body: JSON.stringify({ name, colour }) });
      await api(`/api/cards/${cardId}/labels`, { method: "POST", body: JSON.stringify({ labelId: label.id }) }); // auto-attach
      await this.load();
    },
    async editLabel(id, name, colour) {
      await api(`/api/labels/${id}`, { method: "PUT", body: JSON.stringify({ name, colour }) });
      await this.load();
    },
    async deleteLabel(id) {
      await api(`/api/labels/${id}`, { method: "DELETE" });
      await this.load();
    },
  };
}
```

- [ ] **Step 2: The labels render helper** — `Wend.Api/wwwroot/js/card/labels.js`

```js
import { escapeHtml } from "../escape.js";

export const LABEL_COLOURS = ["mint", "cyan", "amber", "rose", "lilac", "slate"];

// Returns the Labels-section HTML from (card, palette, ui). Pure — no state, no fetch.
// ui = { pickerOpen, mode: "list" | "create" | "edit", editingId }.
export function renderLabels(card, palette, ui) {
  const attached = card.labels ?? [];
  const attachedChips = attached.length
    ? attached.map((l) => `<span class="label-chip label-chip--${l.colour}">${escapeHtml(l.name)}</span>`).join("")
    : `<span class="labels-empty">No labels yet</span>`;

  let body = "";
  if (ui.pickerOpen) {
    if (ui.mode === "create" || ui.mode === "edit") {
      body = labelForm(ui, palette);
    } else {
      const rows = palette.length
        ? palette
            .map((l) => {
              const on = attached.some((a) => a.id === l.id);
              return `
            <li class="label-row">
              <label class="label-toggle">
                <input type="checkbox" data-action="toggle-label" data-label-id="${l.id}" ${on ? "checked" : ""} />
                <span class="label-chip label-chip--${l.colour}">${escapeHtml(l.name)}</span>
              </label>
              <span class="label-row-actions">
                <button type="button" data-action="edit-label" data-label-id="${l.id}"
                  aria-label="Edit label ${escapeHtml(l.name)}">Edit</button>
                <button type="button" data-action="delete-label" data-label-id="${l.id}"
                  aria-label="Delete label ${escapeHtml(l.name)}">Delete</button>
              </span>
            </li>`;
            })
            .join("")
        : `<li class="labels-empty">No labels on this board yet — create one.</li>`;
      body = `
        <ul class="label-list">${rows}</ul>
        <button type="button" class="label-create-open" data-action="create-label-open">＋ Create a new label</button>`;
    }
  }

  return `
    <section class="labels-section" aria-label="Labels">
      <div class="labels-attached">${attachedChips}</div>
      <button type="button" class="labels-toggle" data-action="toggle-picker"
        aria-haspopup="true" aria-expanded="${ui.pickerOpen ? "true" : "false"}">＋ Labels</button>
      ${ui.pickerOpen ? `<div class="label-picker" role="group" aria-label="Choose labels">${body}</div>` : ""}
    </section>`;
}

// The create / edit form (name + six colour swatches), prefilled when editing.
function labelForm(ui, palette) {
  const editing = ui.mode === "edit" ? palette.find((l) => l.id === ui.editingId) : null;
  const name = editing ? editing.name : "";
  const chosen = editing ? editing.colour : LABEL_COLOURS[0];
  const swatches = LABEL_COLOURS
    .map(
      (key) => `
      <label class="swatch">
        <input type="radio" name="colour" value="${key}" ${key === chosen ? "checked" : ""} />
        <span class="swatch-dot label-chip--${key}" aria-hidden="true"></span>
        <span class="swatch-name">${key}</span>
      </label>`
    )
    .join("");
  return `
    <form class="label-form" data-action="${editing ? "save-label" : "add-label"}" ${editing ? `data-label-id="${editing.id}"` : ""}>
      <label class="field">
        <span>Label name</span>
        <input name="name" value="${escapeHtml(name)}" aria-label="Label name" maxlength="50" required />
      </label>
      <fieldset class="swatches">
        <legend>Colour</legend>
        ${swatches}
      </fieldset>
      <div class="label-form-actions">
        <button type="submit">${editing ? "Save label" : "Add label"}</button>
        <button type="button" data-action="cancel-label">Cancel</button>
      </div>
    </form>`;
}
```

- [ ] **Step 3: Compose the picker into the task view** — replace `Wend.Api/wwwroot/js/card/view.js`

```js
import { escapeHtml } from "../escape.js";
import { renderLabels } from "./labels.js";

// Task view for one card: back link, heading, the Labels section + picker, the edit form, and
// delete. Holds only transient picker UI state; all data comes from the model. The view owns the
// purely-visual picker transitions (open / create / edit); the controller owns anything that
// touches the server. Events via data-action.
export function createCardView(root) {
  let lastCard = null;
  let lastPalette = [];
  const ui = { pickerOpen: false, mode: "list", editingId: null };
  let h = {};

  function render(card, palette) {
    lastCard = card;
    lastPalette = palette ?? [];
    paint();
  }

  function paint() {
    const card = lastCard;
    root.innerHTML = `
      <div class="card-view">
        <button class="back-link" data-action="back">← Board</button>
        <h2 class="card-heading" tabindex="-1">${escapeHtml(card.title)}</h2>
        <p class="card-list-name">In list: <strong>${escapeHtml(card.listTitle)}</strong></p>
        ${renderLabels(card, lastPalette, ui)}
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

  // Focus helpers.
  function focusHeading() { root.querySelector(".card-heading")?.focus(); }
  function focusPickerTrigger() { root.querySelector(".labels-toggle")?.focus(); }
  function focusToggle(labelId) {
    root.querySelector(`input[data-action="toggle-label"][data-label-id="${labelId}"]`)?.focus();
  }
  function focusPicker() { root.querySelector(".label-picker input, .label-picker button")?.focus(); }
  function focusLabelName() { root.querySelector(".label-form input[name=name]")?.focus(); }

  // Purely-visual picker transitions (no server): flip ui + repaint + place focus.
  function openPicker() { ui.pickerOpen = true; ui.mode = "list"; paint(); focusPicker(); }
  function closePicker() { ui.pickerOpen = false; ui.mode = "list"; ui.editingId = null; paint(); focusPickerTrigger(); }
  function toCreate() { ui.mode = "create"; paint(); focusLabelName(); }
  function toEdit(id) { ui.mode = "edit"; ui.editingId = id; paint(); focusLabelName(); }
  function toList() { ui.mode = "list"; ui.editingId = null; paint(); focusPicker(); }

  function labelName(id) { return (lastPalette.find((l) => l.id === id) || {}).name || ""; }

  function bindActions(handlers) {
    h = handlers;

    root.addEventListener("submit", async (e) => {
      const action = e.target.dataset.action;
      if (action === "save") {
        e.preventDefault();
        const f = e.target;
        const submit = f.querySelector("button[type=submit]");
        submit.disabled = true;
        try {
          await h.save({ title: f.title.value.trim(), description: f.description.value, dueDate: f.dueDate.value || null });
        } finally {
          submit.disabled = false;
        }
      } else if (action === "add-label" || action === "save-label") {
        e.preventDefault();
        const f = e.target;
        const name = f.elements["name"].value.trim(); // f.name would be the form's name attr
        const colour = f.elements["colour"].value;
        if (!name) return;
        const submit = f.querySelector("button[type=submit]");
        submit.disabled = true;
        try {
          if (action === "add-label") await h.createLabel(name, colour);
          else await h.editLabel(Number(f.dataset.labelId), name, colour);
          toList();
        } finally {
          submit.disabled = false;
        }
      }
    });

    root.addEventListener("change", async (e) => {
      const cb = e.target.closest('input[data-action="toggle-label"]');
      if (!cb) return;
      const id = Number(cb.dataset.labelId);
      if (cb.checked) await h.attachLabel(id);
      else await h.detachLabel(id);
    });

    root.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && ui.pickerOpen) { e.stopPropagation(); closePicker(); }
    });

    root.addEventListener("click", (e) => {
      const btn = e.target.closest("[data-action]");
      if (!btn) return;
      const a = btn.dataset.action;
      if (["save", "add-label", "save-label", "toggle-label"].includes(a)) return; // handled by submit/change
      if (a === "back") return h.back();
      if (a === "delete") return h.delete();
      if (a === "toggle-picker") return ui.pickerOpen ? closePicker() : openPicker();
      if (a === "create-label-open") return toCreate();
      if (a === "cancel-label") return toList();
      if (a === "edit-label") return toEdit(Number(btn.dataset.labelId));
      if (a === "delete-label") {
        const id = Number(btn.dataset.labelId);
        return h.deleteLabel(id, labelName(id));
      }
    });
  }

  return { render, focusHeading, focusPickerTrigger, focusToggle, focusPicker, bindActions };
}
```

- [ ] **Step 4: Wire the label actions** — replace `Wend.Api/wwwroot/js/card/controller.js`

```js
// Wires the task view to the model: save, delete, and the full label picker (attach / detach /
// create / edit / delete). Announces each result and restores focus after server round-trips.
// onBack() returns to the board (focusing this card); onDeleted() returns to the board.
export function createCardController(model, view, announce, { onBack, onDeleted } = {}) {
  let palette = [];
  const nameOf = (id) => (palette.find((l) => l.id === id) || {}).name || "the label";

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
    attachLabel: async (id) => {
      try {
        await model.attachLabel(id);
        announce(`Added label ${nameOf(id)}.`);
        view.focusToggle(id);
      } catch {
        announce("Couldn't add the label — please try again.");
      }
    },
    detachLabel: async (id) => {
      try {
        await model.detachLabel(id);
        announce(`Removed label ${nameOf(id)}.`);
        view.focusToggle(id);
      } catch {
        announce("Couldn't remove the label — please try again.");
      }
    },
    createLabel: async (name, colour) => {
      try {
        await model.createLabel(name, colour);
        announce(`Created label ${name}.`);
      } catch {
        announce("Couldn't create the label — please try again.");
      }
    },
    editLabel: async (id, name, colour) => {
      try {
        await model.editLabel(id, name, colour);
        announce(`Updated label ${name}.`);
      } catch {
        announce("Couldn't update the label — please try again.");
      }
    },
    deleteLabel: async (id, name) => {
      if (!confirm(`Delete '${name}'? It will be removed from every card that uses it.`)) return;
      try {
        await model.deleteLabel(id);
        announce("Label deleted.");
        view.focusPicker();
      } catch {
        announce("Couldn't delete the label — please try again.");
      }
    },
  });

  model.subscribe((card, p) => {
    palette = p ?? [];
    view.render(card, p);
  });
}
```

- [ ] **Step 5: Verify in the browser + with a screen reader**

Run: `dotnet run --project Wend.Api`, open `http://127.0.0.1:5174`, open a board, open a card. Check:
- **Open/close:** "＋ Labels" opens the picker (focus moves into it, `aria-expanded` flips to true); Escape and clicking it again close it and return focus to the trigger.
- **Create (auto-attach):** "＋ Create a new label" → name + a colour swatch → Add → the label appears as an attached chip *and* in the palette list ticked; announced "Created label …". Focus lands back in the picker.
- **Attach/detach:** ticking a palette row attaches (chip appears, "Added label …"); unticking detaches ("Removed label …"); focus stays on the toggle.
- **Edit/recolour:** Edit on a row → rename + change colour → Save → the chip + palette update everywhere; announced "Updated label …".
- **Delete:** Delete on a row → confirm dialog ("…removed from every card that uses it.") → on OK the label disappears from the palette and from any card; announced "Label deleted." Cancel leaves it.
- **Persistence + board front:** back to the board — the card chip shows its current labels; reload the page — everything persists.
- **Keyboard only:** do the whole flow without a mouse; **screen reader** announces each action and reads the edit/delete buttons as "Edit label …" / "Delete label …".

Stop with `Ctrl+C`.

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/wwwroot/js/card/
git commit -m "Add the task-view label picker with attach, create, edit and delete"
```

---

## Task 12: Full-suite check, README refresh + acceptance

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Run the whole suite + a clean build**

```bash
dotnet test
dotnet build
```
Expected: all tests green (**94** NUnit — Plans 1–3 plus this plan's 16 label-repository + 17 label-API tests); `dotnet build` reports **0 warnings**.

- [ ] **Step 2: Update the Status section** — `README.md`

Change the Status paragraph (the "Boards, lists, and cards work end to end…" line) to include labels:

```markdown
Boards, lists, and cards work end to end — create, rename, delete, and reorder lists inside a board, add cards to a list, label them, and open a card into a focused task view to edit its title, notes, due date, and labels — saved to SQLite, accessible and dark-mode-first.
```

Replace the **Done** and **Next** bullets with:

```markdown
- **Done:** the board, list, card, and label backend (JSON APIs behind `IBoardRepository`, `IListRepository`, `ICardRepository`, and `ILabelRepository` seams, EF Core + SQLite, 94 NUnit tests, localhost-only) and the vanilla-JS MVC frontend (board-view navigation, accessible list reordering, card chips with a focused task view, an inline label picker with soft-tint chips on cards and the board, screen-reader announcements, keyboard focus management).
- **Next:** card moving, the Done checkmark, and a checklist.
```

Append the new spec + plan links to the "Design specs … · Build plans …" line:

- after `…2026-06-22-wend-cards-design.md`)` add `, [`docs/2026-06-23-wend-labels-design.md`](docs/2026-06-23-wend-labels-design.md)`
- after `…2026-06-22-slice1-cards.md`)` add `, [`docs/plans/2026-06-23-slice1-labels.md`](docs/plans/2026-06-23-slice1-labels.md)`

Update the **Run it** paragraph's last sentence about the task view to:

```markdown
Then open http://127.0.0.1:5174 to create boards, open one to manage its lists (create, rename, delete, reorder), and add cards — open a card for its task view to edit the title, notes, due date, and labels. The API lives under `/api/boards`, `/api/lists`, `/api/cards`, and `/api/labels`. On first run the SQLite database is created at `%LOCALAPPDATA%\Wend\data.db`.
```

- [ ] **Step 3: Final manual acceptance** (mirrors the slice's accessibility commitments)

With `dotnet run --project Wend.Api`, confirm end to end and persisted across a reload: create a label on a card (auto-attached), attach/detach an existing label, rename + recolour a label (updates on the card front and the task view), delete a label (confirm dialog, leaves every card), and the whole flow keyboard-only with screen-reader announcements. Each of the six palette colours reads ≥ 4.5:1 as chip text (Task 10 gate).

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Document labels in the README status and roadmap"
```

---

## Done when (whole-plan checklist)

- [ ] `Label` + `CardLabel` entities; `ILabelRepository` / `EfLabelRepository` behind the seam; colour validated against the curated palette (`LabelColours`).
- [ ] Cascade is clean: delete a board → labels + joins; delete a label → joins; delete a card → joins. No orphans (covered by repository tests).
- [ ] All six label endpoints live and validated; cross-board attach rejected (400); attach idempotent; detach always 204.
- [ ] `GET /api/boards/{id}` nests the palette + per-card `labelIds`; `GET /api/cards/{id}` returns `boardId` + attached labels; older API tests still green.
- [ ] From the browser: create a label on a card (auto-attached), attach/detach, rename/recolour, delete (with confirm) — persisted across a restart, chips visible on the board front and the task view.
- [ ] Soft-tint chips from the curated palette, **each ≥ 4.5:1 as chip text**; names escaped and always shown; non-modal picker — keyboard-operable, named controls, focus return, `aria-expanded`/`aria-haspopup`, count-free `aria-live` announcements.
- [ ] `dotnet test` green (94), `dotnet build` clean (0 warnings); tests never touch the real DB.
- [ ] README Status / roadmap / specs + plans links updated.
