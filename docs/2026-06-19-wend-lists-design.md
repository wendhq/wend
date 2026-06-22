# Wend — Slice 1, Plan 2: Lists — design

- **Date:** 2026-06-19
- **Status:** Design approved 2026-06-19 — written spec under review
- **Owners:** Malin & Henry (equal ownership)
- **Extends:** [`docs/2026-06-15-wend-slice1-design.md`](2026-06-15-wend-slice1-design.md) (signed-off Slice 1 spec)
- **Builds on:** [`docs/plans/2026-06-16-slice1-foundation-boards.md`](plans/2026-06-16-slice1-foundation-boards.md) (Plan 1 — Foundation + Boards, merged)

---

## Context

Plan 2 is the second of Slice 1's eight shippable plans. Plan 1 delivered board CRUD and the full **model → repository → API → frontend → test** pattern. Plan 2 adds **Lists** inside a board: create, rename, delete, and **reorder**, accessibly.

It introduces the one thing Plan 1 didn't need — **navigating into a single board** — and, because reordering is in scope, it establishes the **accessible move-button pattern** (buttons + a single move endpoint + screen-reader announcements) that later plans reuse for cards.

## Decisions from this brainstorm

Two questions the slice spec left open were settled with both owners on 2026-06-19:

1. **Navigation — open a board into its own view.** Clicking a board on the overview swaps the screen to that board's view (its lists), with a back link to the overview. This matches the slice spec's "board overview = one board's lists", is the familiar kanban model, and is the only option that stays clean when cards arrive in Plan 3. *(Rejected: inline accordion, board switcher.)*
2. **List reordering is in Plan 2.** This **extends the signed-off slice spec**, which scoped the accessible "move" operation to cards only. Reordering lists is now included so a board's columns are fully orderable. The accessible move-button pattern built here is reused by Plan 5 (card moves) — built once, not twice.

## Data model

Add a `List` entity:

| Entity | Fields |
|---|---|
| **List** | Id (PK) · BoardId (FK) · Title · Position |

- **Relationship:** `Board ──1:*── List`. `BoardId` is a required FK with a `Board.Lists` navigation collection. Deleting a board **cascades** to its lists (EF Core default for a required FK) — what "delete this board and everything in it" already promised.
- **Position** is a **0-based, contiguous** index within a board:
  - **Create** → append (`Position` = current list count).
  - **Delete** → re-compact survivors so positions stay gapless.
  - **Move** → re-sequence (see below).
- Contiguous positions (not sparse/gapped) are the simplest correct choice for the handful of lists a local board holds.

## API surface

Mirrors Plan 1's validated-endpoint style (trim + ≤200-char guard, fail-closed bodyless errors):

- `POST /api/boards/{boardId}/lists` `{ title }` → 201 with the created list (400 blank/over-long, 404 missing board)
- `PUT  /api/lists/{id}` `{ title }` → 204 (400 / 404)
- `DELETE /api/lists/{id}` → 204 (404)
- `PUT  /api/lists/{id}/move` `{ position }` → 204 (404) — `position` is the target 0-based index among the board's lists; the server **clamps** it to range, reorders, and rewrites positions to stay contiguous. (Lists never move between boards in Slice 1, so there is no `boardId` in the body — unlike the card move.)

**Reading lists — nested in the board detail.** Extend `GET /api/boards/{id}` to return the board **with its lists**, via a response DTO:

```
GET /api/boards/{id} → { id, title, lists: [ { id, title, position }, … ] }   // lists ordered by position
```

The board view loads in a single request, and this is the natural shape for board → lists → cards later. `GET /api/boards` (the overview) is unchanged and returns no lists.

## Frontend — navigation + lists MVC

- **`main.js` becomes a small coordinator.** It holds app state `currentBoardId` (null = overview, set = board view), renders the right module into `#app`, and manages focus on each transition.
- **Overview** — the existing boards screen, with each board gaining an **Open** control (`data-action="open"`, accessible name "Open board: {title}") that sets `currentBoardId`.
- **Board view** — a new `js/lists/` module (`model` / `view` / `controller`, mirroring `js/boards/`) rendering: a **"← Boards"** back link, the board title (`<h2>`), an add-list form, and the board's lists. Each list shows its title + **Rename · Delete · Move left · Move right**.
- **Server stays authoritative.** The lists model re-fetches the board detail after each mutation (mirroring the boards model's load-after-change), so positions always come back correct from the server rather than being guessed on the client.
- **Layout (mobile-first).** Lists stack in one column on phones; at `min-width: 768px` they lay out as horizontal columns — the start of the familiar board. *(Per-list Done strip and cards are later plans.)*
- **Accessibility.**
  - Focus moves to the board-view heading when a board opens, and back to that board's **Open** control when returning.
  - The `aria-live` region announces open / back, add / rename / delete, and **moves** ("List moved left.").
  - **Move left / Move right** are disabled at the ends (first / last list).
  - **Delete** keeps a **confirm dialog** — per the slice spec, deleting a whole list is a big destructive action.
- **Deferred (noted, not built):** URL-hash routing so the browser Back button moves between overview and board view. Plan 2 uses the in-app back link + focus management to stay lean; hash routing is an easy follow-up if wanted.

## Testing (NUnit — same split as Plan 1)

- **Repository** (`EfListRepository`, in-memory SQLite): create appends position; get-for-board returns position-ordered; rename; delete re-compacts positions; move reorders and **clamps at both ends**; deleting a board cascades to its lists.
- **API** (via `WendApiFactory`, throwaway DB): POST creates under a board (201) and 404s for a missing board; nested `GET /api/boards/{id}` returns lists in order; PUT renames (204) and rejects blank / over-long (400); DELETE (204); move reorders (204); 404s for a missing list.
- **Frontend:** manual browser + screen-reader pass — open a board, full list CRUD, reorder by keyboard, focus returns and each action announces.

## Out of scope (later plans)

Cards and the task view (Plan 3) · per-list Done strip (Plan 6 — needs cards) · moving lists between boards · URL-hash routing · drag-and-drop.

## Definition of done

- `List` entity + `IListRepository` / `EfListRepository` behind the seam; board delete cascades to lists.
- All four list endpoints live and validated; `GET /api/boards/{id}` returns nested, position-ordered lists.
- From the browser: open a board, then create / rename / delete / reorder its lists, persisted across a restart.
- Dark-mode-first, keyboard-operable; focus managed across navigation; moves and CRUD announced; Delete confirmed.
- NUnit green (repository + API), `dotnet build` clean (0 warnings); tests never touch the real DB.
- The accessible move-button pattern is in place for Plan 5 (card moves) to reuse.
