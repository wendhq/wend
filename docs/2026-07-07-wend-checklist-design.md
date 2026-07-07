# Wend — Per-card checklist design spec (+ settings surface)

- **Date:** 2026-07-07
- **Status:** Draft — pending review (Malin & Henry)
- **Owners:** Malin & Henry
- **Builds on:** [Slice 1 design](2026-06-15-wend-slice1-design.md) · split out of Plan 4 as its own increment
- **Build mode:** coached turn-based (Malin & Henry alternating drivers)

---

## Context

The per-card checklist is the last Slice-1 feature increment before Plan 8 (mobile + a11y polish). It was split out of Plan 4 ("card detail: labels + checklist") so each vertical ships as a small PR. Two requirements were baked in at Plan 7 acceptance (see `backlog.md`): checklist-item deletes must be undoable via the toast, and the toast primitive now exists to reuse.

During design, the task view's busyness prompted two additions that ride along: an **Edit mode** for the task view, and a minimal **settings surface** (client-only) with two toggles.

## Scope

- One simple checklist per card (per the slice spec; multiple named checklists stay deferred).
- Items: **add, rename, check/un-check, reorder (▲▼), delete + undo**.
- Checked items tuck into a collapsible **Done strip** inside the checklist.
- Board card chips show checklist progress: **count pill + thin progress bar**.
- Task view gains an **Edit mode** toggle; renaming never requires it.
- New **Settings screen** (localStorage-backed): *Show card Done checkboxes* (default off) and *Always show Delete card button* (default off).
- The card-level Done toggle becomes **opt-in via settings, hidden by default everywhere** (board chips + task view).

**Out of scope:** multiple checklists per card · moving items between cards · the label chips-vs-bars display toggle (its settings home now exists; still deferred) · multi-item batch undo (Trash slice) · Archive/Trash screens.

## Decisions (and why)

1. **Full-parity item actions, including reorder.** Everything else in Wend reorders by accessible buttons; items shouldn't be the exception.
2. **Done strip mirrors the board's Done area** — collapsed by default, hidden at zero, `Done (n)` disclosure. One "completed things" pattern app-wide.
3. **Chip progress = count pill (`☑ 2/5`) + thin bar.** The number carries the information (never colour alone); the bar is decorative (`aria-hidden`) with a forced-colors border fallback. Nothing renders when the card has no items.
4. **Backend models the item as a miniature Card:** `CheckedAt?` and `DeletedAt?` nullable timestamps (no bool), EF query filter, soft-delete + restore, dedicated `/check` and `/move` endpoints. One lifecycle idiom across the codebase; undone items keep their identity; the future Trash slice covers items for free. (Amends the slice spec's illustrative `IsChecked` bool.)
5. **Task-view Edit mode.** An `aria-pressed` **Edit** toggle (top right) reveals the structural/destructive controls: the due-date/notes form (today an always-visible form), item ▲▼/✕, done-item ✕, and **Delete card**. Normal mode is read-only + checkboxes + add-input. **Renaming is always direct** — the card heading and item texts are rename controls (text-as-button → input swap, Enter saves, Esc cancels, focus returns to the renamed text). Done-strip items are the one exception: they are not renameable — un-check first (they keep only un-check + delete). Un-checking is always available: a checkbox is state, not destruction.
6. **Card Done toggle hidden by default, everywhere.** With a checklist carrying completion, the card-level checkbox is noise for us; a settings toggle re-enables it. The board's **Done area still renders whenever done cards exist** — hide entry points, never state — and un-check from the Done area remains the recovery path.
7. **Settings are client-only.** `js/prefs.js` over `localStorage` (per-browser; right for a local single-user app), plus a small settings MVC trio. No schema, no API. Fresh-mount-per-navigation means a changed setting applies on the next mount — no cross-view subscriptions.
8. **Item delete = soft-delete + "Deleted · Undo" toast**, replace-not-stack, reusing `toast.js` with Plan 7's focus contract. Undo restores the item at its stored (clamped) position.
9. **One `Position` sequence per card**, shared by checked and unchecked items — gapless, 0-based (append on create, resequence on delete, clamp + resequence on move). The Done strip is a render grouping on `CheckedAt != null` ordered by the same sequence, so un-checking drops an item back exactly where it lived. The frontend's ▲▼ targets the neighbouring **unchecked** item's position (Plan 6's active-subset lesson); the backend move stays a dumb clamp-insert-resequence.

## Data model

**`ChecklistItem`** (new table — one-time `%LOCALAPPDATA%\Wend\data.db` delete before the first manual run; `EnsureCreated` cannot migrate a live db):

| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `CardId` | int FK, required | + `Card.ChecklistItems` nav → DB-level cascade on hard delete (mirrors `Board.Lists` / `List.Cards`) |
| `Text` | string, required | trimmed, ≤200 chars (house validation) |
| `CheckedAt` | DateTime? | null = unchecked (mirrors `Card.CompletedAt`) |
| `DeletedAt` | DateTime? | soft delete; EF query filter `DeletedAt == null` |
| `Position` | int | 0-based gapless within the card (checked + unchecked share one sequence) |

## API

All endpoints mirror an existing idiom; bodyless fail-closed errors as elsewhere.

| Endpoint | Behaviour |
|---|---|
| `POST /api/cards/{cardId}/checklist-items` `{text}` | 201 + item, appended · 404 missing card · 400 invalid text |
| `PUT /api/checklist-items/{id}` `{text}` | rename → 204 · 404 · 400 |
| `PUT /api/checklist-items/{id}/check` `{checked}` | sets/clears `CheckedAt` → 204 · 404 (omitted field defaults false, mirrors `/complete`) |
| `PUT /api/checklist-items/{id}/move` `{position}` | clamp + resequence within the card → 204 · 404 (no cross-card moves; omitted position defaults to 0 and is clamped, mirroring existing move semantics) |
| `DELETE /api/checklist-items/{id}` | soft-delete + resequence remaining → 204 · 404 |
| `POST /api/checklist-items/{id}/restore` | re-insert at stored (clamped) position → 204 · 404 — **reads via `IgnoreQueryFilters().FirstOrDefaultAsync`, never `FindAsync`** (Plan 7 lesson); restoring a non-deleted item follows card-restore semantics exactly (verify which in Task 4 and mirror) |

Nesting: `GET /api/cards/{id}` gains `items: [{id, text, checkedAt, position}]` ordered by position. `GET /api/boards/{id}` card summaries gain **`checklistDone` / `checklistTotal`** counts only (non-deleted items) — no item bodies in the board payload.

Repository: `IChecklistItemRepository` / `EfChecklistItemRepository` — `AddAsync`, `RenameAsync`, `SetCheckedAsync`, `MoveAsync`, `DeleteAsync`, `RestoreAsync` (bool → 204/404 mapping, mirrors existing repos).

## Frontend

- **`js/card/checklist.js`** — render helper composed by `card/view.js` (the `labels.js` pattern): header (`Checklist` + count pill + bar), unchecked items, Done strip disclosure, add-input. Card view `ui` grows `{ editMode: false, doneOpen: false, renamingId: null }`.
- **Task view restructure** (`card/view.js`): normal mode renders due date + notes read-only; the existing due/notes form appears only in Edit mode. Heading becomes the card's rename control. Done-toggle block renders only when `showCardDone`; Delete card renders when `editMode || alwaysShowDeleteCard`. Labels section unchanged and ungated.
- **`js/prefs.js`** — `getPrefs()` / `setPref()` with defaults `{ showCardDone: false, alwaysShowDeleteCard: false }`, localStorage key `wend.prefs`; reads pick the two known keys explicitly with type checks (`=== true`), never spreading parsed JSON; any parse failure falls back to defaults.
- **`js/settings/`** trio — model wraps prefs + notify; view = two native labelled checkbox toggles; controller announces changes ("Card Done checkboxes on"). Mounted by the coordinator focusing its heading; Back returns to the overview focusing the new-board input (the overview has no heading — that input is its existing focus anchor).
- **App shell** (`index.html` + `main.js`): a Settings button in the header (outside `#app`, like `#status`), wired by the coordinator to `showSettings()`.
- **Board view** (`board/view.js`): chip leading checkbox gated on `showCardDone`; **Done area unaffected by the pref** (renders whenever done cards exist); chips gain the pill + bar when `checklistTotal > 0`, and the chip `aria-label` includes "2 of 5 done".
- **Item-delete undo**: coordinator wires the toast exactly like card deletes — item deleted → `Deleted: {text}` toast → Undo → `POST …/restore` → **re-mounts the task view for that card from wherever you are** (the toast outlives navigation — mirrors Plan 7's navigate-on-undo), focusing the restored item's checkbox — opening the Done strip first when the restored item is checked, so focus never lands in a collapsed region; toast dismissed → focus per Plan 7's contract.
- `escapeHtml` at every new render site; announcements via the existing `#status` live region.

## Accessibility

- Edit toggle: real button, `aria-pressed`, both transitions announced; pressing the toggle keeps focus on it.
- **Esc precedence** (one keystroke, one effect): Esc inside a rename input cancels that rename only, focus returning to the rename control; Esc with the label picker open closes the picker only (existing behaviour); otherwise, if Edit mode is on, Esc exits Edit mode **and moves focus to the Edit toggle** — focus never dies with a control that just disappeared.
- After a successful add, focus returns to the (cleared) add-input so consecutive adds flow without re-tabbing.
- Edit-mode reveal and strip expand/collapse are instant re-renders — no animation, nothing for `prefers-reduced-motion` to suppress (house pattern).
- Renames: text-as-button (`aria-label="Rename: {text}"`) → input swap; Enter saves, Esc cancels, focus returns to the renamed control.
- ▲▼ move within the unchecked subset; focus follows the moved item; announced ("Moved up: Email the group").
- Check/un-check announced with progress context ("Checked: Email the group — 3 of 5 done"); strip disclosure uses `aria-expanded`; strip hidden at zero.
- Progress bar `aria-hidden` + forced-colors border fallback; count pill carries the information.
- Settings toggles are native checkboxes with visible labels; changes announced.
- All targets ≥44px; keyboard-operable end to end.

## Testing

NUnit, TDD per task, 0-warnings gate. Suite ~124 → **~146**.

- **Repo (~13):** add appends position · add to missing card fails · rename (+ trim/validation) · check sets/clears `CheckedAt` · move clamps + resequences · soft-delete hides via filter + resequences · restore lands at stored clamped position · **restore after `ChangeTracker.Clear()`** (regression shape for the Plan 7 `FindAsync` bug) · hard-delete cascade via nav.
- **API (~9):** create 201/400/404 · rename · check · move · delete · restore status codes · items nested in card detail · counts in board nest.
- **Frontend:** manual browser verification per task; keyboard + screen-reader acceptance click-through (Malin & Henry) before merge.
- Per-turn test-count check against the plan's running total (Plan 3 lesson).

## Build shape (~12 turn-based tasks)

1. `ChecklistItem` model + `WendDbContext` (DbSet, filter, nav) — first repo tests red→green
2. Repo: add + rename
3. Repo: check + move
4. Repo: soft-delete + restore (incl. `ChangeTracker.Clear()` regression)
5. API: create + rename + check
6. API: move + delete + restore + nesting/counts
7. Frontend: `prefs.js` + settings trio + shell header button + coordinator
8. Frontend: board chips (pill + bar) + done-checkbox gating
9. Frontend: `checklist.js` + normal mode (items, strip, add, rename)
10. Frontend: Edit mode (toggle, ▲▼/✕, form + Delete card behind it)
11. Frontend: item-delete toast wiring + focus/announce polish
12. Acceptance + README status + backlog updates (close the item-undo entry; note the label-display toggle's settings home now exists)

Branch `feature/checklist` → PR → review + acceptance → merge (merge-not-squash if multi-author). Driver pushes at end of turn; next driver fetches + switches.

## Gotchas carried forward

- Stop any running `Wend.exe` / `dotnet run` before build/test (file lock).
- One-time `data.db` delete on each machine before the first manual run (new table).
- `FindAsync` bypasses query filters only for tracked entities — restore paths use `IgnoreQueryFilters()`; keep the fresh-context regression test.
- Squash-merging a multi-author branch would add a co-author trailer — merge-commit instead (house rule).
- Soft-deleted checklist items persist in `data.db` until the Trash slice ships purge/empty — the same accepted retention trade-off as cards (Plan 7).
