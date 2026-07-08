# Plan 8 — Mobile + A11y Polish Implementation Plan

> **Build mode — Claude solo** (2026-07-07 delegation split). Claude drives every task, commits per task **as Malin** (no AI attribution), and self-verifies each in a real browser via the Chrome extension (the same sweep that found these). Malin & Henry review the PR and run the **keyboard + screen-reader acceptance** before merge. No coached turn-based handoff this plan.
>
> **Verification is browser-first.** Every task ends with a concrete in-browser check (measure the size, read the contrast, inspect `activeElement`, read the a11y tree). **Hard-reload / disable cache before each check** — `UseStaticFiles` serves stale JS otherwise (it bit the sweep on 2026-07-08).

**Goal:** Ship the final Slice-1 increment — the ten accessibility findings from the 2026-07-08 live sweep (Phase 1, ready now) plus the mobile single-list switcher and per-list Done strip (Phase 2, after the shared brainstorm).

**Architecture:** Frontend-only. No backend, no API, no schema, no NUnit change — every fix lives in `Wend.Api/wwwroot/` (CSS + the vanilla-JS MVC views/controllers). Fixes follow patterns already in the codebase (the card/checklist inline-rename, the label-toggle refocus contract, the escape-at-every-`innerHTML` rule).

**Tech Stack:** vanilla-JS MVC (no build step) · CSS (mobile-first, design-system tokens) · ASP.NET Core static hosting (untouched).

**Spec:** [`docs/2026-07-08-wend-mobile-a11y-polish-design.md`](../2026-07-08-wend-mobile-a11y-polish-design.md) (stress-tested; 8 fixes folded).

**Test count:** stays **147 green, 0 warnings** throughout — backend is untouched. Run `dotnet test` once at the end to confirm nothing regressed. (Stop `Wend.Api` before the test run — the DLL lock, not a test failure.)

---

## Before Task 1 — branch + docs commit

```powershell
git switch main; git pull
git switch -c feature/mobile-a11y-polish
git add docs/2026-07-08-wend-mobile-a11y-polish-design.md docs/plans/2026-07-08-slice1-mobile-a11y-polish.md
git commit -m "Plan 8 docs — mobile + a11y polish spec + build plan"
git push -u origin feature/mobile-a11y-polish
```

No `data.db` reset this plan (frontend-only). Keep the seeded sweep board around — it exercises every finding.

---

## Phase 1 — a11y findings (ready to build)

### Task 1 — CSS: target sizes (F2), Done-strip contrast (F3), confirm string (F7)

**Files:** `css/app.css`, `js/card/checklist.js`, `js/card/controller.js`

- **F2** — grow the two sub-24px checkboxes to a ≥24px box (rows are already 44px tall):
  ```css
  .card-done-toggle { width: 1.5rem; height: 1.5rem; }               /* was 1.15rem (18px) */
  .checklist-row input[type="checkbox"],
  .done-item-row input[type="checkbox"] { width: 1.5rem; height: 1.5rem; }  /* was 13px default */
  ```
- **F3** — rename **only** the checklist strip's colliding class in `checklist.js` (`class="done-row-label"` → `class="checklist-done-label"`) and add a matching CSS rule; **delete** the `.done-row-label` opacity from the strip's chain and dim with a **single** step measured ≥4.5:1 (mirror the board area's `0.7`, proven 5.36:1). Leave `board/view.js` + `app.css:314`'s `.done-row-label` untouched.
- **F7** — collapse the label-delete `confirm()` string in `card/controller.js` to one line.

**Verify (browser):** re-measure — both checkboxes report ≥24px; the Done-strip text composites to ≥4.5:1 against `#0D1117`; the board Done area still measures 5.36:1 (unbroken); the confirm dialog reads cleanly.

**Commit:** `Plan 8 Task 1 — a11y CSS: 24px targets, Done-strip contrast, confirm string`

---

### Task 2 — Focus management (F1, F10)

**Files:** `js/board/view.js`, `js/settings/view.js`, `js/settings/controller.js`

- **F1** — in the `toggle-done-section` click branch, call `focusDoneToggle()` after `paint()` (helper exists).
- **F10** — add a `focusPref(key)` helper to the settings view (`root.querySelector(\`input[data-pref="${key}"]\`)?.focus()`) and call it from the controller's `toggle` handler after `model.set`.
- **Both** — if the refocus target is ever absent, fall back to the screen heading rather than letting `?.focus()` drop to `<body>`.

**Verify (browser):** after toggling the board Done area and after flipping each Settings checkbox, `document.activeElement` is the toggle/checkbox (not `BODY`) — the exact check that caught F1/F10.

**Commit:** `Plan 8 Task 2 — focus stays on the toggle after Done-area / settings re-render`

---

### Task 3 — Structure & semantics (F6, F8)

**Files:** `js/board/view.js`, `js/card/view.js`, `css/app.css`

- **F6** — each list becomes a labelled region with the heading as its label (one announcement, both landmark + heading nav):
  ```html
  <li class="list-card" data-list-id="${l.id}" role="group" aria-labelledby="list-${l.id}-title">
    <h3 id="list-${l.id}-title" class="list-title">${escapeHtml(l.title)}</h3>
    …
  ```
  (If a screen reader objects to `role="group"` on an `<li>` inside `<ul>`, wrap the content in a `<section>` instead — decide at build from the a11y tree.)
- **F8** — restructure the card heading so the `<h2>` **keeps its name** and the rename form is a sibling: normal mode → `<h2>` holds the rename-trigger button; rename mode → `<h2>` holds the title as text, with the `<form>` rendered *after* the `</h2>`, never inside it.

**Verify (browser):** the a11y tree shows each list as a `group`/`region` with its title; heading-jump reaches every list. While renaming a card title, no `<form>` is a descendant of the `<h2>` and the heading still exposes the title as its name.

**Commit:** `Plan 8 Task 3 — lists as labelled regions; card rename form out of the h2`

---

### Task 4 — ARIA & announcements (F9)

**Files:** `js/card/labels.js`, `js/settings/view.js`, `js/announce.js`

- `aria-haspopup="true"` → `"dialog"` on the Labels toggle (or drop it — `aria-expanded` already conveys the disclosure).
- Give each `.setting-hint` an `id` and each Settings checkbox `aria-describedby` pointing at it.
- `announce.js`: swap the rAF for a short `setTimeout` so the message fires regardless of tab visibility:
  ```js
  return (message) => {
    region.textContent = "";
    setTimeout(() => { region.textContent = message; }, 120);
  };
  ```

**Verify (browser):** picker toggle reports `aria-haspopup="dialog"`; each settings checkbox has `aria-describedby` resolving to its hint; `#status` receives the message without relying on rAF (the hidden-tab drop no longer happens).

**Commit:** `Plan 8 Task 4 — picker haspopup, settings describedby, reliable announcer`

---

### Task 5 — Toast timing (F4) — *after Task 4*

**Files:** `js/toast.js`

- Lengthen the auto-dismiss window (`TIMEOUT_MS` 8000 → **12000**), keeping the pause-on-hover/focus. (Alternative considered: don't start the timer until first interaction — heavier, and a mouse user who ignores it never sees it dismiss. Bump-and-pause is the pragmatic call; revisit if acceptance says it's still tight.)
- Depends on Task 4: the reliable "Undo available" announcement is what a screen-reader user hears first.

**Verify (browser):** delete a card, confirm the window is ≥12s and still pauses while focus is inside; the Undo→restore round-trip still lands focus on the restored chip.

**Commit:** `Plan 8 Task 5 — longer undo-toast window for AT users`

---

### Task 6 — Inline rename for boards & lists (F5)

**Files:** `js/boards/view.js`, `js/boards/controller.js`, `js/board/view.js`, `js/board/controller.js`

Replace the two `prompt()` calls with the card/checklist inline-rename pattern. Per trio:

- View gains `ui = { renamingId: null }` (survives `paint()`); the title renders as a rename-trigger button (`aria-label="Rename: ${escapeHtml(title)}"`), swapping to a `<form>` with `<input value="${escapeHtml(title)}">` when `renamingId` matches.
- Enter/Save commits (→ `model.rename`), Esc cancels and returns focus to the trigger, **blur leaves it open** (matches cards — no silent auto-commit).
- `escapeHtml` at both the input `value` and the trigger `aria-label` (new interpolation sites).
- Delete the `prompt()` branches in both controllers.

**Verify (browser):** rename a board and a list inline — text→input swap, Enter saves, Esc cancels with focus back on the trigger; a title containing `"` / `<` round-trips without breaking markup (escape check).

**Commit:** `Plan 8 Task 6 — inline rename for boards and lists (replaces prompt)`

---

## Phase 2 — mobile layout (decided 2026-07-08 — locked with Malin)

Decisions in the spec's **Mobile layout §**: native `<select>` switcher, **per-list Done strip only** (the global Done area is retired — revises "Done = both"), selection remembered per board in `localStorage`, no swipe, stale → first list, announce on switch. Order: Task 7 (per-list strip + retire global area) → Task 8 (switcher + persistence), because the switcher shows one `.list-card` at a time and each card now owns its Done strip.

### Before Task 7 — Phase-2 branch + docs commit

Phase 1's `feature/mobile-a11y-polish` was merged (PR #25) and **deleted** — Phase 2 starts on a fresh branch off latest `main`.

```powershell
git switch main; git pull
git switch -c feature/mobile-switcher
git add docs/2026-07-08-wend-mobile-a11y-polish-design.md docs/plans/2026-07-08-slice1-mobile-a11y-polish.md
git commit -m "Plan 8 Phase 2 docs — mobile switcher + per-list Done decisions"
```

(Push happens once, with the PR at the end — Malin greenlights the push.)

### Task 7 — Per-list Done strip; retire the global Done area

**Files:** `js/board/view.js`, `js/board/controller.js`, `css/app.css`

Done cards move from the single global area into a collapsible strip **inside each list**, mirroring the checklist Done strip ([checklist.js:39](../Wend.Api/wwwroot/js/card/checklist.js:39)) and reusing its already-AA-safe classes (`.done-strip`, `.done-toggle`, `.done-items`, `.done-item-row`, `.checklist-done-label`, `.done-item-text` — F2 sizes the checkbox to 24px; F3 gives the text a single-`0.7` dim ≈ 5.36:1), so **no new strip CSS** is needed.

- **view.js — per-list collapse state.** `const ui = { doneOpen: false, renamingId: null };` → `const ui = { doneOpenLists: new Set(), renamingId: null };` (each strip opens independently; view-local, resets on the fresh mount).
- **view.js — render the strip inside each list**, between the active-cards `<ul class="card-list">…</ul>` and the add-card `<form class="card-form">`:
  ```js
  const doneCards = (l.cards ?? []).filter((c) => c.completedAt);
  const doneOpen = ui.doneOpenLists.has(l.id);
  const doneStrip = doneCards.length ? `
    <div class="done-strip">
      <button type="button" class="done-toggle" data-action="toggle-list-done" data-list-id="${l.id}"
        aria-expanded="${doneOpen ? "true" : "false"}">✓ Done (${doneCards.length})</button>
      ${doneOpen ? `<ul class="done-items">${doneCards
        .map((c) => `
        <li class="done-item-row" data-card-id="${c.id}">
          <label class="checklist-done-label">
            <input type="checkbox" data-action="toggle-done" data-card-id="${c.id}" checked
              aria-label="Mark not done: ${escapeHtml(c.title)}" />
            <span class="done-item-text">${escapeHtml(c.title)}</span>
          </label>
        </li>`)
        .join("")}</ul>` : ""}
    </div>` : "";
  ```
  (No `card-done-toggle` class on the strip checkbox — the classless `.done-item-row input[type="checkbox"]` rule already sizes it to 24px, matching the checklist strip and avoiding the old `.done-row .card-done-toggle` margin fix.)
- **view.js — remove the global area.** Delete the `doneGroups` / `doneCount` / `doneArea` block and the `${doneArea}` in the shell; the shell now ends at `<ul class="list-columns">${items}</ul>`.
- **view.js — per-list focus helper + click branch.** Replace `focusDoneToggle` and its `toggle-done-section` handler:
  ```js
  function focusListDoneToggle(listId) {
    const t = root.querySelector(`.list-card[data-list-id="${listId}"] .done-toggle`);
    if (t) t.focus();
    else focusHeading(); // never strand focus on <body>
  }
  ```
  ```js
  if (action === "toggle-list-done") {
    const lid = Number(btn.dataset.listId);
    ui.doneOpenLists.has(lid) ? ui.doneOpenLists.delete(lid) : ui.doneOpenLists.add(lid);
    paint();
    focusListDoneToggle(lid);
    return;
  }
  ```
  Swap `focusDoneToggle` → `focusListDoneToggle` in the returned object.
- **controller.js — retarget the done-toggle focus.** `toggleDone` calls `view.focusDoneToggle()` on complete; find the card's list and focus that list's strip toggle instead:
  ```js
  toggleDone: async (cardId, completed) => {
    const list = lists.find((l) => (l.cards ?? []).some((c) => c.id === cardId));
    const card = list?.cards?.find((c) => c.id === cardId);
    const title = card ? card.title : "the card";
    try {
      await model.setCardDone(cardId, completed);
      if (completed) {
        announce(`Marked done: ${title}.`);
        if (list) view.focusListDoneToggle(list.id); else view.focusHeading();
      } else {
        announce(`Restored: ${title}.`);
        view.focusCard(cardId);
      }
    } catch {
      announce("Couldn't update the card — please try again.");
    }
  },
  ```
- **app.css — delete the dead global-area rules** ([app.css:300](../Wend.Api/wwwroot/css/app.css:300)): `.done-area`, `.done-body`, `.done-group-title`, `.done-list`, `.done-row-label`, `.done-row-title`, `.done-row .card-done-toggle`. **Keep `.done-toggle`** (shared by the checklist strip and now the per-list board strip). Update the section comment to say `.done-toggle` is the shared strip disclosure.

**Verify (browser):** mark an active card done → it leaves the list's active cards and appears under that list's "✓ Done (n)" strip; `document.activeElement` is that list's `.done-toggle` (not `BODY`). Expand → the title is struck through and composites ≥ 4.5:1 against `#0D1117`; the checkbox measures ≥ 24px. Un-check → the card returns to active; un-checking the last done card removes the strip and focus lands on the card. No global Done area at the board bottom.

**Commit:** `Plan 8 Task 7 — per-list Done strips; retire the global Done area`

### Task 8 — Single-list switcher + remembered selection

**Files:** `js/prefs.js`, `js/board/view.js`, `js/board/controller.js`, `css/app.css`

Narrow screens (`< 768px`) show one list at a time behind a native `<select>`; the selection is remembered per board. At `≥ 768px` the switcher hides and all columns show (current behaviour). Each list is already a labelled `role="group"` region — the switcher just picks which one is visible.

- **prefs.js — remembered selection (new key, validated reads).** Append:
  ```js
  const SELECTION_KEY = "wend.board.selection";

  // Remembered "currently viewed" list per board (mobile switcher). Reads validate to a number;
  // anything else → null so the caller falls back to the first list. Keyed by board id.
  export function getSelectedListId(boardId) {
    let map = null;
    try { map = JSON.parse(localStorage.getItem(SELECTION_KEY) ?? "null"); } catch { /* corrupted → null */ }
    const v = map && typeof map === "object" ? map[boardId] : null;
    return typeof v === "number" ? v : null;
  }

  export function setSelectedListId(boardId, listId) {
    let map = null;
    try { map = JSON.parse(localStorage.getItem(SELECTION_KEY) ?? "null"); } catch { /* reset on corruption */ }
    if (!map || typeof map !== "object") map = {};
    map[boardId] = listId;
    localStorage.setItem(SELECTION_KEY, JSON.stringify(map));
  }
  ```
- **view.js — import + state.** `import { getPrefs, getSelectedListId } from "../prefs.js";` and add `selectedListId: null` to `ui`.
- **view.js — resolve the selection each paint** (first paint, remembered value, and a deleted/stale list handled in one place). After `const lists = board.lists;` in `paint()`:
  ```js
  const validIds = new Set(lists.map((l) => l.id));
  if (!validIds.has(ui.selectedListId)) {
    const remembered = getSelectedListId(board.id);
    ui.selectedListId = validIds.has(remembered) ? remembered : (lists[0]?.id ?? null);
  }
  ```
- **view.js — mark the current list + render the switcher.** Add `${l.id === ui.selectedListId ? " is-current" : ""}` to each `.list-card` class. Build the switcher and place it in the shell between the add-list form and `<ul class="list-columns">`:
  ```js
  const switcher = lists.length ? `
    <div class="list-switcher-row">
      <label class="list-switcher-label" for="list-switcher">List</label>
      <select class="list-switcher" id="list-switcher" data-action="switch-list">
        ${lists.map((l) => `<option value="${l.id}"${l.id === ui.selectedListId ? " selected" : ""}>${escapeHtml(l.title)}</option>`).join("")}
      </select>
    </div>` : "";
  ```
- **view.js — handle the change + keep focus.** In the existing `change` listener (after the `toggle-done` check, before `card-move-to`):
  ```js
  const sw = e.target.closest('select[data-action="switch-list"]');
  if (sw) {
    ui.selectedListId = Number(sw.value);
    paint();
    root.querySelector(".list-switcher")?.focus(); // repaint re-creates the select — refocus it
    return handlers.selectList(ui.selectedListId);
  }
  ```
- **controller.js — persist + announce.** Add `setSelectedListId` to the prefs import, capture the board id, add the handler:
  ```js
  // top of createBoardController:
  let boardId = null;
  // model.subscribe:
  model.subscribe((board) => { lists = board.lists; boardId = board.id; view.render(board); });
  // new handler:
  selectList: (listId) => {
    const list = lists.find((l) => l.id === listId);
    if (!list) return;
    setSelectedListId(boardId, listId);
    const count = (list.cards ?? []).filter((c) => !c.completedAt).length;
    announce(`Showing ${list.title}, ${count} ${count === 1 ? "card" : "cards"}.`);
  },
  ```
- **app.css — switcher styles (mobile-first).** Before the `@media (min-width: 768px)` block at [app.css:41](../Wend.Api/wwwroot/css/app.css:41):
  ```css
  /* Mobile single-list switcher: view one list at a time; all columns show at ≥768px. */
  .list-switcher-row { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.75rem; }
  .list-switcher-label { font-weight: 600; }
  .list-switcher { flex: 1; min-height: 44px; max-width: 100%; }
  .list-columns > .list-card { display: none; }
  .list-columns > .list-card.is-current { display: flex; }
  ```
  Inside that `@media (min-width: 768px)` block add:
  ```css
  .list-switcher-row { display: none; }
  .list-columns > .list-card { display: flex; } /* every column visible on desktop */
  ```

**Verify (browser):** at 375px only the current list shows with the `<select>` above; picking another option swaps the visible list, keeps focus on the select, and `#status` announces "Showing <list>, <n> cards". Switch to list B → open a card → Back: still on B; reload: still B. Delete the current list → falls back to the first list (no blank). Resize ≥ 768px → switcher hidden, all columns show.

**Commit:** `Plan 8 Task 8 — mobile single-list switcher with remembered selection`

### Task 9 — Acceptance, README, backlog

**Files:** `README.md`, `docs/backlog.md`, plus a full test run.

- **Test run** (backend untouched — the gate is "still green"). Stop any running server first (`Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force`), then `dotnet test` → expect **147 passed, 0 warnings**.
- **README.md — Status.** Line 13 "(in progress)" → "(complete)". Replace the "Next" bullet (line 18) with a Plan-8 "Done" line + a Slice-2 "Next":
  - `- **Done (Plan 8):** mobile + accessibility polish — per-list Done strips, a single-list phone switcher (remembered per board), 24px touch targets, inline board/list rename, and focus/announcement fixes.`
  - `- **Next:** Slice 2 — sharing and multi-user accounts.`
  Append the Plan-8 spec + plan to the "Design specs" (line 20) and "Build plans" (line 22) lists.
- **backlog.md — record the one Plan-8 deferral.** Add a "Dev static-file caching — no-cache header for local development" entry under "## Deferred decisions" (Now: `UseStaticFiles` sends no `Cache-Control` → dev browsers serve stale JS; Later: a dev-only no-cache header; Why deferred: out of Plan-8 scope; Revisit: next housekeeping pass; Decided: 2026-07-08). Close no existing item.
- **Human acceptance (Malin & Henry, post-PR):** keyboard + screen-reader pass at phone width — switcher operable and announced, per-list Done strips reachable, inline renames feel right. **Hard-reload / disable cache first.**

**Commit:** `Plan 8 Task 9 — Slice 1 complete: README + backlog`

**Merge:** single-author branch (Claude as Malin) → squash-merge is clean. After merge, delete `feature/mobile-switcher` and confirm the remote branch actually went (the checklist merge silently didn't). Slice 1 is then complete.

---

## Gotchas carried forward

- **Hard-reload / disable cache before every browser check** — stale static JS (no `Cache-Control`) served the sweep old modules on 2026-07-08.
- Stop **`Wend.Api`** (not `Wend`) before `dotnet build`/`test`.
- No `data.db` reset — frontend-only, no schema change.
- Phase 2's mobile brainstorm is **done** (locked with Malin 2026-07-08) — both phases are unblocked; Phase 2 builds on a fresh `feature/mobile-switcher` branch off `main`.
