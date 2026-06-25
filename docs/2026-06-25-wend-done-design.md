# Wend — Plan 6: Done (card completion)

**Date:** 2026-06-25
**Status:** Design signed off (brainstorm). Ready for planning.
**Slice:** 1, Plan 6 of 8 — builds on boards + lists + cards + labels + card-moving.

## Summary

A card can be marked **done** with a checkbox. Done cards leave their list's active
flow and collect in a single collapsible **global Done area** beneath the board,
grouped by source list. Marking done sets `CompletedAt`; un-checking clears it and the
card returns to its list in place. **No schema change, no DB reset.**

## Decisions (from the brainstorm)

1. **Scope** — Plan 6 ships the done toggle + the global Done area on the current
   all-lists board view. The **per-list Done strip** and the **mobile single-list
   switcher** are **Plan 8** (the dedicated mobile + a11y plan), which reuses the work
   here.
2. **Toggle affordance** — a native, labelled `<input type="checkbox">` leads each
   active card *and* appears in the card-detail view. One click marks done without
   opening the card.
3. **Done rendering** — one **global Done area**: a full-width section below the lists,
   hidden when no card is done, otherwise collapsed by default with a `✓ Done (n)`
   header. Expanded, done cards are grouped under their source list as dimmed,
   struck-through rows, each with an un-check control.
4. **API** — a dedicated `PUT /api/cards/{id}/complete` (mirrors Plan 5's `/move`), not
   folded into the content-edit `PUT`. The card keeps its `ListId` + `Position` the
   whole time; "done" is purely a render grouping on `CompletedAt != null`.

## Data model — no change

`Card.CompletedAt` (`DateTime?`) already exists. The EF global query filter is
`DeletedAt == null && ArchivedAt == null` — it does **not** filter `CompletedAt`, so
done cards are already returned in every card query, including the board nest. Nothing
to migrate; existing `data.db` files already have the column.

## API

**New endpoint**

- `PUT /api/cards/{id}/complete` — body `{ "completed": true | false }`.
  - `true` → `CompletedAt = DateTime.UtcNow`; `false` → `CompletedAt = null`.
  - `204 No Content` on success; `404` if the card doesn't exist. (Like `/move`, an omitted
    `completed` defaults to `false` rather than 400 — our own frontend always sends it.)
  - Repository: `ICardRepository.SetCardCompletedAsync(int id, bool completed)` returns
    whether the card was found. `EfCardRepository` loads it with `db.Cards.FindAsync(id)`
    (mirrors `EditCardAsync` / `MoveCardAsync`), sets/clears `CompletedAt`, saves.

**Response additions** (so the frontend can group + show checked state)

- `CardSummary` (nested in `GET /api/boards/{id}`) gains `CompletedAt` (`DateTime?`).
- `CardDetail` (`GET /api/cards/{id}`) gains `CompletedAt` (`DateTime?`).

No other endpoint changes — the board nest already includes done cards.

## Frontend (vanilla-JS MVC, served from `wwwroot`)

**`board/`**
- *view* — render the done checkbox as the leading control of each active card chip
  (`CompletedAt == null`). Collect done cards (`CompletedAt != null`) across all lists
  into the global Done area: a labelled `<section>` with an `aria-expanded` disclosure
  button (`✓ Done (n)`), hidden when `n == 0`, collapsed by default. Expanded, group
  rows by `ListId` under the list's title; each row is dimmed + struck-through with an
  un-check control. Reuse `escapeHtml`.
- *model* — unchanged load (the board nest already carries every card incl. done);
  add a split helper (active vs done) if it reads cleaner. Local UI state for the Done
  area's expand/collapse (mirrors the label picker's `pickerOpen`).
- *controller* — wire the chip checkbox + the Done-area un-check →
  `api('/api/cards/{id}/complete', PUT { completed })` → reload board → `announce`.
  Wire the disclosure toggle (local state → re-render).

**`card/`** (detail view)
- *view* — add the same labelled checkbox near the title, reflecting `CompletedAt`.
- *controller* — toggle → same endpoint → refresh + announce.

**Shared / CSS** — reuse `api.js`, `announce.js`, `escape.js`. Add `.done-area`,
`.done-group`, `.done-row`, completed-card and checkbox styles to `app.css`. No new
files unless a `board/done.js` render helper proves cleaner (decide in the plan).

## Accessibility

- The toggle is a real `<input type="checkbox">` with a label tied to the card title,
  so screen readers announce "Done, <title>, checked / unchecked".
- `aria-live` (`#status`) announces "Marked done: <title>" / "Restored: <title>".
- The Done area is a labelled region toggled by an `aria-expanded` disclosure button
  (count in its text).
- Focus is never lost: after marking done (the card leaves its list) focus moves to the
  Done-area toggle; after un-checking, focus returns to the restored card (Plan-5
  `focusCardAction` pattern). All targets ≥ 44px.
- Expand / collapse honours `prefers-reduced-motion`.

## Testing

- **Backend (NUnit):** repo `SetCardCompletedAsync` set + clear + not-found; API
  `PUT …/complete` 204 / 404 / 400; `completedAt` present in the board nest and in
  `CardDetail`. ~ +10–12 tests, 0 warnings.
- **Frontend:** manual browser + keyboard / screen-reader acceptance (Henry), mirroring
  prior plans.

## Out of scope (later plans)

- Per-list Done strip + mobile single-list switcher → **Plan 8**.
- Soft-delete + undo toast → **Plan 7**.
- Archive → later slice.

## Open questions

None — all forks resolved in the brainstorm.
