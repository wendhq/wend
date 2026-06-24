# Card Moving (Plan 5) — Implementation Plan

**Goal:** Let a card be reordered within its list and moved to another list — accessible buttons + a dropdown, no drag-and-drop — behind one `PUT /api/cards/{id}/move`.

**Architecture:** Generalize Plan 2's list-move across two lists in `EfCardRepository.MoveCardAsync` (lift → clamp → insert → renumber, plus re-sequence the *source* on a cross-list move). The frontend restructures the card chip into an open-button + an actions row (▲ ▼ + a "Move to…" `<select>`) and reloads the board after each move, so positions always come from the server.

**Tech stack:** ASP.NET Core minimal API (net10) · EF Core + SQLite · NUnit · hand-authored vanilla-JS MVC (no build step).

**Execution:** Coached turn-based with Henry — alternating drivers (Malin odd tasks, Henry even), per-task red→green TDD on the backend, browser-verified frontend, per-human commits with no AI attribution. Branch off `main` (suggested `feature/card-moving`). Steps use checkboxes for tracking.

**Before building / gotchas:**
- Stop any running `Wend.exe` / `dotnet run` before `dotnet build` or `dotnet test` (it locks `Wend.Api.dll`).
- **No database reset needed** — Plan 5 adds no columns or tables; it only rewrites the existing `Card.ListId` / `Card.Position`.
- At each turn's end, check the running test count against this plan (`dotnet test --filter "FullyQualifiedName~Card"`) — the Plan 3 "a paste silently dropped 3 tests" lesson.
- Cross-machine: the driver pushes at end of turn (`git push -u origin feature/card-moving`); the next driver `git fetch && git switch feature/card-moving` before starting.

---

## Task 1 (Malin): Reorder a card within its list — repository

**Files:**
- Create: `Wend.Core/CardMoveResult.cs`
- Modify: `Wend.Core/ICardRepository.cs`
- Modify: `Wend.Core/EfCardRepository.cs`
- Test: `Wend.Tests/CardRepositoryTests.cs`

- [ ] **Step 1: Add the result type**

Create `Wend.Core/CardMoveResult.cs`:

```csharp
namespace Wend.Core;

/// <summary>Outcome of a card move, mapped to HTTP status by the endpoint.</summary>
public enum CardMoveResult
{
    Moved,
    NotFound,    // the card or the target list doesn't exist
    CrossBoard,  // the target list belongs to a different board
}
```

- [ ] **Step 2: Declare the method on the interface**

In `Wend.Core/ICardRepository.cs`, update the summary line and add the method:

```csharp
namespace Wend.Core;

/// <summary>
/// Persistence seam for cards within a list. Position is a 0-based contiguous index; the
/// repository keeps it gapless on create, delete, and move.
/// </summary>
public interface ICardRepository
{
    Task<IReadOnlyList<Card>> GetCardsForListAsync(int listId);
    Task<Card?> GetCardAsync(int id);
    Task<Card> CreateCardAsync(int listId, string title);
    Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate);
    Task<bool> DeleteCardAsync(int id);
    Task<CardMoveResult> MoveCardAsync(int id, int targetListId, int position);
}
```

- [ ] **Step 3: Write the failing tests**

In `Wend.Tests/CardRepositoryTests.cs`, add (the `_repo`, `_boards`, `_lists`, and `NewListAsync()` helpers already exist):

```csharp
[Test]
public async Task Move_reorders_a_card_up_within_its_list()
{
    var listId = await NewListAsync();
    await _repo.CreateCardAsync(listId, "A");          // 0
    await _repo.CreateCardAsync(listId, "B");          // 1
    var c = await _repo.CreateCardAsync(listId, "C");  // 2

    Assert.That(await _repo.MoveCardAsync(c.Id, listId, 0), Is.EqualTo(CardMoveResult.Moved));

    var cards = await _repo.GetCardsForListAsync(listId);
    Assert.That(cards.Select(x => x.Title), Is.EqualTo(new[] { "C", "A", "B" }));
    Assert.That(cards.Select(x => x.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless
}

[Test]
public async Task Move_reorders_a_card_down_within_its_list()
{
    var listId = await NewListAsync();
    var a = await _repo.CreateCardAsync(listId, "A");  // 0
    await _repo.CreateCardAsync(listId, "B");          // 1
    await _repo.CreateCardAsync(listId, "C");          // 2

    Assert.That(await _repo.MoveCardAsync(a.Id, listId, 2), Is.EqualTo(CardMoveResult.Moved));

    var cards = await _repo.GetCardsForListAsync(listId);
    Assert.That(cards.Select(x => x.Title), Is.EqualTo(new[] { "B", "C", "A" }));
    Assert.That(cards.Select(x => x.Position), Is.EqualTo(new[] { 0, 1, 2 }));
}

[Test]
public async Task Move_reports_a_missing_card()
{
    var listId = await NewListAsync();
    Assert.That(await _repo.MoveCardAsync(9999, listId, 0), Is.EqualTo(CardMoveResult.NotFound));
}
```

- [ ] **Step 4: Run the tests — expect a compile failure (red)**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests.Move"`
Expected: build error — `EfCardRepository` does not implement `MoveCardAsync` (the interface member is unimplemented). That's our red.

- [ ] **Step 5: Implement the within-list path**

In `Wend.Core/EfCardRepository.cs`, add the method below `DeleteCardAsync` (above the private `ResequenceAsync`). The cross-list branch is stubbed for now — Task 2 fills it:

```csharp
public async Task<CardMoveResult> MoveCardAsync(int id, int targetListId, int position)
{
    var card = await db.Cards.FindAsync(id);
    if (card is null) return CardMoveResult.NotFound;

    if (targetListId == card.ListId)
    {
        // Reorder within the list: lift out of the ordered cards, clamp, re-insert, renumber.
        var cards = await db.Cards.Where(c => c.ListId == card.ListId)
            .OrderBy(c => c.Position)
            .ToListAsync();
        cards.Remove(cards.First(c => c.Id == id));
        var index = Math.Clamp(position, 0, cards.Count);
        cards.Insert(index, card);
        for (var i = 0; i < cards.Count; i++) cards[i].Position = i;
        await db.SaveChangesAsync();
        return CardMoveResult.Moved;
    }

    return CardMoveResult.NotFound; // cross-list move arrives in Task 2
}
```

- [ ] **Step 6: Run the tests — expect pass (green)**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests.Move"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add Wend.Core/CardMoveResult.cs Wend.Core/ICardRepository.cs Wend.Core/EfCardRepository.cs Wend.Tests/CardRepositoryTests.cs
git commit -m "Add MoveCardAsync with within-list reordering"
```

---

## Task 2 (Henry): Move a card to another list — repository

**Files:**
- Modify: `Wend.Core/EfCardRepository.cs`
- Test: `Wend.Tests/CardRepositoryTests.cs`

- [ ] **Step 1: Write the failing tests**

In `Wend.Tests/CardRepositoryTests.cs`, add:

```csharp
[Test]
public async Task Move_to_another_list_appends_at_its_bottom_and_resequences_both()
{
    var board = await _boards.CreateBoardAsync("Board");
    var todo = await _lists.CreateListAsync(board.Id, "To do");
    var doing = await _lists.CreateListAsync(board.Id, "Doing");
    await _repo.CreateCardAsync(todo.Id, "A");           // todo 0
    var b = await _repo.CreateCardAsync(todo.Id, "B");   // todo 1
    await _repo.CreateCardAsync(todo.Id, "C");           // todo 2
    await _repo.CreateCardAsync(doing.Id, "X");          // doing 0

    // position 99 overshoots — it should clamp to the bottom.
    Assert.That(await _repo.MoveCardAsync(b.Id, doing.Id, 99), Is.EqualTo(CardMoveResult.Moved));

    var todoCards = await _repo.GetCardsForListAsync(todo.Id);
    Assert.That(todoCards.Select(c => c.Title), Is.EqualTo(new[] { "A", "C" }));
    Assert.That(todoCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 }));  // source gapless

    var doingCards = await _repo.GetCardsForListAsync(doing.Id);
    Assert.That(doingCards.Select(c => c.Title), Is.EqualTo(new[] { "X", "B" }));
    Assert.That(doingCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 })); // target gapless
}

[Test]
public async Task Move_to_another_list_can_insert_at_the_top()
{
    var board = await _boards.CreateBoardAsync("Board");
    var todo = await _lists.CreateListAsync(board.Id, "To do");
    var doing = await _lists.CreateListAsync(board.Id, "Doing");
    var a = await _repo.CreateCardAsync(todo.Id, "A");
    await _repo.CreateCardAsync(doing.Id, "X");  // 0
    await _repo.CreateCardAsync(doing.Id, "Y");  // 1

    Assert.That(await _repo.MoveCardAsync(a.Id, doing.Id, 0), Is.EqualTo(CardMoveResult.Moved));

    var doingCards = await _repo.GetCardsForListAsync(doing.Id);
    Assert.That(doingCards.Select(c => c.Title), Is.EqualTo(new[] { "A", "X", "Y" }));
    Assert.That(doingCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1, 2 }));
}
```

- [ ] **Step 2: Run the tests — expect fail (red)**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests.Move_to_another_list"`
Expected: FAIL — both return `NotFound` (the Task 1 stub) instead of `Moved`.

- [ ] **Step 3: Implement the cross-list branch**

In `Wend.Core/EfCardRepository.cs`, replace the stub line `return CardMoveResult.NotFound; // cross-list move arrives in Task 2` with:

```csharp
    // Move to another list: re-home the card, insert into the target at the clamped
    // position, renumber the target, then close the gap left behind in the source.
    var sourceListId = card.ListId;
    var targetCards = await db.Cards.Where(c => c.ListId == targetListId)
        .OrderBy(c => c.Position)
        .ToListAsync();
    card.ListId = targetListId;
    var pos = Math.Clamp(position, 0, targetCards.Count);
    targetCards.Insert(pos, card);
    for (var i = 0; i < targetCards.Count; i++) targetCards[i].Position = i;
    await db.SaveChangesAsync();
    await ResequenceAsync(sourceListId);
    return CardMoveResult.Moved;
```

- [ ] **Step 4: Run the tests — expect pass (green)**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests.Move"`
Expected: PASS (5 tests — the 3 from Task 1 plus these 2).

- [ ] **Step 5: Commit**

```bash
git add Wend.Core/EfCardRepository.cs Wend.Tests/CardRepositoryTests.cs
git commit -m "Move cards between lists, resequencing both"
```

---

## Task 3 (Malin): Guard missing and cross-board targets — repository

**Files:**
- Modify: `Wend.Core/EfCardRepository.cs`
- Test: `Wend.Tests/CardRepositoryTests.cs`

- [ ] **Step 1: Write the failing tests**

In `Wend.Tests/CardRepositoryTests.cs`, add:

```csharp
[Test]
public async Task Move_reports_a_missing_target_list()
{
    var listId = await NewListAsync();
    var card = await _repo.CreateCardAsync(listId, "A");

    Assert.That(await _repo.MoveCardAsync(card.Id, 9999, 0), Is.EqualTo(CardMoveResult.NotFound));
}

[Test]
public async Task Move_to_a_list_on_another_board_is_rejected()
{
    var boardA = await _boards.CreateBoardAsync("A");
    var listA = await _lists.CreateListAsync(boardA.Id, "A-list");
    var card = await _repo.CreateCardAsync(listA.Id, "Card");

    var boardB = await _boards.CreateBoardAsync("B");
    var listB = await _lists.CreateListAsync(boardB.Id, "B-list");

    Assert.That(await _repo.MoveCardAsync(card.Id, listB.Id, 0), Is.EqualTo(CardMoveResult.CrossBoard));
}
```

- [ ] **Step 2: Run the tests — expect fail (red)**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests.Move_reports_a_missing_target_list|FullyQualifiedName~CardRepositoryTests.Move_to_a_list_on_another_board"`
Expected: FAIL — with no guard, a missing target orphans the card and a cross-board move succeeds; both return `Moved`.

- [ ] **Step 3: Add the guard**

In `Wend.Core/EfCardRepository.cs`, insert these lines in `MoveCardAsync` immediately after the `if (card is null) return CardMoveResult.NotFound;` line and before the `if (targetListId == card.ListId)` block:

```csharp
    var targetList = await db.Lists.FindAsync(targetListId);
    var sourceList = await db.Lists.FindAsync(card.ListId);
    if (targetList is null || sourceList is null) return CardMoveResult.NotFound;
    if (targetList.BoardId != sourceList.BoardId) return CardMoveResult.CrossBoard;
```

- [ ] **Step 4: Run the tests — expect pass (green)**

Run: `dotnet test --filter "FullyQualifiedName~CardRepositoryTests.Move"`
Expected: PASS (7 tests). Within-list moves still pass — for them `targetList` and `sourceList` are the same list, so the board check can never trip.

- [ ] **Step 5: Commit**

```bash
git add Wend.Core/EfCardRepository.cs Wend.Tests/CardRepositoryTests.cs
git commit -m "Guard card moves against missing and cross-board targets"
```

---

## Task 4 (Henry): The move endpoint — API

**Files:**
- Modify: `Wend.Api/CardEndpoints.cs`
- Test: `Wend.Tests/CardApiTests.cs`

- [ ] **Step 1: Write the failing tests**

In `Wend.Tests/CardApiTests.cs`, add (the `CreateBoardAsync` / `CreateListAsync` / `CreateCardAsync` helpers and the `BoardWithCardsDto` / `ListWithCardsDto` records already exist):

```csharp
[Test]
public async Task Moving_a_card_within_its_list_reorders_it()
{
    var board = await CreateBoardAsync("Sprint");
    var list = await CreateListAsync(board.Id, "To do");
    var a = await CreateCardAsync(list.Id, "A");  // 0
    await CreateCardAsync(list.Id, "B");          // 1
    await CreateCardAsync(list.Id, "C");          // 2

    var move = await _client.PutAsJsonAsync($"/api/cards/{a.Id}/move", new { listId = list.Id, position = 2 });
    Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

    var detail = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");
    Assert.That(detail!.Lists.Single().Cards.Select(c => c.Title), Is.EqualTo(new[] { "B", "C", "A" }));
}

[Test]
public async Task Moving_a_card_to_another_list_appends_it_there()
{
    var board = await CreateBoardAsync("Sprint");
    var todo = await CreateListAsync(board.Id, "To do");
    var doing = await CreateListAsync(board.Id, "Doing");
    var a = await CreateCardAsync(todo.Id, "A");
    await CreateCardAsync(doing.Id, "X");

    var move = await _client.PutAsJsonAsync($"/api/cards/{a.Id}/move", new { listId = doing.Id, position = 99 });
    Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

    var detail = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");
    Assert.That(detail!.Lists.Single(l => l.Title == "To do").Cards, Is.Empty);
    Assert.That(detail.Lists.Single(l => l.Title == "Doing").Cards.Select(c => c.Title),
        Is.EqualTo(new[] { "X", "A" }));
}

[Test]
public async Task Moving_a_missing_card_is_404()
{
    var board = await CreateBoardAsync("Sprint");
    var list = await CreateListAsync(board.Id, "To do");

    var move = await _client.PutAsJsonAsync("/api/cards/9999/move", new { listId = list.Id, position = 0 });
    Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
}

[Test]
public async Task Moving_a_card_to_another_board_is_400()
{
    var boardA = await CreateBoardAsync("A");
    var listA = await CreateListAsync(boardA.Id, "A-list");
    var card = await CreateCardAsync(listA.Id, "Card");
    var boardB = await CreateBoardAsync("B");
    var listB = await CreateListAsync(boardB.Id, "B-list");

    var move = await _client.PutAsJsonAsync($"/api/cards/{card.Id}/move", new { listId = listB.Id, position = 0 });
    Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
}
```

- [ ] **Step 2: Run the tests — expect fail (red)**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests.Moving"`
Expected: FAIL — no `/move` route exists yet, so the calls return `404` (the `204` cases fail).

- [ ] **Step 3: Add the endpoint + request record**

In `Wend.Api/CardEndpoints.cs`, add this route inside `MapCardEndpoints` (just before `return app;`):

```csharp
        app.MapPut("/api/cards/{id:int}/move", async (int id, MoveCardRequest req, ICardRepository cards) =>
            await cards.MoveCardAsync(id, req.ListId, req.Position) switch
            {
                CardMoveResult.Moved => Results.NoContent(),
                CardMoveResult.CrossBoard => Results.BadRequest(),
                _ => Results.NotFound(),
            });
```

And add the record alongside the others at the bottom of the file:

```csharp
public record MoveCardRequest(int ListId, int Position);
```

- [ ] **Step 4: Run the tests — expect pass (green)**

Run: `dotnet test --filter "FullyQualifiedName~CardApiTests.Moving"`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the full suite + confirm zero warnings**

Run: `dotnet test`
Expected: PASS — 94 (existing) + 11 (Tasks 1–4) = **105 tests**, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/CardEndpoints.cs Wend.Tests/CardApiTests.cs
git commit -m "Add the card move endpoint"
```

---

## Task 5 (Malin): Up/Down move controls — board frontend

The card chip becomes a container: the title stays a click-to-open button, and an actions row gains ▲ ▼. Reordering works end-to-end after this task. (Styling is bare until Task 7.)

**Files:**
- Modify: `Wend.Api/wwwroot/js/board/view.js`
- Modify: `Wend.Api/wwwroot/js/board/model.js`
- Modify: `Wend.Api/wwwroot/js/board/controller.js`

- [ ] **Step 1: Add `moveCard` to the model**

In `board/model.js`, add this method to the returned object (e.g. after `createCard`):

```javascript
        async moveCard(id, listId, position) {
            await api(`/api/cards/${id}/move`, { method: "PUT", body: JSON.stringify({ listId, position }) });
            await this.load();
        },
```

- [ ] **Step 2: Restructure the card + add ▲ ▼ in the view**

In `board/view.js`, replace the card-mapping block (the `const cards = (l.cards ?? []).map((c) => { ... }).join("");` inside the lists `.map`) with:

```javascript
              const listCards = l.cards ?? [];
              const cards = listCards
                  .map((c, ci) => {
                    const chips = labelChips(c.labelIds);
                    const firstCard = ci === 0;
                    const lastCard = ci === listCards.length - 1;
                    return `
            <li class="card-item" data-card-id="${c.id}" data-list-id="${l.id}">
              <button class="card-chip" data-action="open-card" data-card-id="${c.id}"
                aria-label="${escapeHtml(cardAria(c))}">
                ${chips ? `<span class="card-chip-labels">${chips}</span>` : ""}
                <span class="card-title">${escapeHtml(c.title)}</span>
                ${c.dueDate ? `<span class="card-due">${escapeHtml(c.dueDate)}</span>` : ""}
              </button>
              <div class="card-actions">
                <button data-action="card-up" data-card-id="${c.id}" ${firstCard ? "disabled" : ""}
                  aria-label="Move card up: ${escapeHtml(c.title)}">▲</button>
                <button data-action="card-down" data-card-id="${c.id}" ${lastCard ? "disabled" : ""}
                  aria-label="Move card down: ${escapeHtml(c.title)}">▼</button>
              </div>
            </li>`;
                  })
                  .join("");
```

- [ ] **Step 3: Handle the new clicks in `bindActions`**

In `board/view.js`, in the `click` listener inside `bindActions`, add these two lines immediately after the `if (action === "open-card") ...` line:

```javascript
      if (action === "card-up") return handlers.cardUp(Number(btn.dataset.cardId));
      if (action === "card-down") return handlers.cardDown(Number(btn.dataset.cardId));
```

- [ ] **Step 4: Add the `focusCardAction` helper + export it**

In `board/view.js`, add this function next to `focusListAction`:

```javascript
  function focusCardAction(cardId, preferred) {
    const item = root.querySelector(`.card-item[data-card-id="${cardId}"]`);
    if (!item) return;
    const order = preferred === "card-up" ? ["card-up", "card-down"] : ["card-down", "card-up"];
    for (const action of order) {
      const btn = item.querySelector(`button[data-action="${action}"]`);
      if (btn && !btn.disabled) { btn.focus(); return; }
    }
    const select = item.querySelector('select[data-action="card-move-to"]');
    if (select && !select.disabled) { select.focus(); return; }
    item.querySelector(".card-chip")?.focus();
  }
```

Then add `focusCardAction` to the returned object:

```javascript
  return { render, focusHeading, focusNewListInput, focusNewCardInput, focusCard, focusListAction, focusCardAction, bindActions };
```

- [ ] **Step 5: Wire the handlers in the controller**

In `board/controller.js`, add these two handlers to the `view.bindActions({ ... })` object (e.g. after `moveRight`):

```javascript
        cardUp: (cardId) => moveCard(cardId, -1, "card-up"),
        cardDown: (cardId) => moveCard(cardId, +1, "card-down"),
```

Then add this function next to the existing `move` function:

```javascript
    async function moveCard(cardId, delta, action) {
        const list = lists.find((l) => (l.cards ?? []).some((c) => c.id === cardId));
        if (!list) return;
        const cards = list.cards ?? [];
        const index = cards.findIndex((c) => c.id === cardId);
        const target = index + delta;
        if (target < 0 || target >= cards.length) return; // already at an end (button disabled)
        try {
            await model.moveCard(cardId, list.id, target);
            announce(delta < 0 ? "Card moved up." : "Card moved down.");
            view.focusCardAction(cardId, action);
        } catch {
            announce("Couldn't move the card — please try again.");
        }
    }
```

- [ ] **Step 6: Verify in the browser**

Run the app (`dotnet run --project Wend.Api`), open a board with a list of 3+ cards.
Expected: each card shows ▲ ▼; ▲ is disabled on the top card and ▼ on the bottom; clicking them reorders the card; the screen reader announces "Card moved up/down"; focus stays on the arrow you pressed (or the other arrow when one disables at an end).

- [ ] **Step 7: Commit**

```bash
git add Wend.Api/wwwroot/js/board/view.js Wend.Api/wwwroot/js/board/model.js Wend.Api/wwwroot/js/board/controller.js
git commit -m "Add up/down card move controls to the board"
```

---

## Task 6 (Henry): "Move to…" dropdown — board frontend

**Files:**
- Modify: `Wend.Api/wwwroot/js/board/view.js`
- Modify: `Wend.Api/wwwroot/js/board/controller.js`

- [ ] **Step 1: Render the select in the view**

In `board/view.js`, just before the `const cards = listCards.map(...)` line you added in Task 5, build the option list once per list:

```javascript
              const otherLists = lists.filter((t) => t.id !== l.id);
              const moveOptions = otherLists
                  .map((t) => `<option value="${t.id}">${escapeHtml(t.title)}</option>`)
                  .join("");
```

Then, inside the card's `<div class="card-actions"> ... </div>` (after the ▼ button), add the select:

```javascript
                <select class="card-move-to" data-action="card-move-to" data-card-id="${c.id}"
                  aria-label="Move card to another list: ${escapeHtml(c.title)}" ${otherLists.length ? "" : "disabled"}>
                  <option value="" selected disabled>Move to…</option>
                  ${moveOptions}
                </select>
```

- [ ] **Step 2: Handle the select change in `bindActions`**

In `board/view.js`, add a `change` listener inside `bindActions` (alongside the existing `submit` and `click` listeners):

```javascript
    root.addEventListener("change", (e) => {
      const sel = e.target.closest('select[data-action="card-move-to"]');
      if (!sel) return;
      const listId = Number(sel.value);
      if (!listId) return;
      handlers.moveCardTo(Number(sel.dataset.cardId), listId);
    });
```

- [ ] **Step 3: Wire `moveCardTo` in the controller**

In `board/controller.js`, add this handler to the `view.bindActions({ ... })` object (after `cardDown`):

```javascript
        moveCardTo: async (cardId, listId) => {
            const dest = lists.find((l) => l.id === listId);
            if (!dest) return;
            try {
                await model.moveCard(cardId, listId, (dest.cards ?? []).length); // append at the bottom
                announce(`Card moved to ${dest.title}.`);
                view.focusCardAction(cardId, "card-up");
            } catch {
                announce("Couldn't move the card — please try again.");
            }
        },
```

- [ ] **Step 4: Verify in the browser**

Run the app, open a board with 2+ lists and a few cards.
Expected: each card has a "Move to…" dropdown listing the *other* lists; choosing one moves the card to the bottom of that list; the screen reader announces "Card moved to {list}"; on a board with only one list the dropdown is disabled.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/wwwroot/js/board/view.js Wend.Api/wwwroot/js/board/controller.js
git commit -m "Add a move-to-list dropdown to each card"
```

---

## Task 7 (Malin): Style the card actions row

**Files:**
- Modify: `Wend.Api/wwwroot/css/app.css`

- [ ] **Step 1: Add the styles**

In `Wend.Api/wwwroot/css/app.css`, after the `.card-chip { ... }` rule, add:

```css
/* A card = its open button (title) plus a row of move controls. */
.card-item {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.card-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-wrap: wrap;
}

/* ≥44px touch targets, matching the label controls. */
.card-actions button,
.card-actions .card-move-to {
  min-height: 44px;
}

.card-move-to { max-width: 100%; }
```

- [ ] **Step 2: Verify, mobile-first**

Run the app; check a board at a narrow width (≈360px) and at desktop width.
Expected: the ▲ ▼ buttons and the dropdown sit on one row under each card, wrap cleanly when narrow, and every control is at least 44px tall. The board still reads as a vertical stack on mobile and as columns at ≥768px (unchanged).

- [ ] **Step 3: Commit**

```bash
git add Wend.Api/wwwroot/css/app.css
git commit -m "Style the card move controls"
```

---

## Task 8 (Henry): Acceptance pass + README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Keyboard + screen-reader acceptance**

With the app running, using only the keyboard and with a screen reader on, confirm:
- Tab reaches each card's ▲, ▼, and the "Move to…" select in order.
- ▲ / ▼ activate with Enter/Space and reorder the card; they are disabled (and skipped by Tab) at the list's top/bottom.
- The select is operable by keyboard and moves the card to the chosen list's bottom.
- Each action announces ("Card moved up/down." / "Card moved to {list}.") via the live region.
- After a move, focus lands on the moved card's controls (not lost to the page top).
- A single-list board disables the select; a single-card list disables ▲ and ▼.

Note any failures and fix in the relevant module before continuing.

- [ ] **Step 2: Update the README status + roadmap**

In `README.md`, mark card moving as shipped — add it to the working-features list and tick Plan 5 in the Slice 1 roadmap, mirroring how Plan 4 (labels) is recorded. Example feature line:

```markdown
- **Move cards** within a list (up/down buttons) and between lists (a "Move to…" dropdown) — keyboard-operable, no drag-and-drop.
```

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Document card moving in the README"
```

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin feature/card-moving
```

Open a PR to `main`; the other reviews and merges via a **merge commit (not squash)** — the multi-author convention that avoids an auto-added `Co-authored-by` trailer. Confirm CI (`Build & test`) is green first.

---

## Done when

- `dotnet test` is green at **105 tests**, 0 warnings.
- A card can be reordered within its list and moved to any other list on the board, by keyboard, with announcements and sensible focus.
- The board still renders as a mobile stack / desktop columns, and no database reset was needed.
- Plan 5 is ticked in the README roadmap; the branch is merged to `main` via a merge commit.
