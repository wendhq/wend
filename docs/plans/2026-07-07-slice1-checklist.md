# Per-Card Checklist (+ Settings Surface) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **For Malin & Henry (the actual build mode):** coached turn-based — Malin drives odd tasks, Henry drives even tasks. Per-human commits, no AI attribution. Driver pushes at end of turn (`git push`); next driver starts with `git fetch && git switch feature/checklist`. After every task, check the **running test count** against the plan's total for that task.

**Goal:** One simple checklist per card — add, rename, check into a collapsible Done strip, reorder, delete with toast undo — plus checklist progress on board chips, a task-view Edit mode, and a localStorage settings screen (card Done checkboxes and the Delete card button become opt-in).

**Architecture:** `ChecklistItem` is a miniature `Card` (same position algebra, `CheckedAt`/`DeletedAt` timestamps, EF query filter, soft-delete + idempotent restore) behind a new `IChecklistItemRepository` seam, exposed through endpoints that each mirror an existing one. The frontend follows the house MVC shapes: a pure `checklist.js` render helper composed by the card view (the `labels.js` pattern), a `js/settings/` trio over a tiny `prefs.js`, and coordinator-wired toast undo (the Plan 7 pattern).

**Tech Stack:** ASP.NET Core net10 minimal API · EF Core + SQLite · NUnit 4 + Mvc.Testing · vanilla-JS MVC (no build step)

**Spec:** [`docs/2026-07-07-wend-checklist-design.md`](../2026-07-07-wend-checklist-design.md)

**Test count checkpoints:** start 124 → T1 127 → T2 131 → T3 135 → T4 138 → T5 142 → T6 147 → (frontend tasks add none) → final **147 green, 0 warnings**.

---

## Before Task 1 — branch + docs commit (Malin)

- [ ] **Create the branch and commit the docs**

```powershell
git switch main
git pull
git switch -c feature/checklist
git add docs/2026-07-07-wend-checklist-design.md docs/plans/2026-07-07-slice1-checklist.md
git commit -m "Checklist docs — design spec + build plan"
git push -u origin feature/checklist
```

**Reminder for every manual run in this plan:** stop any running `Wend.exe` / `dotnet run` before `dotnet build` or `dotnet test` (file lock), and the FIRST manual app run after Task 6 needs a one-time database reset on EACH machine (`EnsureCreated` cannot add the new table to a live db):

```powershell
Remove-Item "$env:LOCALAPPDATA\Wend\data.db"
```

---

### Task 1: `ChecklistItem` entity + DbContext wiring — *Malin*

**Files:**
- Create: `Wend.Core/ChecklistItem.cs`
- Modify: `Wend.Core/Card.cs` (add nav property)
- Modify: `Wend.Core/WendDbContext.cs` (DbSet + query filter)
- Test: `Wend.Tests/ChecklistItemRepositoryTests.cs` (new file)

- [ ] **Step 1: Write the three failing schema tests**

Create `Wend.Tests/ChecklistItemRepositoryTests.cs`. (No repository field yet — Task 2 adds it. **Do not delete these three tests when Task 2 extends this file** — that's how Plan 3 silently lost its cascade coverage.)

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class ChecklistItemRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;
    private EfBoardRepository _boards = null!;
    private EfListRepository _lists = null!;
    private EfCardRepository _cards = null!;

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

    // Adds a board + list + card directly, returning the card id, so item tests have a parent.
    private async Task<int> NewCardAsync()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        return card.Id;
    }

    [Test]
    public async Task Saved_item_belongs_to_its_card_and_keeps_its_position()
    {
        var cardId = await NewCardAsync();

        _db.ChecklistItems.Add(new ChecklistItem { CardId = cardId, Text = "Write intro", Position = 0 });
        await _db.SaveChangesAsync();

        var item = await _db.ChecklistItems.SingleAsync();
        Assert.That(item.Id, Is.GreaterThan(0));
        Assert.That(item.CardId, Is.EqualTo(cardId));
        Assert.That(item.Text, Is.EqualTo("Write intro"));
        Assert.That(item.Position, Is.EqualTo(0));
        Assert.That(item.CheckedAt, Is.Null);
    }

    [Test]
    public async Task Deleting_a_card_row_cascades_to_its_items()
    {
        var cardId = await NewCardAsync();
        _db.ChecklistItems.Add(new ChecklistItem { CardId = cardId, Text = "Item", Position = 0 });
        await _db.SaveChangesAsync();

        var card = await _db.Cards.SingleAsync(c => c.Id == cardId);
        _db.Cards.Remove(card); // hard delete (the future Trash empty) — DB-level cascade
        await _db.SaveChangesAsync();

        Assert.That(await _db.ChecklistItems.IgnoreQueryFilters().AnyAsync(), Is.False);
    }

    [Test]
    public async Task Deleted_items_are_hidden_from_queries()
    {
        var cardId = await NewCardAsync();
        _db.ChecklistItems.Add(new ChecklistItem { CardId = cardId, Text = "Visible", Position = 0 });
        _db.ChecklistItems.Add(new ChecklistItem { CardId = cardId, Text = "Gone", Position = 1, DeletedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var texts = await _db.ChecklistItems.Select(i => i.Text).ToListAsync();
        Assert.That(texts, Is.EqualTo(new[] { "Visible" }));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ChecklistItem"`
Expected: **build FAILS** — `CS0246: The type or namespace name 'ChecklistItem' could not be found` (the entity doesn't exist yet).

- [ ] **Step 3: Create the entity**

Create `Wend.Core/ChecklistItem.cs`:

```csharp
namespace Wend.Core;

/// <summary>One entry in a card's checklist — a miniature Card: the same 0-based gapless
/// Position algebra and lifecycle idioms (CheckedAt mirrors Card.CompletedAt, DeletedAt
/// mirrors the soft delete). Checked and unchecked items share ONE position sequence, so
/// un-checking an item drops it back exactly where it lived.</summary>
public class ChecklistItem
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public string Text { get; set; } = "";
    public int Position { get; set; }
    public DateTime? CheckedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

In `Wend.Core/Card.cs`, add the navigation property after the `DeletedAt` line (before the closing brace):

```csharp
    // A card's checklist items. Required FK on ChecklistItem.CardId → deleting a card cascades to them.
    public ICollection<ChecklistItem> ChecklistItems { get; set; } = [];
```

In `Wend.Core/WendDbContext.cs`, add the DbSet after the `CardLabels` line:

```csharp
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
```

…and this filter at the end of `OnModelCreating` (items carry their own filter so the required
relationship to the filtered `Card` principal doesn't warn):

```csharp
        // Hide soft-deleted checklist items from every query — mirrors the Card filter.
        modelBuilder.Entity<ChecklistItem>().HasQueryFilter(i => i.DeletedAt == null);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test`
Expected: PASS — **127 tests**, 0 warnings.

- [ ] **Step 5: Commit and hand over**

```powershell
git add Wend.Core/ChecklistItem.cs Wend.Core/Card.cs Wend.Core/WendDbContext.cs Wend.Tests/ChecklistItemRepositoryTests.cs
git commit -m "Checklist Task 1 — ChecklistItem entity, context wiring, schema tests"
git push
```

Henry: `git fetch && git switch feature/checklist && git pull`

---

### Task 2: Repository — add + rename — *Henry*

**Files:**
- Create: `Wend.Core/IChecklistItemRepository.cs`
- Create: `Wend.Core/EfChecklistItemRepository.cs`
- Modify: `Wend.Tests/ChecklistItemRepositoryTests.cs` (add repo field + 4 tests — keep Task 1's three tests)

- [ ] **Step 1: Add the repository field and failing tests**

In `ChecklistItemRepositoryTests.cs`, add a field beneath `_cards`:

```csharp
    private EfChecklistItemRepository _repo = null!;
```

…and this line at the end of `SetUp()`:

```csharp
        _repo = new EfChecklistItemRepository(_db);
```

Append these four tests before the closing brace:

```csharp
    [Test]
    public async Task Add_appends_each_item_at_the_next_position()
    {
        var cardId = await NewCardAsync();

        var first = await _repo.AddItemAsync(cardId, "First");
        var second = await _repo.AddItemAsync(cardId, "Second");

        Assert.That(first.Position, Is.EqualTo(0));
        Assert.That(second.Position, Is.EqualTo(1));
    }

    [Test]
    public async Task Positions_count_from_zero_per_card()
    {
        var cardA = await NewCardAsync();
        var cardB = await NewCardAsync();

        var a1 = await _repo.AddItemAsync(cardA, "A1");
        var b1 = await _repo.AddItemAsync(cardB, "B1");

        Assert.That(a1.Position, Is.EqualTo(0));
        Assert.That(b1.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Get_items_for_card_returns_them_in_position_order()
    {
        var cardId = await NewCardAsync();
        await _repo.AddItemAsync(cardId, "First");
        await _repo.AddItemAsync(cardId, "Second");

        var items = await _repo.GetItemsForCardAsync(cardId);

        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Rename_updates_the_text_and_reports_missing()
    {
        var cardId = await NewCardAsync();
        var item = await _repo.AddItemAsync(cardId, "Old");

        Assert.That(await _repo.RenameItemAsync(item.Id, "New"), Is.True);
        Assert.That((await _repo.GetItemsForCardAsync(cardId)).Single().Text, Is.EqualTo("New"));

        Assert.That(await _repo.RenameItemAsync(9999, "X"), Is.False);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ChecklistItem"`
Expected: **build FAILS** — `CS0246: 'EfChecklistItemRepository' could not be found`.

- [ ] **Step 3: Create the seam and the implementation**

Create `Wend.Core/IChecklistItemRepository.cs` (grows in Tasks 3, 4, and 6 — one method group per task, so nothing is stubbed):

```csharp
namespace Wend.Core;

/// <summary>
/// Persistence seam for a card's checklist items. Position is a 0-based contiguous index
/// shared by checked AND unchecked items; the repository keeps it gapless on create,
/// delete, and move — the same algebra as cards within a list.
/// </summary>
public interface IChecklistItemRepository
{
    Task<IReadOnlyList<ChecklistItem>> GetItemsForCardAsync(int cardId);
    Task<ChecklistItem> AddItemAsync(int cardId, string text);
    Task<bool> RenameItemAsync(int id, string text);
}
```

Create `Wend.Core/EfChecklistItemRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfChecklistItemRepository(WendDbContext db) : IChecklistItemRepository
{
    public async Task<IReadOnlyList<ChecklistItem>> GetItemsForCardAsync(int cardId) =>
        await db.ChecklistItems.Where(i => i.CardId == cardId)
            .OrderBy(i => i.Position)
            .ToListAsync();

    public async Task<ChecklistItem> AddItemAsync(int cardId, string text)
    {
        // Append: the next position is the current item count for this card.
        var position = await db.ChecklistItems.CountAsync(i => i.CardId == cardId);
        var item = new ChecklistItem { CardId = cardId, Text = text, Position = position };
        db.ChecklistItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<bool> RenameItemAsync(int id, string text)
    {
        var item = await db.ChecklistItems.FindAsync(id);
        if (item is null) return false;
        item.Text = text;
        await db.SaveChangesAsync();
        return true;
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test`
Expected: PASS — **131 tests**, 0 warnings.

- [ ] **Step 5: Commit and hand over**

```powershell
git add Wend.Core/IChecklistItemRepository.cs Wend.Core/EfChecklistItemRepository.cs Wend.Tests/ChecklistItemRepositoryTests.cs
git commit -m "Checklist Task 2 — repository seam: add + rename"
git push
```

Malin: `git fetch && git switch feature/checklist && git pull`

---

### Task 3: Repository — check + move — *Malin*

**Files:**
- Modify: `Wend.Core/IChecklistItemRepository.cs`
- Modify: `Wend.Core/EfChecklistItemRepository.cs`
- Test: `Wend.Tests/ChecklistItemRepositoryTests.cs` (+4 tests)

- [ ] **Step 1: Write the failing tests**

Append to `ChecklistItemRepositoryTests.cs`:

```csharp
    [Test]
    public async Task Set_checked_stamps_checkedAt_and_clears_it()
    {
        var cardId = await NewCardAsync();
        var item = await _repo.AddItemAsync(cardId, "Do it");

        Assert.That(await _repo.SetCheckedAsync(item.Id, true), Is.True);
        Assert.That((await _repo.GetItemsForCardAsync(cardId)).Single().CheckedAt, Is.Not.Null);

        Assert.That(await _repo.SetCheckedAsync(item.Id, false), Is.True);
        Assert.That((await _repo.GetItemsForCardAsync(cardId)).Single().CheckedAt, Is.Null);
    }

    [Test]
    public async Task Set_checked_reports_a_missing_item()
    {
        Assert.That(await _repo.SetCheckedAsync(9999, true), Is.False);
    }

    [Test]
    public async Task Move_reorders_within_the_card_and_clamps_an_overshoot()
    {
        var cardId = await NewCardAsync();
        var a = await _repo.AddItemAsync(cardId, "A");   // 0
        await _repo.AddItemAsync(cardId, "B");           // 1
        var c = await _repo.AddItemAsync(cardId, "C");   // 2

        Assert.That(await _repo.MoveItemAsync(c.Id, 0), Is.True);
        var items = await _repo.GetItemsForCardAsync(cardId);
        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "C", "A", "B" }));
        Assert.That(items.Select(i => i.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless

        // Position 99 overshoots — it should clamp to the bottom.
        Assert.That(await _repo.MoveItemAsync(a.Id, 99), Is.True);
        items = await _repo.GetItemsForCardAsync(cardId);
        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "C", "B", "A" }));
    }

    [Test]
    public async Task Move_reports_a_missing_item()
    {
        Assert.That(await _repo.MoveItemAsync(9999, 0), Is.False);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ChecklistItem"`
Expected: **build FAILS** — `CS1061: 'EfChecklistItemRepository' does not contain a definition for 'SetCheckedAsync'`.

- [ ] **Step 3: Implement**

Add to `IChecklistItemRepository`:

```csharp
    Task<bool> SetCheckedAsync(int id, bool isChecked);
    Task<bool> MoveItemAsync(int id, int position);
```

Add to `EfChecklistItemRepository` (NB: the parameter is `isChecked` — `checked` is a C# keyword):

```csharp
    public async Task<bool> SetCheckedAsync(int id, bool isChecked)
    {
        var item = await db.ChecklistItems.FindAsync(id);
        if (item is null) return false;
        item.CheckedAt = isChecked ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MoveItemAsync(int id, int position)
    {
        var item = await db.ChecklistItems.FindAsync(id);
        if (item is null) return false;

        // Pull the card's items in order, lift this one out, drop it back at the clamped
        // target index, then renumber so positions stay gapless — MoveListAsync's algorithm.
        var siblings = await db.ChecklistItems.Where(i => i.CardId == item.CardId)
            .OrderBy(i => i.Position)
            .ToListAsync();
        siblings.Remove(siblings.First(i => i.Id == id));
        var target = Math.Clamp(position, 0, siblings.Count);
        siblings.Insert(target, item);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test`
Expected: PASS — **135 tests**, 0 warnings.

- [ ] **Step 5: Commit and hand over**

```powershell
git add Wend.Core/IChecklistItemRepository.cs Wend.Core/EfChecklistItemRepository.cs Wend.Tests/ChecklistItemRepositoryTests.cs
git commit -m "Checklist Task 3 — repository: check + move"
git push
```

Henry: `git fetch && git switch feature/checklist && git pull`

---

### Task 4: Repository — soft-delete + restore — *Henry*

**Files:**
- Modify: `Wend.Core/IChecklistItemRepository.cs`
- Modify: `Wend.Core/EfChecklistItemRepository.cs`
- Test: `Wend.Tests/ChecklistItemRepositoryTests.cs` (+3 tests)

- [ ] **Step 1: Write the failing tests**

Append to `ChecklistItemRepositoryTests.cs`:

```csharp
    [Test]
    public async Task Delete_soft_deletes_and_resequences_the_rest()
    {
        var cardId = await NewCardAsync();
        await _repo.AddItemAsync(cardId, "A");           // 0
        var b = await _repo.AddItemAsync(cardId, "B");   // 1
        await _repo.AddItemAsync(cardId, "C");           // 2

        Assert.That(await _repo.DeleteItemAsync(b.Id), Is.True);

        // Hidden from normal queries and the survivors close the gap…
        var items = await _repo.GetItemsForCardAsync(cardId);
        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(items.Select(i => i.Position), Is.EqualTo(new[] { 0, 1 }));
        // …but the row still exists with DeletedAt set, so undo can bring it back.
        var row = await _db.ChecklistItems.IgnoreQueryFilters().SingleAsync(i => i.Id == b.Id);
        Assert.That(row.DeletedAt, Is.Not.Null);
    }

    [Test]
    public async Task Restore_brings_an_item_back_to_its_original_position()
    {
        var cardId = await NewCardAsync();
        await _repo.AddItemAsync(cardId, "A");           // 0
        var b = await _repo.AddItemAsync(cardId, "B");   // 1
        await _repo.AddItemAsync(cardId, "C");           // 2

        await _repo.DeleteItemAsync(b.Id);               // survivors resequence to A(0), C(1)
        Assert.That(await _repo.RestoreItemAsync(b.Id), Is.True);

        var items = await _repo.GetItemsForCardAsync(cardId);
        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "A", "B", "C" }));
        Assert.That(items.Select(i => i.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless, B back in the middle
    }

    [Test]
    public async Task Restore_works_from_a_fresh_context_not_only_a_tracked_one()
    {
        var cardId = await NewCardAsync();
        var item = await _repo.AddItemAsync(cardId, "Temp");
        await _repo.DeleteItemAsync(item.Id);

        _db.ChangeTracker.Clear(); // force a DB read, as a new HTTP request would — no tracked entity

        Assert.That(await _repo.RestoreItemAsync(item.Id), Is.True);
        Assert.That((await _repo.GetItemsForCardAsync(cardId)).Select(i => i.Text), Is.EqualTo(new[] { "Temp" }));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ChecklistItem"`
Expected: **build FAILS** — `CS1061: … does not contain a definition for 'DeleteItemAsync'`.

- [ ] **Step 3: Implement**

Add to `IChecklistItemRepository`:

```csharp
    Task<bool> DeleteItemAsync(int id);
    Task<bool> RestoreItemAsync(int id);
```

Add to `EfChecklistItemRepository` — a faithful mirror of the card versions, including the Plan 7
lesson (`IgnoreQueryFilters`, never `FindAsync`, in the restore path) and idempotent restore:

```csharp
    public async Task<bool> DeleteItemAsync(int id)
    {
        var item = await db.ChecklistItems.FindAsync(id);
        if (item is null || item.DeletedAt is not null) return false; // missing or already gone
        item.DeletedAt = DateTime.UtcNow;   // soft delete — the row survives for undo
        await db.SaveChangesAsync();
        await ResequenceAsync(item.CardId); // close the gap among the survivors (filter hides this item)
        return true;
    }

    public async Task<bool> RestoreItemAsync(int id)
    {
        // IgnoreQueryFilters so the soft-deleted row is found from ANY context. FindAsync only
        // returns it while it's still tracked in the same context — the API's per-request
        // contexts read from the DB, where the filter hides it (Plan 7's restore-404 bug).
        var item = await db.ChecklistItems.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return false;
        if (item.DeletedAt is null) return true;   // already active — idempotent no-op

        var siblings = await db.ChecklistItems.Where(i => i.CardId == item.CardId)
            .OrderBy(i => i.Position)
            .ToListAsync();                        // active siblings only (the item is still filtered out)
        item.DeletedAt = null;
        var index = Math.Clamp(item.Position, 0, siblings.Count); // its old spot, bounded to the list today
        siblings.Insert(index, item);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }

    // Rewrites a card's item positions to a gapless 0-based sequence in current order.
    private async Task ResequenceAsync(int cardId)
    {
        var items = await db.ChecklistItems.Where(i => i.CardId == cardId)
            .OrderBy(i => i.Position)
            .ToListAsync();
        for (var i = 0; i < items.Count; i++) items[i].Position = i;
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test`
Expected: PASS — **138 tests**, 0 warnings.

- [ ] **Step 5: Commit and hand over**

```powershell
git add Wend.Core/IChecklistItemRepository.cs Wend.Core/EfChecklistItemRepository.cs Wend.Tests/ChecklistItemRepositoryTests.cs
git commit -m "Checklist Task 4 — repository: soft-delete + restore"
git push
```

Malin: `git fetch && git switch feature/checklist && git pull`

---

### Task 5: API — create + rename + items nested in card detail — *Malin*

**Files:**
- Create: `Wend.Api/ChecklistItemEndpoints.cs`
- Modify: `Wend.Api/CardEndpoints.cs` (detail gains `Items`)
- Modify: `Wend.Api/Program.cs` (DI + mapping)
- Test: `Wend.Tests/ChecklistItemApiTests.cs` (new file, 4 tests)

- [ ] **Step 1: Write the failing API tests**

Create `Wend.Tests/ChecklistItemApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class ChecklistItemApiTests
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
    private record ItemDto(int Id, string Text, DateTime? CheckedAt, int Position);
    private record CardDetailDto(int Id, string Title, List<ItemDto> Items);
    private record CardSummaryDto(int Id, string Title, int ChecklistDone, int ChecklistTotal);
    private record ListWithCardsDto(int Id, string Title, List<CardSummaryDto> Cards);
    private record BoardWithCardsDto(int Id, string Title, List<ListWithCardsDto> Lists);

    private async Task<BoardDto> CreateBoardAsync(string title)
    {
        var res = await _client.PostAsJsonAsync("/api/boards", new { title });
        return (await res.Content.ReadFromJsonAsync<BoardDto>())!;
    }

    private async Task<int> NewCardAsync()
    {
        var board = await CreateBoardAsync("Board");
        var listRes = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/lists", new { title = "List" });
        var list = (await listRes.Content.ReadFromJsonAsync<ListDto>())!;
        var cardRes = await _client.PostAsJsonAsync($"/api/lists/{list.Id}/cards", new { title = "Card" });
        return (await cardRes.Content.ReadFromJsonAsync<CardDto>())!.Id;
    }

    private async Task<ItemDto> AddItemAsync(int cardId, string text)
    {
        var res = await _client.PostAsJsonAsync($"/api/cards/{cardId}/checklist-items", new { text });
        return (await res.Content.ReadFromJsonAsync<ItemDto>())!;
    }

    [Test]
    public async Task Posting_an_item_creates_it_at_the_next_position()
    {
        var cardId = await NewCardAsync();

        var response = await _client.PostAsJsonAsync($"/api/cards/{cardId}/checklist-items", new { text = "Write intro" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = (await response.Content.ReadFromJsonAsync<ItemDto>())!;
        Assert.That(created.Text, Is.EqualTo("Write intro"));
        Assert.That(created.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Posting_a_blank_item_text_is_rejected()
    {
        var cardId = await NewCardAsync();
        var response = await _client.PostAsJsonAsync($"/api/cards/{cardId}/checklist-items", new { text = "   " });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_item_to_a_missing_card_is_404()
    {
        var response = await _client.PostAsJsonAsync("/api/cards/9999/checklist-items", new { text = "X" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Card_detail_nests_items_in_order_and_rename_shows_there()
    {
        var cardId = await NewCardAsync();
        var first = await AddItemAsync(cardId, "First");
        await AddItemAsync(cardId, "Second");

        var rename = await _client.PutAsJsonAsync($"/api/checklist-items/{first.Id}", new { text = "Renamed" });
        Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = (await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{cardId}"))!;
        Assert.That(detail.Items.Select(i => i.Text), Is.EqualTo(new[] { "Renamed", "Second" }));
        Assert.That(detail.Items.Select(i => i.Position), Is.EqualTo(new[] { 0, 1 }));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ChecklistItemApi"`
Expected: PASS build, **4 FAIL** — `Posting_…` tests get 404 (route doesn't exist), `Card_detail_…` fails deserialising `Items` (property missing → `Items` is null → `NullReferenceException` or empty).

- [ ] **Step 3: Implement the endpoints**

Create `Wend.Api/ChecklistItemEndpoints.cs`:

```csharp
using Wend.Core;

namespace Wend.Api;

public static class ChecklistItemEndpoints
{
    private const int MaxTextLength = 200;

    public static IEndpointRouteBuilder MapChecklistItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/cards/{cardId:int}/checklist-items",
            async (int cardId, CreateChecklistItemRequest req, ICardRepository cards, IChecklistItemRepository items) =>
            {
                var text = req.Text?.Trim() ?? "";
                if (text.Length is 0 or > MaxTextLength) return Results.BadRequest();
                if (await cards.GetCardAsync(cardId) is null) return Results.NotFound();
                var item = await items.AddItemAsync(cardId, text);
                return Results.Created($"/api/checklist-items/{item.Id}", item);
            });

        app.MapPut("/api/checklist-items/{id:int}",
            async (int id, RenameChecklistItemRequest req, IChecklistItemRepository items) =>
            {
                var text = req.Text?.Trim() ?? "";
                if (text.Length is 0 or > MaxTextLength) return Results.BadRequest();
                return await items.RenameItemAsync(id, text) ? Results.NoContent() : Results.NotFound();
            });

        return app;
    }
}

public record CreateChecklistItemRequest(string Text);
public record RenameChecklistItemRequest(string Text);
public record ChecklistItemDto(int Id, string Text, DateTime? CheckedAt, int Position);
```

In `Wend.Api/CardEndpoints.cs`, replace the `GET /api/cards/{id}` handler with this version
(injects the checklist repo, maps items into the detail):

```csharp
        app.MapGet("/api/cards/{id:int}", async (int id, ICardRepository cards, IListRepository lists,
            ILabelRepository labels, IChecklistItemRepository checklist) =>
        {
            if (await cards.GetCardAsync(id) is not { } c) return Results.NotFound();
            var list = await lists.GetListAsync(c.ListId);
            var attached = (await labels.GetCardLabelsAsync(c.Id))
                .Select(l => new LabelDto(l.Id, l.Name, l.Colour)).ToList();
            var items = (await checklist.GetItemsForCardAsync(c.Id))
                .Select(i => new ChecklistItemDto(i.Id, i.Text, i.CheckedAt, i.Position)).ToList();
            return Results.Ok(new CardDetail(c.Id, c.ListId, list?.Title ?? "", list?.BoardId ?? 0,
                c.Title, c.Description, c.DueDate, c.Position, c.CompletedAt, attached, items));
        });
```

…and extend the `CardDetail` record at the bottom of the file:

```csharp
public record CardDetail(int Id, int ListId, string ListTitle, int BoardId, string Title, string? Description, DateOnly? DueDate, int Position, DateTime? CompletedAt, IReadOnlyList<LabelDto> Labels, IReadOnlyList<ChecklistItemDto> Items);
```

In `Wend.Api/Program.cs`, register the repository after the `ILabelRepository` line:

```csharp
builder.Services.AddScoped<IChecklistItemRepository, EfChecklistItemRepository>();
```

…and map the endpoints after `app.MapLabelEndpoints();`:

```csharp
app.MapChecklistItemEndpoints();
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test`
Expected: PASS — **142 tests**, 0 warnings.

- [ ] **Step 5: Commit and hand over**

```powershell
git add Wend.Api/ChecklistItemEndpoints.cs Wend.Api/CardEndpoints.cs Wend.Api/Program.cs Wend.Tests/ChecklistItemApiTests.cs
git commit -m "Checklist Task 5 — API: create + rename, items in card detail"
git push
```

Henry: `git fetch && git switch feature/checklist && git pull`

---

### Task 6: API — check + move + delete + restore + board counts — *Henry*

**Files:**
- Modify: `Wend.Api/ChecklistItemEndpoints.cs`
- Modify: `Wend.Core/IChecklistItemRepository.cs` + `Wend.Core/EfChecklistItemRepository.cs` (counts query)
- Modify: `Wend.Api/BoardEndpoints.cs` (`CardSummary` gains counts)
- Test: `Wend.Tests/ChecklistItemApiTests.cs` (+5 tests)

- [ ] **Step 1: Write the failing tests**

Append to `ChecklistItemApiTests.cs`:

```csharp
    [Test]
    public async Task Checking_an_item_stamps_and_clears_its_checkedAt()
    {
        var cardId = await NewCardAsync();
        var item = await AddItemAsync(cardId, "Do it");

        var check = await _client.PutAsJsonAsync($"/api/checklist-items/{item.Id}/check", new { @checked = true });
        Assert.That(check.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var detail = (await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{cardId}"))!;
        Assert.That(detail.Items.Single().CheckedAt, Is.Not.Null);

        await _client.PutAsJsonAsync($"/api/checklist-items/{item.Id}/check", new { @checked = false });
        detail = (await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{cardId}"))!;
        Assert.That(detail.Items.Single().CheckedAt, Is.Null);
    }

    [Test]
    public async Task Moving_an_item_reorders_it()
    {
        var cardId = await NewCardAsync();
        await AddItemAsync(cardId, "A");                 // 0
        await AddItemAsync(cardId, "B");                 // 1
        var c = await AddItemAsync(cardId, "C");         // 2

        var move = await _client.PutAsJsonAsync($"/api/checklist-items/{c.Id}/move", new { position = 0 });
        Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = (await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{cardId}"))!;
        Assert.That(detail.Items.Select(i => i.Text), Is.EqualTo(new[] { "C", "A", "B" }));
    }

    [Test]
    public async Task Deleting_then_restoring_an_item_brings_it_back_in_place()
    {
        var cardId = await NewCardAsync();
        await AddItemAsync(cardId, "A");                 // 0
        var b = await AddItemAsync(cardId, "B");         // 1
        await AddItemAsync(cardId, "C");                 // 2

        var del = await _client.DeleteAsync($"/api/checklist-items/{b.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var detail = (await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{cardId}"))!;
        Assert.That(detail.Items.Select(i => i.Text), Is.EqualTo(new[] { "A", "C" }));

        var restore = await _client.PostAsync($"/api/checklist-items/{b.Id}/restore", null);
        Assert.That(restore.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        detail = (await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{cardId}"))!;
        Assert.That(detail.Items.Select(i => i.Text), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public async Task Missing_item_endpoints_are_404()
    {
        Assert.That((await _client.PutAsJsonAsync("/api/checklist-items/9999", new { text = "X" })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _client.PutAsJsonAsync("/api/checklist-items/9999/check", new { @checked = true })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _client.PutAsJsonAsync("/api/checklist-items/9999/move", new { position = 0 })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _client.DeleteAsync("/api/checklist-items/9999")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _client.PostAsync("/api/checklist-items/9999/restore", null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Board_nest_exposes_checklist_counts()
    {
        var board = await CreateBoardAsync("Sprint");
        var listRes = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/lists", new { title = "To do" });
        var list = (await listRes.Content.ReadFromJsonAsync<ListDto>())!;
        var cardRes = await _client.PostAsJsonAsync($"/api/lists/{list.Id}/cards", new { title = "With items" });
        var cardId = (await cardRes.Content.ReadFromJsonAsync<CardDto>())!.Id;
        await _client.PostAsJsonAsync($"/api/lists/{list.Id}/cards", new { title = "Without items" });

        var item = await AddItemAsync(cardId, "One");
        await AddItemAsync(cardId, "Two");
        await _client.PutAsJsonAsync($"/api/checklist-items/{item.Id}/check", new { @checked = true });

        var detail = (await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}"))!;
        var cards = detail.Lists.Single().Cards;
        Assert.That(cards.Single(c => c.Title == "With items").ChecklistDone, Is.EqualTo(1));
        Assert.That(cards.Single(c => c.Title == "With items").ChecklistTotal, Is.EqualTo(2));
        Assert.That(cards.Single(c => c.Title == "Without items").ChecklistTotal, Is.EqualTo(0));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ChecklistItemApi"`
Expected: **5 FAIL** — check/move/delete/restore routes 404; counts test fails on `ChecklistDone` = 0.

- [ ] **Step 3: Implement**

In `Wend.Api/ChecklistItemEndpoints.cs`, add before `return app;`:

```csharp
        app.MapPut("/api/checklist-items/{id:int}/check",
            async (int id, CheckChecklistItemRequest req, IChecklistItemRepository items) =>
                await items.SetCheckedAsync(id, req.Checked) ? Results.NoContent() : Results.NotFound());

        app.MapPut("/api/checklist-items/{id:int}/move",
            async (int id, MoveChecklistItemRequest req, IChecklistItemRepository items) =>
                await items.MoveItemAsync(id, req.Position) ? Results.NoContent() : Results.NotFound());

        app.MapDelete("/api/checklist-items/{id:int}", async (int id, IChecklistItemRepository items) =>
            await items.DeleteItemAsync(id) ? Results.NoContent() : Results.NotFound());

        app.MapPost("/api/checklist-items/{id:int}/restore", async (int id, IChecklistItemRepository items) =>
            await items.RestoreItemAsync(id) ? Results.NoContent() : Results.NotFound());
```

…and these records at the bottom:

```csharp
public record CheckChecklistItemRequest(bool Checked);
public record MoveChecklistItemRequest(int Position);
```

Add the counts query to `IChecklistItemRepository`:

```csharp
    Task<IReadOnlyDictionary<int, ChecklistCounts>> GetCountsByCardAsync(int boardId);
```

…a record in `Wend.Core/IChecklistItemRepository.cs` (below the interface):

```csharp
public record ChecklistCounts(int Done, int Total);
```

…and the implementation in `EfChecklistItemRepository` (query filters keep deleted items and
deleted cards out automatically):

```csharp
    public async Task<IReadOnlyDictionary<int, ChecklistCounts>> GetCountsByCardAsync(int boardId)
    {
        var rows = await (
            from i in db.ChecklistItems
            join c in db.Cards on i.CardId equals c.Id
            join l in db.Lists on c.ListId equals l.Id
            where l.BoardId == boardId
            group i by i.CardId into g
            select new { CardId = g.Key, Done = g.Count(x => x.CheckedAt != null), Total = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(r => r.CardId, r => new ChecklistCounts(r.Done, r.Total));
    }
```

In `Wend.Api/BoardEndpoints.cs`, replace the `GET /{id}` handler's signature and card mapping:

```csharp
        group.MapGet("/{id:int}", async (int id, IBoardRepository boards, IListRepository lists,
            ICardRepository cards, ILabelRepository labels, IChecklistItemRepository checklist) =>
        {
            if (await boards.GetBoardAsync(id) is not { } board) return Results.NotFound();

            var palette = (await labels.GetBoardLabelsAsync(id))
                .Select(l => new LabelDto(l.Id, l.Name, l.Colour)).ToList();
            var labelIdsByCard = await labels.GetLabelIdsByCardAsync(id);
            var counts = await checklist.GetCountsByCardAsync(id);

            var summaries = new List<ListSummary>();
            foreach (var l in await lists.GetListsForBoardAsync(id))
            {
                var cardSummaries = (await cards.GetCardsForListAsync(l.Id))
                    .Select(c => new CardSummary(c.Id, c.Title, c.DueDate, c.Position, c.CompletedAt,
                        labelIdsByCard.TryGetValue(c.Id, out var ids) ? ids : new List<int>(),
                        counts.TryGetValue(c.Id, out var k) ? k.Done : 0,
                        counts.TryGetValue(c.Id, out var t) ? t.Total : 0))
                    .ToList();
                summaries.Add(new ListSummary(l.Id, l.Title, l.Position, cardSummaries));
            }
            return Results.Ok(new BoardDetail(board.Id, board.Title, palette, summaries));
        });
```

…and extend the `CardSummary` record at the bottom:

```csharp
public record CardSummary(int Id, string Title, DateOnly? DueDate, int Position, DateTime? CompletedAt, IReadOnlyList<int> LabelIds, int ChecklistDone, int ChecklistTotal);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test`
Expected: PASS — **147 tests**, 0 warnings. **Backend complete.**

- [ ] **Step 5: Commit and hand over**

```powershell
git add Wend.Api/ChecklistItemEndpoints.cs Wend.Api/BoardEndpoints.cs Wend.Core/IChecklistItemRepository.cs Wend.Core/EfChecklistItemRepository.cs Wend.Tests/ChecklistItemApiTests.cs
git commit -m "Checklist Task 6 — API: check + move + delete + restore, board counts"
git push
```

Malin: `git fetch && git switch feature/checklist && git pull`

---

### Task 7: Frontend — prefs + Settings screen + header entry — *Malin*

**Files:**
- Create: `Wend.Api/wwwroot/js/prefs.js`
- Create: `Wend.Api/wwwroot/js/settings/model.js`, `…/settings/view.js`, `…/settings/controller.js`
- Modify: `Wend.Api/wwwroot/index.html` (header button)
- Modify: `Wend.Api/wwwroot/js/main.js` (imports + `showSettings` + listener)
- Modify: `Wend.Api/wwwroot/css/app.css` (header layout + settings styles)

- [ ] **Step 1: Create `js/prefs.js`**

```js
// Client-only preferences in localStorage. Reads pick the known keys explicitly with type
// checks — never spread parsed JSON (localStorage is hand-editable). Parse failure → defaults.
const KEY = "wend.prefs";

export function getPrefs() {
  let parsed = null;
  try {
    parsed = JSON.parse(localStorage.getItem(KEY) ?? "null");
  } catch {
    // corrupted value → fall through to defaults
  }
  return {
    showCardDone: parsed?.showCardDone === true,
    alwaysShowDeleteCard: parsed?.alwaysShowDeleteCard === true,
  };
}

export function setPref(key, value) {
  const prefs = getPrefs();
  if (!(key in prefs)) return; // unknown key → ignore
  localStorage.setItem(KEY, JSON.stringify({ ...prefs, [key]: value === true }));
}
```

- [ ] **Step 2: Create the settings trio**

`js/settings/model.js`:

```js
import { getPrefs, setPref } from "../prefs.js";

// Wraps the stored prefs in the house subscribe/notify shape. Synchronous — localStorage.
export function createSettingsModel() {
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(getPrefs()));
  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(getPrefs());
    },
    set(key, value) {
      setPref(key, value);
      notify();
    },
  };
}
```

`js/settings/view.js`:

```js
// Renders the Settings screen: back link, heading, two labelled native toggles.
// No logic; events via data-action.
export function createSettingsView(root) {
  let h = {};

  function render(prefs) {
    root.innerHTML = `
      <div class="settings-view">
        <button class="back-link" data-action="back">← Boards</button>
        <h2 class="settings-heading" tabindex="-1">Settings</h2>
        <label class="setting-row">
          <input type="checkbox" data-action="toggle-pref" data-pref="showCardDone"
            ${prefs.showCardDone ? "checked" : ""} />
          <span>Show card Done checkboxes</span>
        </label>
        <p class="setting-hint">Adds a done checkbox to every card, so cards can be tucked into the board's Done area.</p>
        <label class="setting-row">
          <input type="checkbox" data-action="toggle-pref" data-pref="alwaysShowDeleteCard"
            ${prefs.alwaysShowDeleteCard ? "checked" : ""} />
          <span>Always show the Delete card button</span>
        </label>
        <p class="setting-hint">Otherwise Delete card only appears in a card's Edit mode.</p>
      </div>`;
  }

  function focusHeading() { root.querySelector(".settings-heading")?.focus(); }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-action="back"]')) h.back();
    });
    root.addEventListener("change", (e) => {
      const cb = e.target.closest('input[data-action="toggle-pref"]');
      if (cb) h.toggle(cb.dataset.pref, cb.checked);
    });
  }

  return { render, focusHeading, bindActions };
}
```

`js/settings/controller.js`:

```js
// Wires the settings view: flips a pref and announces the result.
const NAMES = {
  showCardDone: "Card Done checkboxes",
  alwaysShowDeleteCard: "Always show Delete card",
};

export function createSettingsController(model, view, announce, { onBack } = {}) {
  view.bindActions({
    back: () => onBack?.(),
    toggle: (key, value) => {
      model.set(key, value);
      announce(`${NAMES[key] ?? "Setting"} ${value ? "on" : "off"}.`);
    },
  });
  model.subscribe((prefs) => view.render(prefs));
}
```

- [ ] **Step 3: Wire the shell and coordinator**

In `index.html`, replace the header line:

```html
  <header class="app-header"><h1>Wend</h1></header>
```

with:

```html
  <header class="app-header">
    <h1>Wend</h1>
    <button type="button" id="settings-link">Settings</button>
  </header>
```

In `js/main.js`, add the imports after the toast import:

```js
import { createSettingsModel } from "./settings/model.js";
import { createSettingsView } from "./settings/view.js";
import { createSettingsController } from "./settings/controller.js";
```

…replace the whole `showOverview` function with this version (adds an opt-in input focus for
returns from Settings — the overview has no heading, and first paint must stay unfocused so the
skip link keeps working):

```js
function showOverview(focusBoardId, focusInput = false) {
  mount((root) => {
    const model = createBoardsModel();
    const view = createBoardsView(root);
    createBoardsController(model, view, announce, { onOpen: showBoard });
    // After (re)load, return focus to the board we came back from — but not on first paint.
    model.load().then(() => {
      if (focusBoardId) view.focusOpen(focusBoardId);
      else if (focusInput) view.focusNewBoardInput();
    });
  });
}
```

…and add before the final `showOverview();` line:

```js
function showSettings() {
  mount((root) => {
    const model = createSettingsModel();
    const view = createSettingsView(root);
    createSettingsController(model, view, announce, { onBack: () => showOverview(null, true) });
    view.focusHeading(); // house pattern: mounting focuses the screen's heading
  });
}
document.getElementById("settings-link").addEventListener("click", showSettings);
```

In `css/app.css`, replace the `.app-header` rule with (and append the settings rules):

```css
.app-header {
  padding: 1rem;
  border-bottom: 1px solid;
  border-color: color-mix(in srgb, currentColor 15%, transparent);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

/* Settings screen. */
.settings-view { display: flex; flex-direction: column; }
.settings-heading { margin: 0 0 1rem; font-size: 1.25rem; }
.setting-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  min-height: 44px; /* touch target */
}
.setting-hint {
  opacity: 0.7;
  font-size: 0.9rem;
  margin: 0.25rem 0 1rem;
}
```

- [ ] **Step 4: Verify manually**

First manual run after Task 6 — **reset the database once on this machine**:

```powershell
Remove-Item "$env:LOCALAPPDATA\Wend\data.db"
dotnet run --project Wend.Api
```

Check at `http://127.0.0.1:5174`:
- Settings button sits in the header on every screen; clicking it mounts the Settings screen and focus lands on the "Settings" heading (a focus ring is visible).
- Both toggles flip and **survive a full page reload** (localStorage).
- Keyboard: Tab reaches back link → both checkboxes; Space flips them; the screen reader announcement fires (check `#status` in devtools or listen with NVDA).
- ← Boards returns to the overview with the "New board…" input focused (the overview's focus anchor — it has no heading).

- [ ] **Step 5: Commit and hand over**

```powershell
git add Wend.Api/wwwroot/js/prefs.js Wend.Api/wwwroot/js/settings Wend.Api/wwwroot/index.html Wend.Api/wwwroot/js/main.js Wend.Api/wwwroot/css/app.css
git commit -m "Checklist Task 7 — prefs store + Settings screen + header entry"
git push
```

Henry: `git fetch && git switch feature/checklist && git pull` — and reset **your** `data.db` before your first `dotnet run` (command at the top of the plan).

---

### Task 8: Frontend — board chips: progress pill + bar, done-checkbox gating — *Henry*

**Files:**
- Modify: `Wend.Api/wwwroot/js/board/view.js`
- Modify: `Wend.Api/wwwroot/css/app.css`

- [ ] **Step 1: Gate the chip checkbox and add the progress pill + bar**

In `js/board/view.js`, add the prefs import under the escape import:

```js
import { getPrefs } from "../prefs.js";
```

At the top of `paint()`, after `const labelsById = …`, add:

```js
    const prefs = getPrefs();
```

Replace the `cardAria` helper so the count reaches screen readers:

```js
    const cardAria = (c) => {
      const names = (c.labelIds ?? []).map((id) => labelsById.get(id)).filter(Boolean).map((l) => l.name);
      const progress = c.checklistTotal ? `, ${c.checklistDone} of ${c.checklistTotal} done` : "";
      return `Open card: ${c.title}${names.length ? `, labels: ${names.join(", ")}` : ""}${progress}`;
    };
```

In the card template inside `paint()`, replace the leading checkbox line:

```js
                <input type="checkbox" class="card-done-toggle" data-action="toggle-done" data-card-id="${c.id}"
                  aria-label="Mark done: ${escapeHtml(c.title)}" />
```

with a pref-gated version:

```js
                ${prefs.showCardDone ? `<input type="checkbox" class="card-done-toggle" data-action="toggle-done" data-card-id="${c.id}"
                  aria-label="Mark done: ${escapeHtml(c.title)}" />` : ""}
```

…and replace the chip's due-date line:

```js
                  ${c.dueDate ? `<span class="card-due">${escapeHtml(c.dueDate)}</span>` : ""}
```

with the due date + progress pill + bar (the width is a computed number, never user input;
the bar is decorative — the pill and `aria-label` carry the information):

```js
                  ${c.dueDate ? `<span class="card-due">${escapeHtml(c.dueDate)}</span>` : ""}
                  ${c.checklistTotal ? `<span class="card-checklist">☑ ${c.checklistDone}/${c.checklistTotal}</span>
                  <span class="card-progress" aria-hidden="true"><span class="card-progress-fill" style="width:${Math.round((c.checklistDone / c.checklistTotal) * 100)}%"></span></span>` : ""}
```

**Leave the Done area exactly as it is** — it renders whenever done cards exist, pref or no pref
(hide entry points, never state; its un-check checkboxes stay as the recovery path).

- [ ] **Step 2: Style the pill and bar (mobile-first, no breakpoints needed)**

Append to `css/app.css`:

```css
/* Checklist progress on a card chip: a quiet count pill + a thin decorative bar. */
.card-checklist {
  font-size: 0.8rem;
  opacity: 0.85;
}
.card-progress {
  display: block;
  height: 4px;
  border-radius: 999px;
  background: color-mix(in srgb, currentColor 12%, transparent);
  overflow: hidden;
}
.card-progress-fill {
  display: block;
  height: 100%;
  border-radius: 999px;
  background: var(--wend-mint, #52e0b6);
}
/* Forced colors flatten backgrounds — keep the bar visible with real borders/colors. */
@media (forced-colors: active) {
  .card-progress { border: 1px solid CanvasText; background: Canvas; }
  .card-progress-fill { background: CanvasText; }
}
```

- [ ] **Step 3: Verify manually**

Reset your `data.db` if this is this machine's first run since Task 6, then `dotnet run --project Wend.Api`:
- Seed via the UI: create a board, a list, a card. In devtools' console, add items through the UI's API:
  `await fetch("/api/cards/" + 1 + "/checklist-items", {method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({text:"One"})})` — **use the real card id** (self-discovering seed: `(await (await fetch("/api/boards")).json())[0]` then walk to the first card — ids drift; hardcoding 1 bit us in Plan 4).
- Board view: the chip shows `☑ 0/1`-style pill + bar; the bar fills as you check items via a second `fetch` to `/check`; a card with no items shows nothing.
- With *Show card Done checkboxes* **off** (default): no leading checkboxes on chips; an already-done card still appears in the Done area with a working un-check.
- Toggle the setting on in Settings → back to the board → checkboxes are back and work.
- Screen reader / accessibility tree: the chip's name ends with "…, 1 of 2 done".

- [ ] **Step 4: Commit and hand over**

```powershell
git add Wend.Api/wwwroot/js/board/view.js Wend.Api/wwwroot/css/app.css
git commit -m "Checklist Task 8 — board chips: progress pill + bar, gated done checkbox"
git push
```

Malin: `git fetch && git switch feature/checklist && git pull`

---

### Task 9: Frontend — checklist section in the task view (normal mode) — *Malin*

**Files:**
- Create: `Wend.Api/wwwroot/js/card/checklist.js`
- Modify: `Wend.Api/wwwroot/js/card/model.js` (items + add/check/rename)
- Modify: `Wend.Api/wwwroot/js/card/view.js` (compose the section; ui state; Esc precedence; focus helpers)
- Modify: `Wend.Api/wwwroot/js/card/controller.js` (handlers + progress announcements)
- Modify: `Wend.Api/wwwroot/css/app.css`

- [ ] **Step 1: Create the pure render helper `js/card/checklist.js`**

```js
import { escapeHtml } from "../escape.js";

// Returns the Checklist-section HTML from (card, ui). Pure — no state, no fetch.
// ui = { editMode, doneOpen, renamingId } — renamingId is an item id, "title", or null.
// Unchecked items keep their stored order; checked items render inside a collapsible Done
// strip (same Position sequence, so un-checking drops an item back where it lived).
export function renderChecklist(card, ui) {
  const items = card.items ?? [];
  const unchecked = items.filter((i) => !i.checkedAt);
  const done = items.filter((i) => i.checkedAt);
  const total = items.length;

  const itemText = (i) =>
    ui.renamingId === i.id
      ? `<form class="rename-form" data-action="save-item-rename" data-item-id="${i.id}">
          <input name="text" value="${escapeHtml(i.text)}" aria-label="Item text" maxlength="200" required />
          <button type="submit">Save</button>
        </form>`
      : `<button type="button" class="rename-trigger" data-action="rename-item" data-item-id="${i.id}"
          aria-label="Rename: ${escapeHtml(i.text)}">${escapeHtml(i.text)}</button>`;

  const uncheckedRows = unchecked
    .map((i, idx) => `
      <li class="checklist-row" data-item-id="${i.id}">
        <input type="checkbox" data-action="toggle-item" data-item-id="${i.id}"
          aria-label="Mark done: ${escapeHtml(i.text)}" />
        ${itemText(i)}
        ${ui.editMode ? `<span class="item-actions">
          <button type="button" data-action="item-up" data-item-id="${i.id}" ${idx === 0 ? "disabled" : ""}
            aria-label="Move up: ${escapeHtml(i.text)}">▲</button>
          <button type="button" data-action="item-down" data-item-id="${i.id}" ${idx === unchecked.length - 1 ? "disabled" : ""}
            aria-label="Move down: ${escapeHtml(i.text)}">▼</button>
          <button type="button" data-action="delete-item" data-item-id="${i.id}"
            aria-label="Delete: ${escapeHtml(i.text)}">✕</button>
        </span>` : ""}
      </li>`)
    .join("");

  const doneStrip = done.length ? `
    <div class="done-strip">
      <button type="button" class="done-toggle" data-action="toggle-item-done-section"
        aria-expanded="${ui.doneOpen ? "true" : "false"}">✓ Done (${done.length})</button>
      ${ui.doneOpen ? `<ul class="done-items">${done
        .map((i) => `
        <li class="done-item-row" data-item-id="${i.id}">
          <label class="done-row-label">
            <input type="checkbox" data-action="toggle-item" data-item-id="${i.id}" checked
              aria-label="Mark not done: ${escapeHtml(i.text)}" />
            <span class="done-item-text">${escapeHtml(i.text)}</span>
          </label>
          ${ui.editMode ? `<button type="button" data-action="delete-item" data-item-id="${i.id}"
            aria-label="Delete: ${escapeHtml(i.text)}">✕</button>` : ""}
        </li>`)
        .join("")}</ul>` : ""}
    </div>` : "";

  return `
    <section class="checklist-section" aria-label="Checklist">
      <div class="checklist-header">
        <span class="checklist-title">Checklist</span>
        ${total ? `<span class="card-checklist">☑ ${done.length}/${total}</span>` : ""}
      </div>
      ${total ? `<span class="card-progress" aria-hidden="true"><span class="card-progress-fill" style="width:${Math.round((done.length / total) * 100)}%"></span></span>` : ""}
      <ul class="checklist-items">${uncheckedRows}</ul>
      ${doneStrip}
      <form class="item-form" data-action="add-item">
        <input name="text" aria-label="Add a checklist item" placeholder="Add an item…" maxlength="200" required />
        <button type="submit">Add</button>
      </form>
    </section>`;
}
```

- [ ] **Step 2: Extend `js/card/model.js`**

Add `items: []` to the initial card state (inside the `let card = { … }` literal, after `labels: []`):

```js
  let card = { id: cardId, listId: 0, listTitle: "", boardId: 0, title: "", description: "", dueDate: null, position: 0, labels: [], items: [] };
```

Add these methods after `setDone`:

```js
    async addItem(text) {
      await api(`/api/cards/${cardId}/checklist-items`, { method: "POST", body: JSON.stringify({ text }) });
      await this.load();
    },
    async checkItem(id, checked) {
      await api(`/api/checklist-items/${id}/check`, { method: "PUT", body: JSON.stringify({ checked }) });
      await this.load();
    },
    async renameItem(id, text) {
      await api(`/api/checklist-items/${id}`, { method: "PUT", body: JSON.stringify({ text }) });
      await this.load();
    },
```

- [ ] **Step 3: Compose the section in `js/card/view.js`**

Add the import under the labels import:

```js
import { renderChecklist } from "./checklist.js";
```

Extend the `ui` literal:

```js
  const ui = { pickerOpen: false, mode: "list", editingId: null, editMode: false, doneOpen: false, renamingId: null };
```

In `paint()`, insert the checklist section between `${renderLabels(card, lastPalette, ui)}` and the `<form class="card-detail"` line:

```js
        ${renderChecklist(card, ui)}
```

Add these focus helpers and rename transitions after `focusLabelName()`:

```js
  function focusAddInput() { root.querySelector(".item-form input")?.focus(); }
  function focusDoneStripToggle() { root.querySelector(".done-strip .done-toggle")?.focus(); }
  function focusRenameTrigger(id) { root.querySelector(`[data-action="rename-item"][data-item-id="${id}"]`)?.focus(); }
  function focusItem(id) { root.querySelector(`input[data-action="toggle-item"][data-item-id="${id}"]`)?.focus(); }

  // Purely-visual rename transitions (no server): flip ui + repaint + place focus.
  function startItemRename(id) { ui.renamingId = id; paint(); root.querySelector(".rename-form input")?.select(); }
  function cancelRename() {
    const id = ui.renamingId;
    ui.renamingId = null;
    paint();
    if (id !== null && id !== "title") focusRenameTrigger(id);
    else focusHeading();
  }
```

Replace the existing Escape `keydown` handler with the **precedence version** (one keystroke, one
effect — rename beats picker; Edit-mode exit arrives in Task 10):

```js
    root.addEventListener("keydown", (e) => {
      if (e.key !== "Escape") return;
      if (ui.renamingId !== null) { e.stopPropagation(); cancelRename(); return; }
      if (ui.pickerOpen) { e.stopPropagation(); closePicker(); }
    });
```

In the `submit` listener, add two branches after the `save` branch:

```js
      } else if (action === "add-item") {
        e.preventDefault();
        const f = e.target;
        const text = f.elements["text"].value.trim(); // f.text would shadow the form's name attr
        if (!text) return;
        const submit = f.querySelector("button[type=submit]");
        submit.disabled = true;
        try { await h.addItem(text); } finally { submit.disabled = false; }
      } else if (action === "save-item-rename") {
        e.preventDefault();
        const f = e.target;
        const text = f.elements["text"].value.trim();
        if (!text) return;
        ui.renamingId = null;
        await h.renameItem(Number(f.dataset.itemId), text);
      }
```

In the `change` listener, add before the label branch:

```js
      const item = e.target.closest('input[data-action="toggle-item"]');
      if (item) return h.toggleItem(Number(item.dataset.itemId), item.checked);
```

In the `click` listener's switch, add:

```js
      if (a === "rename-item") return startItemRename(Number(btn.dataset.itemId));
      if (a === "toggle-item-done-section") { ui.doneOpen = !ui.doneOpen; paint(); focusDoneStripToggle(); return; }
```

Export the new helpers by extending the return line:

```js
  return { render, focusHeading, focusPickerTrigger, focusToggle, focusPicker, bindActions,
    focusAddInput, focusDoneStripToggle, focusRenameTrigger, focusItem };
```

- [ ] **Step 4: Wire `js/card/controller.js`**

Add these handlers inside `view.bindActions({ … })`, after `toggleDone`:

```js
        addItem: async (text) => {
            try {
                await model.addItem(text);
                announce("Item added.");
                view.focusAddInput(); // consecutive adds flow without re-tabbing
            } catch {
                announce("Couldn't add the item — please try again.");
            }
        },
        toggleItem: async (id, checked) => {
            const item = (current.items ?? []).find((i) => i.id === id);
            const text = item ? item.text : "the item";
            try {
                await model.checkItem(id, checked);
                const items = current.items ?? [];
                const doneCount = items.filter((i) => i.checkedAt).length;
                announce(`${checked ? "Checked" : "Un-checked"}: ${text} — ${doneCount} of ${items.length} done.`);
                if (checked) view.focusDoneStripToggle(); // the row left for the (maybe collapsed) strip
                else view.focusItem(id);                  // the row is back among the unchecked
            } catch {
                announce("Couldn't update the item — please try again.");
            }
        },
        renameItem: async (id, text) => {
            try {
                await model.renameItem(id, text);
                announce("Item renamed.");
                view.focusRenameTrigger(id);
            } catch {
                announce("Couldn't rename the item — please try again.");
            }
        },
```

(`current` is refreshed by `model.subscribe` before the announcement reads it — the model
re-loads and notifies before `checkItem` resolves.)

- [ ] **Step 5: Style it (append to `css/app.css`)**

```css
/* Checklist section in the task view. */
.checklist-section {
  border: 1px solid;
  border-color: color-mix(in srgb, currentColor 15%, transparent);
  border-radius: 0.5rem;
  padding: 0.75rem;
  margin-bottom: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.checklist-header { display: flex; gap: 0.5rem; align-items: baseline; }
.checklist-title { font-weight: 600; }
.checklist-items, .done-items {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.checklist-row, .done-item-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  min-height: 44px; /* touch target */
}
.checklist-row .rename-trigger {
  flex: 1;
  text-align: left;
  background: none;
  border: none;
  padding: 0.25rem 0;
  font: inherit;
  color: inherit;
}
.rename-form { flex: 1; display: flex; gap: 0.5rem; }
.rename-form input { flex: 1; }
.item-actions { display: flex; gap: 0.25rem; }
.item-form { display: flex; gap: 0.5rem; }
.item-form input { flex: 1; }
/* Done strip: dimmed, struck-through rows behind a disclosure. */
.done-strip {
  border-top: 1px solid;
  border-color: color-mix(in srgb, currentColor 15%, transparent);
  padding-top: 0.5rem;
}
.done-item-row { opacity: 0.6; }
.done-item-text { text-decoration: line-through; }
.done-row-label { display: flex; gap: 0.5rem; align-items: center; flex: 1; }
```

- [ ] **Step 6: Verify manually**

`dotnet run --project Wend.Api`, open a card:
- Add three items via the input — after each add, **focus stays in the cleared input** (type-Enter-type-Enter works with no mouse).
- Check an item → it disappears from the list, "✓ Done (1)" strip appears collapsed, focus lands on the strip toggle, announcement includes "1 of 3 done"; expand → the item is struck through; un-check → it returns **to its original spot**, focus follows it.
- Click an item's text → it becomes an input (text selected); Enter saves (focus returns to the text); **Esc cancels** (focus returns to the text). Esc with the label picker open still only closes the picker.
- Progress pill + bar update; the pill matches the strip count; the chip on the board view shows the same numbers after going back.
- Everything above works keyboard-only.

- [ ] **Step 7: Commit and hand over**

```powershell
git add Wend.Api/wwwroot/js/card/checklist.js Wend.Api/wwwroot/js/card/model.js Wend.Api/wwwroot/js/card/view.js Wend.Api/wwwroot/js/card/controller.js Wend.Api/wwwroot/css/app.css
git commit -m "Checklist Task 9 — task-view checklist: add, check into Done strip, rename"
git push
```

Henry: `git fetch && git switch feature/checklist && git pull`

---

### Task 10: Frontend — Edit mode: toggle, reorder/delete controls, gated form + Delete card — *Henry*

**Files:**
- Modify: `Wend.Api/wwwroot/js/card/view.js`
- Modify: `Wend.Api/wwwroot/js/card/model.js` (`moveItem`)
- Modify: `Wend.Api/wwwroot/js/card/controller.js` (`toggleEditMode`, `moveItemUp/Down`, title rename)
- Modify: `Wend.Api/wwwroot/css/app.css`

- [ ] **Step 1: Restructure `paint()` in `js/card/view.js`**

Add the prefs import under the checklist import:

```js
import { getPrefs } from "../prefs.js";
```

Replace the whole `paint()` body with:

```js
  function paint() {
    const card = lastCard;
    const prefs = getPrefs();
    root.innerHTML = `
      <div class="card-view">
        <div class="card-view-top">
          <button class="back-link" data-action="back">← Board</button>
          <button type="button" class="edit-toggle" data-action="toggle-edit"
            aria-pressed="${ui.editMode ? "true" : "false"}">${ui.editMode ? "Editing…" : "Edit"}</button>
        </div>
        <h2 class="card-heading" tabindex="-1">${
          ui.renamingId === "title"
            ? `<form class="rename-form" data-action="save-title">
                <input name="text" value="${escapeHtml(card.title)}" aria-label="Card title" maxlength="200" required />
                <button type="submit">Save</button>
              </form>`
            : `<button type="button" class="rename-trigger" data-action="rename-title"
                aria-label="Rename: ${escapeHtml(card.title)}">${escapeHtml(card.title)}</button>`
        }</h2>
        <p class="card-list-name">In list: <strong>${escapeHtml(card.listTitle)}</strong></p>
        ${prefs.showCardDone ? `<div class="card-done">
          <label class="card-done-label">
            <input type="checkbox" data-action="toggle-done" ${card.completedAt ? "checked" : ""} />
            <span>Done</span>
          </label>
        </div>` : ""}
        ${renderLabels(card, lastPalette, ui)}
        ${renderChecklist(card, ui)}
        ${ui.editMode ? `
        <form class="card-detail" data-action="save">
          <label class="field">
            <span>Due date</span>
            <input name="dueDate" type="date" value="${card.dueDate ?? ""}" aria-label="Due date" />
          </label>
          <label class="field">
            <span>Notes</span>
            <textarea name="description" aria-label="Notes">${escapeHtml(card.description ?? "")}</textarea>
          </label>
          <button type="submit">Save changes</button>
        </form>` : `
        ${card.dueDate ? `<p class="card-meta">Due: <strong>${escapeHtml(card.dueDate)}</strong></p>` : ""}
        ${card.description ? `<p class="card-notes">${escapeHtml(card.description)}</p>` : ""}`}
        ${ui.editMode || prefs.alwaysShowDeleteCard ? `<button class="card-delete" data-action="delete">Delete card</button>` : ""}
      </div>`;
  }
```

(The title field left the form — the heading **is** the rename control now, in both modes.)

- [ ] **Step 2: Edit-mode transitions and Esc**

Add after `cancelRename()`:

```js
  function focusEditToggle() { root.querySelector(".edit-toggle")?.focus(); }
  // Flips Edit mode, repaints, and parks focus on the toggle — so focus never dies with a
  // control that just disappeared. Returns the new state for the controller's announcement.
  function toggleEditMode() {
    ui.editMode = !ui.editMode;
    paint();
    focusEditToggle();
    return ui.editMode;
  }
```

Replace the Escape `keydown` handler with the full precedence chain:

```js
    root.addEventListener("keydown", (e) => {
      if (e.key !== "Escape") return;
      if (ui.renamingId !== null) { e.stopPropagation(); cancelRename(); return; }
      if (ui.pickerOpen) { e.stopPropagation(); closePicker(); return; }
      if (ui.editMode) { e.stopPropagation(); h.toggleEditMode(); }
    });
```

In the `click` switch, add:

```js
      if (a === "toggle-edit") return h.toggleEditMode();
      if (a === "rename-title") { ui.renamingId = "title"; paint(); root.querySelector(".rename-form input")?.select(); return; }
      if (a === "item-up") return h.moveItemUp(Number(btn.dataset.itemId));
      if (a === "item-down") return h.moveItemDown(Number(btn.dataset.itemId));
```

In the `submit` listener: the `save` branch's payload no longer has a title field — replace its body-building line with:

```js
          await h.save({ title: lastCard.title, description: f.description.value, dueDate: f.dueDate.value || null });
```

…and add a `save-title` branch after `save-item-rename`:

```js
      } else if (action === "save-title") {
        e.preventDefault();
        const text = e.target.elements["text"].value.trim();
        if (!text) return;
        ui.renamingId = null;
        await h.saveTitle(text);
```

Add the focus helper for the title trigger and export the new functions:

```js
  function focusTitleTrigger() { root.querySelector('[data-action="rename-title"]')?.focus(); }
```

```js
  return { render, focusHeading, focusPickerTrigger, focusToggle, focusPicker, bindActions,
    focusAddInput, focusDoneStripToggle, focusRenameTrigger, focusItem,
    focusEditToggle, focusTitleTrigger, toggleEditMode };
```

- [ ] **Step 3: Model + controller**

`js/card/model.js` — add after `renameItem`:

```js
    async moveItem(id, position) {
      await api(`/api/checklist-items/${id}/move`, { method: "PUT", body: JSON.stringify({ position }) });
      await this.load();
    },
```

`js/card/controller.js` — add handlers after `renameItem`:

```js
        toggleEditMode: () => {
            const on = view.toggleEditMode();
            announce(on ? "Edit mode on." : "Edit mode off.");
        },
        saveTitle: async (text) => {
            try {
                await model.save({ title: text, description: current.description, dueDate: current.dueDate });
                announce("Card renamed.");
                view.focusTitleTrigger();
            } catch {
                announce("Couldn't rename the card — please try again.");
            }
        },
        moveItemUp: (id) => moveItem(id, -1, "item-up"),
        moveItemDown: (id) => moveItem(id, +1, "item-down"),
```

…and this helper after the `nameOf` line (the Plan 6 active-subset lesson: target the
neighbouring **unchecked** item's position, stepping over interleaved checked items):

```js
    async function moveItem(id, delta, action) {
        const unchecked = (current.items ?? []).filter((i) => !i.checkedAt);
        const index = unchecked.findIndex((i) => i.id === id);
        if (index < 0) return;
        const target = index + delta;
        if (target < 0 || target >= unchecked.length) return; // already at an end (button is disabled)
        const text = unchecked[index].text;
        try {
            await model.moveItem(id, unchecked[target].position);
            announce(`${delta < 0 ? "Moved up" : "Moved down"}: ${text}.`);
            view.focusItemAction(id, action);
        } catch {
            announce("Couldn't move the item — please try again.");
        }
    }
```

Add the matching focus helper in `view.js` (and to the export list):

```js
  function focusItemAction(id, action) {
    root.querySelector(`[data-action="${action}"][data-item-id="${id}"]`)?.focus();
  }
```

- [ ] **Step 4: Style (append to `css/app.css`)**

```css
/* Task-view header row + edit toggle. */
.card-view-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}
.card-view-top .back-link { margin-bottom: 0; }
.card-heading .rename-trigger {
  background: none;
  border: none;
  padding: 0;
  font: inherit;
  color: inherit;
  text-align: left;
}
.card-meta { margin: 0 0 0.5rem; }
.card-notes {
  margin: 0 0 1rem;
  white-space: pre-wrap; /* keep the user's line breaks in read-only notes */
}
```

- [ ] **Step 5: Verify manually**

`dotnet run --project Wend.Api`, open a card:
- Normal mode: no form, no ▲▼/✕, no Delete card; due date + notes read as text (line breaks kept); heading and item texts still rename directly.
- Edit (top right): `aria-pressed` flips; ▲▼/✕ appear on unchecked rows, ✕ on expanded strip rows, due/notes form + Delete card appear. **Esc** (outside a rename) exits Edit and focus lands on the toggle.
- Esc precedence: with an item rename open *and* Edit on, Esc only cancels the rename; second Esc exits Edit.
- ▲▼: check the middle item of three, then move the last one up — it steps **over** the checked item's slot (lands top), announcement "Moved up: …", focus stays on the ▲ you pressed.
- Settings → *Always show Delete card* on → Delete card is visible without Edit mode.
- Board view (pref off): chips have no checkbox; task view has no Done block.

- [ ] **Step 6: Commit and hand over**

```powershell
git add Wend.Api/wwwroot/js/card/view.js Wend.Api/wwwroot/js/card/model.js Wend.Api/wwwroot/js/card/controller.js Wend.Api/wwwroot/css/app.css
git commit -m "Checklist Task 10 — Edit mode: toggle, reorder + delete controls, gated form"
git push
```

Malin: `git fetch && git switch feature/checklist && git pull`

---

### Task 11: Frontend — item delete + toast undo (navigate-and-re-mount) — *Malin*

**Files:**
- Modify: `Wend.Api/wwwroot/js/card/model.js` (`deleteItem`)
- Modify: `Wend.Api/wwwroot/js/card/controller.js` (`deleteItem` handler + `onItemDeleted`)
- Modify: `Wend.Api/wwwroot/js/card/view.js` (`focusItem` opens the strip when needed)
- Modify: `Wend.Api/wwwroot/js/toast.js` (configurable `ariaLabel`)
- Modify: `Wend.Api/wwwroot/js/main.js` (toast wiring + `undoItemDelete`)

- [ ] **Step 1: Model + controller**

`js/card/model.js` — add after `moveItem`:

```js
    async deleteItem(id) {
      await api(`/api/checklist-items/${id}`, { method: "DELETE" });
      await this.load();
    },
```

`js/card/controller.js` — accept the new callback and add the handler. Change the signature line to:

```js
export function createCardController(model, view, announce, {onBack, onDeleted, onItemDeleted} = {}) {
```

…and add after `moveItemDown`:

```js
        deleteItem: async (id) => {
            const item = (current.items ?? []).find((i) => i.id === id);
            const text = item ? item.text : "the item";
            try {
                await model.deleteItem(id);
                onItemDeleted?.(id, text); // the coordinator shows the undo toast
                view.focusAddInput();      // the row is gone — park focus on the add input
            } catch {
                announce("Couldn't delete the item — please try again.");
            }
        },
```

- [ ] **Step 2: `focusItem` must never target a collapsed strip**

In `js/card/view.js`, replace the `focusItem` helper with:

```js
  function focusItem(id) {
    const item = (lastCard.items ?? []).find((i) => i.id === id);
    if (item?.checkedAt && !ui.doneOpen) { ui.doneOpen = true; paint(); } // open the strip first
    root.querySelector(`input[data-action="toggle-item"][data-item-id="${id}"]`)?.focus();
  }
```

- [ ] **Step 3: Toast label**

In `js/toast.js`, make the group label configurable (items aren't cards). Change the `show` signature:

```js
  function show({ message, actionLabel, onAction, onDismissFocus, ariaLabel = "Deleted card" }) {
```

…and the label line inside it:

```js
    el.setAttribute("aria-label", ariaLabel);
```

- [ ] **Step 4: Coordinator wiring in `js/main.js`**

Replace the whole `showCard` function and add `undoItemDelete` after `undoDelete`:

```js
function showCard(cardId, boardId, focusItemId) {
    mount((root) => {
        const model = createCardModel(cardId);
        const view = createCardView(root);
        createCardController(model, view, announce, {
            onBack: () => showBoard(boardId, cardId), // return → focus the card we opened
            onDeleted: (deletedId, title) => {
                showBoard(boardId); // card is gone → back to the board, focus the heading
                toast.show({
                    message: `Deleted: ${title}`,
                    actionLabel: "Undo",
                    onAction: () => undoDelete(deletedId, title, boardId),
                    onDismissFocus: () => document.querySelector(".board-heading")?.focus(),
                });
                announce(`Deleted: ${title}. Undo available.`);
            },
            onItemDeleted: (itemId, text) => {
                toast.show({
                    message: `Deleted: ${text}`,
                    actionLabel: "Undo",
                    onAction: () => undoItemDelete(itemId, text, cardId, boardId),
                    onDismissFocus: () => document.querySelector(".item-form input")?.focus(),
                    ariaLabel: "Deleted checklist item",
                });
                announce(`Deleted: ${text}. Undo available.`);
            },
        });
        model.load().then(() => {
            if (focusItemId) view.focusItem(focusItemId);
            else view.focusHeading();
        });
    });
}

// The toast outlives navigation, so undo RE-MOUNTS the task view from wherever we are
// (mirrors undoDelete's navigate-on-undo) and focuses the restored item — focusItem opens
// the Done strip first if the item came back checked.
async function undoItemDelete(itemId, text, cardId, boardId) {
    try {
        await api(`/api/checklist-items/${itemId}/restore`, { method: "POST" });
        announce(`Restored: ${text}.`);
        showCard(cardId, boardId, itemId);
    } catch {
        announce("Couldn't restore the item — please try again.");
    }
}
```

- [ ] **Step 5: Verify manually**

`dotnet run --project Wend.Api`:
- Edit mode → ✕ an item → toast "Deleted: … · Undo"; announcement fires; focus parked on the add input. Undo → item back **in its old position**, focused; announcement "Restored: …".
- Delete a **checked** item from the strip → Undo → the strip **opens itself** and focus lands on the restored item's checkbox.
- Delete an item → **navigate Back to the board** → Undo from the toast → the task view re-mounts with the item restored and focused.
- Delete two items quickly → one toast only (replace-not-stack); Undo restores the second (the first stays recoverable in the db — Trash slice).
- Toast timer still pauses on hover/focus; × dismiss returns focus to the add input.
- The toast group announces as "Deleted checklist item" (devtools accessibility tree).

- [ ] **Step 6: Commit and hand over**

```powershell
git add Wend.Api/wwwroot/js/card/model.js Wend.Api/wwwroot/js/card/controller.js Wend.Api/wwwroot/js/card/view.js Wend.Api/wwwroot/js/toast.js Wend.Api/wwwroot/js/main.js
git commit -m "Checklist Task 11 — item delete with toast undo, navigate-and-re-mount restore"
git push
```

Henry: `git fetch && git switch feature/checklist && git pull`

---

### Task 12: Acceptance + README + backlog — *Henry* (acceptance run with Malin)

**Files:**
- Modify: `README.md`
- Modify: `docs/backlog.md`

- [ ] **Step 1: Full-suite check**

Run: `dotnet test`
Expected: PASS — **147 tests, 0 warnings**. If the count differs, STOP and find the dropped test (Plan 3's lesson) before continuing.

- [ ] **Step 2: Keyboard + screen-reader acceptance (both of you, phone-width too)**

Script — all keyboard-only, then repeat the marked ones with NVDA:
1. Boards → open board → open card. Add 3 items typing-Enter without touching the mouse. *(SR: each "Item added.")*
2. Check item 2 → strip appears; expand; un-check → returns to slot 2. *(SR: progress counts)*
3. Rename an item via its text; Esc-cancel once, Enter-save once.
4. Edit mode on (button + Esc off again + `aria-pressed` in the tree). Reorder with ▲▼ across a checked item. Delete an item, Undo from the toast **after** tabbing to it (timer pauses).
5. Back to board mid-toast → Undo → task view re-mounts, item focused.
6. Chips show `☑ n/m` + bar; SR chip name ends "…done". No done checkboxes anywhere (default prefs). Settings: flip both toggles → checkboxes + always-visible Delete card appear; reload → still set.
7. Forced-colors (Windows High Contrast) spot-check: progress bar visible with borders.

- [ ] **Step 3: README updates (exact edits)**

In `README.md`, replace the Status paragraph's feature sentence (line 15) with:

```markdown
Boards, lists, and cards work end to end — create, rename, delete, and reorder lists inside a board, add cards to a list, move a card within its list or to another list, label them, delete a card with a one-click undo, open a card into a focused task view with an Edit mode, keep a per-card checklist (add, rename, reorder, check off into a collapsible Done strip, delete with undo) with progress shown on the board's card chips, and tune it all in a small settings screen — saved to SQLite, accessible and dark-mode-first.
```

Replace the `- **Done:** …` bullet with:

```markdown
- **Done:** the board, list, card, label, and checklist backend (JSON APIs behind `IBoardRepository`, `IListRepository`, `ICardRepository`, `ILabelRepository`, and `IChecklistItemRepository` seams, EF Core + SQLite, 147 NUnit tests, localhost-only) and the vanilla-JS MVC frontend (board-view navigation, accessible list reordering, card chips with a focused task view, accessible card moving with up/down buttons and a move-to-list dropdown, an inline label picker with soft-tint chips, a per-card checklist with a Done strip and chip progress bars, an undo-first delete for cards and checklist items with a transient "Deleted · Undo" toast, a task-view Edit mode, a localStorage settings screen gating the card Done checkboxes and the Delete card button, screen-reader announcements, keyboard focus management).
```

Replace the `- **Next:** …` bullet with:

```markdown
- **Next:** mobile + accessibility polish (per-list Done strips and a single-list phone view).
```

Append to the two doc-link lists on line 20: after the delete-undo spec link add
`, [`docs/2026-07-07-wend-checklist-design.md`](docs/2026-07-07-wend-checklist-design.md)`
and after the delete-undo plan link add
`, [`docs/plans/2026-07-07-slice1-checklist.md`](docs/plans/2026-07-07-slice1-checklist.md)`.

In "Run it", replace the sentence's tail `…to edit the title, notes, due date, and labels.` with:

```markdown
…to edit the title, notes, due date, labels, and a per-card checklist. The API lives under `/api/boards`, `/api/lists`, `/api/cards`, `/api/labels`, and `/api/checklist-items`.
```

(Keep the rest of that paragraph as is.)

- [ ] **Step 4: Backlog updates (exact edits)**

In `docs/backlog.md`, under **“Undo for checklist-item deletes”**, replace the whole entry body with:

```markdown
- **Resolved (checklist increment, feature/checklist):** shipped as specced — checklist items
  soft-delete (`DeletedAt` + query filter) with a "Deleted · Undo" toast that restores in place,
  sharing the cards' toast primitive and retention behaviour.
- **Originally decided:** 2026-07-07 (Malin & Henry, Plan 7 acceptance).
```

Under **“Label display — per-user choice of chips vs colour bars on the board front”**, replace the `- **Revisit when:** …` line with:

```markdown
- **Revisit when:** wanted — the blocker is gone: the checklist increment shipped a Settings
  screen (`js/prefs.js` + `js/settings/`), so this toggle now has a home to hang on.
```

- [ ] **Step 5: Commit, PR, review, merge**

```powershell
git add README.md docs/backlog.md
git commit -m "Checklist Task 12 — acceptance, README status, backlog updates"
git push
gh pr create --title "Per-card checklist + settings surface" --body "Checklist increment: items with add/rename/check (Done strip)/reorder/soft-delete+undo, chip progress, task-view Edit mode, localStorage settings screen. 147 NUnit, 0 warnings. Spec: docs/2026-07-07-wend-checklist-design.md"
```

Then: CI green → the **other** person reviews the diff → **merge with a merge commit, NOT squash**
(multi-author branch — squash would add a co-author trailer) → delete `feature/checklist` on
GitHub → both machines: `git switch main && git pull && git fetch --prune && git branch -d feature/checklist`.

---

## Post-merge notes

- Anyone pulling `main` resets their `data.db` once before running (new table).
- Slice 1 remaining: **Plan 8 — mobile + a11y polish** (per-list Done strips, single-list phone
  view; Claude codes it solo per the 2026-07-07 delegation split, spec + review still shared).
