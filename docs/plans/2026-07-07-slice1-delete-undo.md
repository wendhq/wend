# Wend — Plan 7: Soft-delete + undo (implementation plan)

**Date:** 2026-07-07
**Spec:** `docs/2026-07-07-wend-delete-undo-design.md` (stress-tested)
**Slice:** 1, Plan 7 of 8. Turn-based build with Henry; per-human commits, no AI attribution.

**Goal:** Deleting a card soft-deletes it and offers a transient "Deleted · Undo" toast that
restores it to its original spot.

**Architecture:** Flip `DeleteCardAsync` to set `Card.DeletedAt` (the EF query filter already
hides it); add `RestoreCardAsync` + `POST /api/cards/{id}/restore`; harden the card GET; add a
reusable `toast.js` shell primitive wired by the coordinator. **No schema change, no DB reset.**

**Backend is TDD** (red → green per task). **Frontend is browser-verified.** Suggested drivers
alternate Malin / Henry — swap freely.

## Before you start

```powershell
git switch main; git pull; git switch -c feature/delete-undo
```

- Stop any running `Wend.exe` / `dotnet run` before `dotnet test` (it locks the DLL).
- Driver **pushes** at end of turn (`git push -u origin feature/delete-undo`); next driver
  **fetches + switches** at start (`git fetch && git switch feature/delete-undo`).
- No `data.db` reset — `DeletedAt` already exists on the row.

---

## Task 1 — soft-delete cards · *Malin*

**Files:** modify `Wend.Core/EfCardRepository.cs`; test `Wend.Tests/CardRepositoryTests.cs`.

- [ ] **Step 1 — failing tests** (add to `CardRepositoryTests.cs`):

```csharp
[Test]
public async Task Delete_soft_deletes_so_the_row_survives_for_undo()
{
    var listId = await NewListAsync();
    var card = await _repo.CreateCardAsync(listId, "Temp");

    Assert.That(await _repo.DeleteCardAsync(card.Id), Is.True);

    // Hidden from normal queries…
    Assert.That(await _repo.GetCardsForListAsync(listId), Is.Empty);
    // …but the row still exists with DeletedAt set, so undo can bring it back.
    var row = await _db.Cards.IgnoreQueryFilters().SingleAsync(c => c.Id == card.Id);
    Assert.That(row.DeletedAt, Is.Not.Null);
}

[Test]
public async Task Deleting_an_already_deleted_card_reports_missing()
{
    var listId = await NewListAsync();
    var card = await _repo.CreateCardAsync(listId, "Temp");
    await _repo.DeleteCardAsync(card.Id);

    Assert.That(await _repo.DeleteCardAsync(card.Id), Is.False);
}
```

- [ ] **Step 2 — run, expect RED:** `dotnet test --filter "FullyQualifiedName~CardRepositoryTests"`
  (`Delete_soft_deletes…` fails — the current hard delete removes the row).

- [ ] **Step 3 — replace `DeleteCardAsync`** in `EfCardRepository.cs`:

```csharp
public async Task<bool> DeleteCardAsync(int id)
{
    var card = await db.Cards.FindAsync(id);
    if (card is null || card.DeletedAt is not null) return false; // missing or already gone
    card.DeletedAt = DateTime.UtcNow;   // soft delete — the row survives for undo
    await db.SaveChangesAsync();
    await ResequenceAsync(card.ListId); // close the gap among the survivors (filter hides this card)
    return true;
}
```

- [ ] **Step 4 — run, expect GREEN.** Existing `Delete_removes_the_card_and_resequences_the_rest`
  and API `Delete_removes_a_card` stay green (the filter hides the row).

- [ ] **Step 5 — commit:**

```powershell
git add Wend.Core/EfCardRepository.cs Wend.Tests/CardRepositoryTests.cs
git commit -m "Plan 7 Task 1 — soft-delete cards (DeletedAt)"
git push -u origin feature/delete-undo
```

---

## Task 2 — restore to original position · *Henry*

**Files:** modify `Wend.Core/ICardRepository.cs`, `Wend.Core/EfCardRepository.cs`; test
`Wend.Tests/CardRepositoryTests.cs`.

- [ ] **Step 1 — failing tests:**

```csharp
[Test]
public async Task Restore_brings_a_deleted_card_back_to_its_original_position()
{
    var listId = await NewListAsync();
    await _repo.CreateCardAsync(listId, "A");          // 0
    var b = await _repo.CreateCardAsync(listId, "B");  // 1
    await _repo.CreateCardAsync(listId, "C");          // 2

    await _repo.DeleteCardAsync(b.Id);                 // survivors resequence to A(0), C(1)
    Assert.That(await _repo.RestoreCardAsync(b.Id), Is.True);

    var cards = await _repo.GetCardsForListAsync(listId);
    Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "A", "B", "C" }));
    Assert.That(cards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless, B back in the middle
}

[Test]
public async Task Restore_is_idempotent_for_a_card_that_is_not_deleted()
{
    var listId = await NewListAsync();
    var card = await _repo.CreateCardAsync(listId, "Here");

    Assert.That(await _repo.RestoreCardAsync(card.Id), Is.True); // no-op, still reports found
    Assert.That((await _repo.GetCardsForListAsync(listId)).Single().Title, Is.EqualTo("Here"));
}

[Test]
public async Task Restore_reports_a_missing_card()
{
    Assert.That(await _repo.RestoreCardAsync(9999), Is.False);
}

[Test]
public async Task Restoring_a_done_card_keeps_it_done()
{
    var listId = await NewListAsync();
    var card = await _repo.CreateCardAsync(listId, "Shipped");
    await _repo.SetCardCompletedAsync(card.Id, true);
    await _repo.DeleteCardAsync(card.Id);

    Assert.That(await _repo.RestoreCardAsync(card.Id), Is.True);
    var row = await _db.Cards.IgnoreQueryFilters().SingleAsync(c => c.Id == card.Id);
    Assert.That(row.DeletedAt, Is.Null);
    Assert.That(row.CompletedAt, Is.Not.Null); // still done after undo
}
```

- [ ] **Step 2 — run, expect RED** (`RestoreCardAsync` doesn't exist → doesn't compile).

- [ ] **Step 3 — add to `ICardRepository.cs`**, below `DeleteCardAsync`:

```csharp
    Task<bool> RestoreCardAsync(int id);
```

- [ ] **Step 4 — add to `EfCardRepository.cs`**, below `DeleteCardAsync` (mirrors the within-list
  reorder in `MoveCardAsync`: lift → clamp → insert → renumber):

```csharp
public async Task<bool> RestoreCardAsync(int id)
{
    var card = await db.Cards.FindAsync(id);   // FindAsync bypasses the filter, so it sees the deleted row
    if (card is null) return false;
    if (card.DeletedAt is null) return true;   // already active — idempotent no-op

    var siblings = await db.Cards.Where(c => c.ListId == card.ListId)
        .OrderBy(c => c.Position)
        .ToListAsync();                        // active siblings only (the card is still filtered out)
    card.DeletedAt = null;
    var index = Math.Clamp(card.Position, 0, siblings.Count); // its old spot, bounded to the list today
    siblings.Insert(index, card);
    for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
    await db.SaveChangesAsync();
    return true;
}
```

- [ ] **Step 5 — run, expect GREEN.**
- [ ] **Step 6 — commit** (`Plan 7 Task 2 — restore cards to their original position`) + push.

---

## Task 3 — hide a soft-deleted card from GET · *Malin*

**Files:** modify `Wend.Core/EfCardRepository.cs`; test `Wend.Tests/CardRepositoryTests.cs`.

- [ ] **Step 1 — failing test:**

```csharp
[Test]
public async Task Get_card_hides_a_soft_deleted_card()
{
    var listId = await NewListAsync();
    var card = await _repo.CreateCardAsync(listId, "Temp");
    await _repo.DeleteCardAsync(card.Id);

    Assert.That(await _repo.GetCardAsync(card.Id), Is.Null);
}
```

- [ ] **Step 2 — run, expect RED** (`GetCardAsync` uses `FindAsync`, which bypasses the filter).

- [ ] **Step 3 — replace `GetCardAsync`** in `EfCardRepository.cs`:

```csharp
public async Task<Card?> GetCardAsync(int id) =>
    await db.Cards.FirstOrDefaultAsync(c => c.Id == id); // goes through the filter → deleted cards read as gone
```

- [ ] **Step 4 — run, expect GREEN** (`Get_card_returns_it_or_null` + the completed-card tests
  stay green — they use live cards).
- [ ] **Step 5 — commit** (`Plan 7 Task 3 — hide soft-deleted cards from GET`) + push.

---

## Task 4 — the restore endpoint · *Henry*

**Files:** modify `Wend.Api/CardEndpoints.cs`; test `Wend.Tests/CardApiTests.cs`.

- [ ] **Step 1 — failing tests** (add to `CardApiTests.cs`):

```csharp
[Test]
public async Task Deleting_then_restoring_a_card_brings_it_back()
{
    var board = await CreateBoardAsync("B");
    var list = await CreateListAsync(board.Id, "L");
    var card = await CreateCardAsync(list.Id, "Temp");

    await _client.DeleteAsync($"/api/cards/{card.Id}");
    var afterDelete = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");
    Assert.That(afterDelete!.Lists.Single().Cards, Is.Empty);

    var restore = await _client.PostAsync($"/api/cards/{card.Id}/restore", null);
    Assert.That(restore.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

    var afterRestore = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");
    Assert.That(afterRestore!.Lists.Single().Cards.Select(c => c.Title), Is.EqualTo(new[] { "Temp" }));
}

[Test]
public async Task Restoring_a_missing_card_is_404()
{
    var res = await _client.PostAsync("/api/cards/9999/restore", null);
    Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
}

[Test]
public async Task A_soft_deleted_card_is_404_on_get()
{
    var board = await CreateBoardAsync("B");
    var list = await CreateListAsync(board.Id, "L");
    var card = await CreateCardAsync(list.Id, "Temp");
    await _client.DeleteAsync($"/api/cards/{card.Id}");

    var res = await _client.GetAsync($"/api/cards/{card.Id}");
    Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
}
```

- [ ] **Step 2 — run, expect RED** (no restore route yet).

- [ ] **Step 3 — add to `CardEndpoints.cs`**, right after the `MapDelete` line:

```csharp
        app.MapPost("/api/cards/{id:int}/restore", async (int id, ICardRepository cards) =>
            await cards.RestoreCardAsync(id) ? Results.NoContent() : Results.NotFound());
```

- [ ] **Step 4 — run, expect GREEN.** Backend done: **+10 tests, 0 warnings.**
- [ ] **Step 5 — commit** (`Plan 7 Task 4 — POST /api/cards/{id}/restore`) + push.

---

## Task 5 — the undo toast primitive · *Malin*

**Files:** create `Wend.Api/wwwroot/js/toast.js`; modify `Wend.Api/wwwroot/index.html`,
`Wend.Api/wwwroot/css/app.css`. Browser-verified (no unit tests).

- [ ] **Step 1 — create `wwwroot/js/toast.js`** (a shell widget like `announce.js`, not an MVC trio):

```javascript
// A single transient toast in the shell's #toast-region: a message + one action (Undo) + a
// dismiss button. Auto-dismisses after a timeout that PAUSES while the pointer is over it or
// keyboard focus is inside it, so it can't vanish before a keyboard / screen-reader user
// reaches the action. A new show() replaces the current toast. No business logic — the caller
// supplies the message and the callbacks.
const TIMEOUT_MS = 8000;

export function createToast(region) {
  let current = null; // { el, remaining, startedAt, timerId }

  function clearTimer() {
    if (current?.timerId) { clearTimeout(current.timerId); current.timerId = null; }
  }
  function startTimer(ms) {
    clearTimer();
    current.startedAt = Date.now();
    current.remaining = ms;
    current.timerId = setTimeout(dismiss, ms);
  }
  function pause() {
    if (!current?.timerId) return;
    clearTimer();
    current.remaining -= Date.now() - current.startedAt; // bank the elapsed time
  }
  function resume() {
    if (!current || current.timerId) return;
    startTimer(Math.max(current.remaining, 0));
  }
  function dismiss() {
    if (!current) return;
    clearTimer();
    current.el.remove();
    current = null;
  }

  function show({ message, actionLabel, onAction, onDismissFocus }) {
    dismiss(); // one toast at a time — replace any current one

    const el = document.createElement("div");
    el.className = "toast toast-info";
    el.setAttribute("role", "group");
    el.setAttribute("aria-label", "Deleted card");

    const text = document.createElement("span");
    text.className = "toast-message";
    text.textContent = message; // user title → textContent, never innerHTML

    const action = document.createElement("button");
    action.type = "button";
    action.className = "toast-action";
    action.textContent = actionLabel;
    action.addEventListener("click", () => { dismiss(); onAction?.(); }); // caller moves focus to the restored card

    const close = document.createElement("button");
    close.type = "button";
    close.className = "toast-dismiss";
    close.setAttribute("aria-label", "Dismiss");
    close.textContent = "×";
    close.addEventListener("click", () => { dismiss(); onDismissFocus?.(); }); // hand focus back to the board heading

    el.append(text, action, close);
    el.addEventListener("mouseenter", pause);
    el.addEventListener("mouseleave", resume);
    el.addEventListener("focusin", pause);
    el.addEventListener("focusout", (e) => { if (!el.contains(e.relatedTarget)) resume(); });

    region.append(el);
    current = { el, remaining: TIMEOUT_MS, startedAt: 0, timerId: null };
    startTimer(TIMEOUT_MS);
  }

  return { show, dismiss };
}
```

- [ ] **Step 2 — `index.html`:** add the region between `<header>` and `<main id="app">`:

```html
  <header class="app-header"><h1>Wend</h1></header>

  <!-- Transient "Deleted · Undo" toast. Fixed at the bottom via the design-system .toast-region,
       but placed BEFORE #app in the DOM so its Undo button sits near the front of the tab order —
       one Shift+Tab back from the board heading. Outside #app so a re-render never wipes it. -->
  <div id="toast-region" class="toast-region"></div>

  <main id="app" tabindex="-1"></main>
```

- [ ] **Step 3 — `css/app.css`:** append (the DS `.toast` / `.toast-region` already give surface,
  shadow, radius, reduced-motion — this is layout + touch targets only):

```css
/* ---- Undo toast (uses the design-system .toast-region / .toast component) ---- */
.toast { align-items: center; }
.toast-message { flex: 1; }
.toast-action,
.toast-dismiss { min-height: 44px; }
.toast-dismiss { min-width: 44px; }
```

- [ ] **Step 4 — commit** (`Plan 7 Task 5 — undo toast primitive`) + push. Nothing wired yet, so no
  visible change — that's Task 6.

---

## Task 6 — wire delete → toast → undo · *Henry*

**Files:** modify `Wend.Api/wwwroot/js/main.js`, `Wend.Api/wwwroot/js/card/controller.js`.

- [ ] **Step 1 — `main.js` imports** (top of file):

```javascript
import { api } from "./api.js";
import { createToast } from "./toast.js";
```

- [ ] **Step 2 — `main.js`** create the toast next to the announcer:

```javascript
const toast = createToast(document.getElementById("toast-region"));
```

- [ ] **Step 3 — `main.js`** replace `showCard` and add `undoDelete` beneath it:

```javascript
function showCard(cardId, boardId) {
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
    });
    model.load().then(() => view.focusHeading());
  });
}

async function undoDelete(cardId, title, boardId) {
  try {
    await api(`/api/cards/${cardId}/restore`, { method: "POST" });
    announce(`Restored: ${title}.`);
    showBoard(boardId, cardId); // re-mount the board and focus the restored card
  } catch {
    announce("Couldn't restore the card — please try again.");
  }
}
```

- [ ] **Step 4 — `card/controller.js`** track the loaded card and pass its id + title to `onDeleted`.
  Add `let current = null;` under `let palette = [];`:

```javascript
export function createCardController(model, view, announce, { onBack, onDeleted } = {}) {
  let palette = [];
  let current = null; // latest loaded card — for its id + title on delete
  const nameOf = (id) => (palette.find((l) => l.id === id) || {}).name || "the label";
```

  Replace the `delete` handler:

```javascript
    delete: async () => {
      try {
        await model.remove();
        onDeleted?.(current.id, current.title); // coordinator navigates + shows the undo toast + announces
      } catch {
        announce("Couldn't delete the card — please try again.");
      }
    },
```

  Set `current` in the `subscribe` at the bottom:

```javascript
  model.subscribe((card, p) => {
    current = card;
    palette = p ?? [];
    view.render(card, p);
  });
```

- [ ] **Step 5 — commit** (`Plan 7 Task 6 — wire delete to the undo toast`) + push.

---

## Task 7 — acceptance + README · *both*

- [ ] **Run** `dotnet run --project Wend.Api`, then walk the keyboard / screen-reader script:
  1. Open a card → **Delete card** → land back on the board, card gone, toast "Deleted: <title>",
     SR hears "Deleted: <title>. Undo available."
  2. From the board heading, **Shift+Tab once** → focus lands on **Undo** → activate → the card
     returns to its original spot and takes focus; SR hears "Restored: <title>."
  3. Delete again; hover the toast / keep focus in it → it does **not** auto-dismiss; move away → it
     dismisses after ~8s. Press **×** → focus returns to the board heading.
  4. Delete two cards quickly → only the second toast shows (first card stays gone — the
     "one toast replaces" call).
- [ ] **README** — bump Slice 1 status to **7/8 (delete + undo)**.
- [ ] **Commit** (`Plan 7 Task 7 — README + acceptance`), open the PR, **merge-not-squash**
  (multi-author → squash would auto-add a `Co-authored-by` trailer).

## Out of scope (later)

Trash screen (durable recovery + empty) · list/board soft-delete · board-chip delete · Archive.
