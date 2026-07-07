# Wend — Plan 7: Soft-delete + undo

**Date:** 2026-07-07
**Status:** Design signed off (brainstorm). Ready for stress test → planning.
**Slice:** 1, Plan 7 of 8 — builds on boards + lists + cards + labels + card-moving + Done.

## Summary

Deleting a card becomes an **undo-first, reversible** action. Instead of removing the
row, delete sets `Card.DeletedAt`; the EF query filter already hides deleted cards from
every read, so the card simply vanishes from the board. A transient **"Deleted · Undo"**
toast — the app's first toast — offers a one-click restore that returns the card to its
**original spot**. **No schema change, no DB reset** (`DeletedAt` already exists and the
filter already excludes it).

## Decisions

**Settled by the Slice 1 master spec (signed off 2026-06-16):**

1. **Undo-first, not confirm** — card delete no longer confirms; it soft-deletes and
   offers undo. Lists and boards keep their confirm dialog and stay **hard**-delete (only
   `Card` carries `DeletedAt`).
2. **Dedicated restore endpoint** — `POST /api/cards/{id}/restore` (mirrors Plan 5
   `/move` and Plan 6 `/complete`), not folded into another PUT.
3. **Reusable toast primitive** — built as a shell element (`toast.js` beside the
   `#status` region) so it survives the board re-mount and Plan 8 can reuse it.

**Resolved in this brainstorm (the four forks):**

4. **Restore position = original spot.** Restore re-inserts the card at its stored
   `Position`, clamped to the list's current length, then resequences — a faithful undo.
   Soft-delete never rewrites the deleted card's `Position`, so it's still there to use.
5. **Toast lifetime = ~8s, pauses on hover/focus, plus a manual dismiss.** The
   auto-dismiss timer pauses while the pointer is over the toast or keyboard focus is
   inside it, so a keyboard / screen-reader user has time to reach Undo. A `×` dismisses
   immediately.
6. **One toast at a time.** A new delete replaces the current toast; the earlier card
   stays deleted (recoverable later via the Trash screen — a future slice). One region,
   one announcement.
7. **Delete from the card detail view only.** Delete stays where it is today (open card →
   Delete). On delete the app navigates back to the board and the toast appears there. No
   board-chip delete button in this plan.

## Data model — no change

`Card.DeletedAt` (`DateTime?`) already exists (`Card.cs:17`). The EF global query filter
is `DeletedAt == null && ArchivedAt == null` (`WendDbContext.cs:18`), so every LINQ read
over `db.Cards` already hides soft-deleted cards — including the board nest, which builds
summaries through `GetCardsForListAsync` (`BoardEndpoints.cs:32`). Nothing to migrate;
existing `data.db` files already have the column.

## API

**Changed — `DELETE /api/cards/{id}` becomes a soft delete**

- Still `204 No Content` on success; `404` if the card doesn't exist **or is already
  deleted**.
- `EfCardRepository.DeleteCardAsync` (`EfCardRepository.cs:41`) changes from
  `db.Cards.Remove(card)` to `card.DeletedAt = DateTime.UtcNow`, keeping the existing
  `ResequenceAsync(card.ListId)` call. `ResequenceAsync` queries through the filter, so it
  renumbers the **surviving** (still-visible) cards to a gapless `0..n-1` and never touches
  the deleted card's stored `Position`.
- Returns `false` when the card is missing or already soft-deleted (the guard catches an
  already-deleted row while it's still tracked; a fresh read finds it already filtered out).

**New — `POST /api/cards/{id}/restore`**

- `204 No Content` on success (including the idempotent no-op of restoring a card that
  isn't deleted); `404` if no such card row exists.
- `ICardRepository.RestoreCardAsync(int id)` returns whether the card was found.
  `EfCardRepository`:
  - `db.Cards.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id)` → `null` ⇒ `false`
    (404). Already active (`DeletedAt is null`) ⇒ `true` (no-op). **Must be
    `IgnoreQueryFilters`, not `FindAsync`** — see the note below.
  - Otherwise: load the list's current active cards (`GetCardsForListAsync` — the filter
    still excludes the deleted card), clear `DeletedAt`, insert the card at
    `Math.Clamp(card.Position, 0, activeCount)`, renumber `0..n`, save.
- If the card's list was hard-deleted meanwhile, the FK cascade removed the card row too →
  the read returns `null` → 404, and the toast surfaces "Couldn't restore." Rare; the
  toast is short-lived.

**Hardening — the card GET hides a deleted card.** `GetCardAsync` (`EfCardRepository.cs:12`)
used `FindAsync`, which returns a *tracked* soft-deleted card straight from the change-tracker
— so a GET right after a delete (same context) could still return it. Change it to a filtered
read (`db.Cards.FirstOrDefaultAsync(c => c.Id == id)`) so a deleted card `404`s consistently.
Small, in-scope correctness fix.

No other endpoint changes — the board nest already drops soft-deleted cards.

> **`FindAsync` vs. the query filter (the lesson).** `FindAsync` returns an entity straight
> from the change-tracker when it's already tracked — bypassing the filter — but when it
> reads from the database it **applies** the global query filter. Each API request gets its
> own `DbContext`, so restore reads from the DB, where a soft-deleted card is filtered out.
> That's why `RestoreCardAsync` uses `IgnoreQueryFilters()`. Repo tests that reuse one
> context keep the entity tracked and never hit this — a fresh-context test (or
> `ChangeTracker.Clear()`) does.

## Frontend (vanilla-JS MVC, served from `wwwroot`)

**New shell primitive — `js/toast.js`**

- `createToast(region)` → `show({ message, actionLabel, onAction })` and `dismiss()`.
- Renders into `#toast-region`, a new container in the shell **outside `#app`** (placed
  **before `#app`** — see Shell), so `app.replaceChildren()` on navigation never wipes it.
- Visible content: the message text + a real `<button>` for the action (Undo) + a `×`
  dismiss button. Clicking the action runs `onAction` then dismisses; `×` dismisses.
- **Escape discipline:** the message is written with `textContent` (a text node), **never**
  `innerHTML` with the title interpolated — matching `announce()`. The card title is user
  input; `textContent` makes XSS impossible on this new render path without needing
  `escapeHtml`.
- Auto-dismiss after ~8s; the timer **pauses on `mouseenter` / `focusin`, resumes on
  `mouseleave` / `focusout`** (tracking remaining time). A second `show()` replaces the
  current toast (clears the timer + content).
- Not itself a live region — announcements go through the existing `announce()`
  (`#status`), so nothing double-speaks. The region carries `role="group"` +
  `aria-label="Deleted card"` so its purpose is clear when focus lands on Undo.
  Reduced-motion: no transition on show/hide (the design-system motion tokens already gate
  on `prefers-reduced-motion`).

**Shell (`index.html`)** — add `<div id="toast-region"></div>` **before `#app`** (right
after `<header>`), still outside `#app` so `app.replaceChildren()` never wipes it. CSS
positions it `fixed` at the bottom, so it reads as a bottom toast but sits near the front
of the tab order — Undo is one Shift+Tab back from the board heading (see Accessibility).

**Coordinator (`main.js`)** — owns the toast, like it already owns `announce` + navigation:

- `const toast = createToast(document.getElementById("toast-region"))`.
- `showCard`'s `onDeleted` gains the deleted card's id + title:
  `onDeleted: (cardId, title) => { showBoard(boardId); toast.show({ message: `Deleted: ${title}`, actionLabel: "Undo", onAction: () => undoDelete(cardId, title, boardId) }); announce(`Deleted: ${title}. Undo available.`); }`.
- `undoDelete(cardId, title, boardId)` → `POST /api/cards/{id}/restore` → on success
  `announce(`Restored: ${title}.`)` + `showBoard(boardId, cardId)` (re-mounts the board and
  focuses the restored card via the existing `view.focusCard`). On failure,
  `announce("Couldn't restore the card — please try again.")`.

**Card detail (`card/controller.js`)** — the `delete` handler (`card/controller.js:19`)
captures the current card's title (from the `subscribe` callback, the way it already tracks
`palette`) and, after `model.remove()` succeeds, calls `onDeleted(cardId, title)` instead
of announcing locally. The coordinator owns the announcement + toast. (`cardId` is the
model's construction id.)

**No board changes.** The board already omits soft-deleted cards (filtered nest); after
delete it re-mounts fresh, after undo it re-mounts with the card restored + focused.
`board/view.focusCard` already exists (used by the Done restore).

**CSS (`app.css`)** — a `.toast` block: fixed near the bottom, design-system tokens
(surface, border, radius, shadow); the action + `×` as ≥44px targets; a solid border for
forced-colors; no transition under `prefers-reduced-motion`.

## Accessibility

- **Announcement** — deleting announces "Deleted: `<title>`. Undo available." and restoring
  announces "Restored: `<title>`." via the polite `#status` region, so a screen-reader user
  knows undo exists.
- **Keyboard-reachable undo** — `#toast-region` sits **before `#app`** in the DOM, so the
  Undo `<button>` is near the front of the tab order: one Shift+Tab back from the board
  heading, not a tab past every card. Focus is **not** stolen on delete (it follows the
  back-navigation to the board heading via `showBoard`'s `focusHeading`). Once focus enters
  the toast, the auto-dismiss timer pauses, so it can't vanish mid-reach.
- **Focus on dismiss** — a `×` dismisses immediately and returns focus to the board heading
  (never letting it fall to `<body>`); Undo moves focus to the restored card *before* the
  toast is torn down, so focus is never left on a removed node. The action and `×` are
  ≥44px.
- **Reduced-motion / forced-colors** — no motion on show/hide under
  `prefers-reduced-motion`; a solid border keeps the toast visible in forced-colors.

## Testing

**Backend (NUnit):**

- `DeleteCardAsync` sets `DeletedAt` (the row still exists), the card drops from
  `GetCardsForListAsync` + the board nest, survivors are resequenced gapless, and a second
  delete of the same card returns `false`.
- `RestoreCardAsync` — clears `DeletedAt`; re-inserts at the clamped original position
  (delete a middle card, restore, assert the order); idempotent `true` on an active card;
  `false` on a missing card.
- Endpoints — `DELETE …` 204 then 404 on repeat; `POST …/restore` 204 / 404; a soft-deleted
  card 404s on `GET /api/cards/{id}`.
- ~ +10–12 tests, 0 warnings. Existing delete tests stay green (soft-delete is transparent
  to filtered reads).

**Frontend:** manual browser + keyboard / screen-reader acceptance (Henry), mirroring prior
plans — the toast's timer / pause / focus behaviour is verified by hand.

## Out of scope (later)

- **Trash screen** (durable recovery + empty) — a future slice; Plan 7 ships only the
  transient toast. Until it exists, soft-deleted rows persist in `data.db` (hidden, not
  erased) with no purge path, and undo does not survive a page reload.
- **List / board soft-delete + undo** — they keep confirm + hard delete.
- **Board-chip delete button** — delete stays in the detail view.
- **Archive.**

## Open questions

None — all four forks resolved in the brainstorm.
