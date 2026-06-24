# Wend — Plan 5: Card moving (design spec)

- **Date:** 2026-06-24
- **Status:** Signed off (2026-06-24) — ready for planning
- **Owners:** Malin & Henry (equal ownership)
- **Slice / Plan:** Slice 1 · Plan 5 of 8

---

## Goal

Make cards movable: **reorder a card within its list**, and **move it to another list** — buttons + keyboard only, no drag-and-drop (that's a later slice). One underlying move operation, mirroring the accessible list-reorder shipped in Plan 2.

This is the increment that makes the board genuinely usable as a kanban: until now a card is stuck in the list it was created in.

---

## Decisions

1. **Controls = Up/Down arrows + a "Move to list" dropdown** (Option B). ▲ ▼ reorder a card within its list; a native `<select>` lists every *other* list on the board and sends the card there. Chosen over four directional arrows (A) because a dropdown reaches any list in one step and scales as a board grows lists; chosen over a reveal-on-click panel (C) for less view state and fewer clicks. A native `<select>` carries keyboard + screen-reader support for free.
2. **A cross-list move lands at the bottom** of the target list (append). Predictable, matches how new cards already append, and keeps the announcement simple.
3. **Cross-board move → `400`.** The dropdown only ever lists same-board lists, so the UI can't trigger it, but the API rejects a target list that belongs to another board — consistent with the Plan 4 card-label guard, and it keeps the endpoint honest.
4. **No schema change, no DB reset.** `Card` already has `ListId` and `Position`; moving only ever rewrites those two columns.

---

## Data model

Unchanged. Ordering rides on the existing `Card.Position` (0-based, gapless per list) and `Card.ListId`, both shipped in Plan 3.

---

## Backend

**Repository** (`Wend.Core/ICardRepository.cs`, `EfCardRepository.cs`)

- New method `MoveCardAsync(int id, int targetListId, int position)`.
- Returns a result the endpoint maps to three outcomes — **moved / not-found / cross-board** — mirroring how Plan 4's card-label attach distinguished its statuses.
- Algorithm — Plan 2's `MoveListAsync` generalized across two lists:
  - **Same list** (target == source): take the list's cards ordered by position, lift this card out, clamp the target index to `[0, count]`, re-insert, renumber `0..n`.
  - **Different list:** set `card.ListId = targetListId`; insert into the target's ordered cards at the clamped position; renumber the target `0..n`; then re-sequence the *source* list to close the gap — reusing the existing private `ResequenceAsync(listId)` that delete already calls.
- Position always clamps to `[0, count]`, so the client can request "bottom" as simply the target list's current card count.

**API** (`Wend.Api/CardEndpoints.cs`)

- New endpoint `PUT /api/cards/{id}/move`, body `{ listId, position }` (`MoveCardRequest(int ListId, int Position)`).
- Maps: moved → `204`; missing card or target list → `404`; cross-board target → `400`.
- No change to `GET /api/boards/{id}` — it already nests each list's cards, which is everything the board view needs.

---

## Frontend (`Wend.Api/wwwroot/js/board/`)

**Card anatomy.** A card chip stops being a single `<button>` (the whole card is the "open" control today) and becomes a container: a clickable **title** that opens the task view (unchanged behaviour) + an **actions row** — the same shape `list-card` already uses. Nested buttons are invalid HTML, so a card that carries controls has to restructure this way.

**Controls per card** (in the actions row):

- **▲ Move up** — disabled on the first card in the list.
- **▼ Move down** — disabled on the last card in the list.
- **`<select>` "Move to…"** — options = every other list on the board; `aria-label` names the card; choosing a list moves the card to that list's bottom. Disabled when the board has no other list.

**Model** (`board/model.js`): one `moveCard(id, listId, position)` → `PUT /api/cards/{id}/move` then reload the board, so positions always come straight from the server (same approach as `move` for lists).

**Controller** (`board/controller.js`):

- ▲ → `moveCard(id, listId, index-1)`; ▼ → `moveCard(id, listId, index+1)`, guarded at the ends.
- Dropdown change → `moveCard(id, chosenListId, targetCardCount)`.
- **Announce:** "Card moved up." / "Card moved down." / "Card moved to {list title}."
- **Focus follows the card:** after ▲▼, focus stays on the same arrow (falling back to the other arrow, then the select, when it disables at an end); after a cross-list move, focus the moved card in its new list so a keyboard user can continue.

**View** (`board/view.js`): render the new card anatomy + controls + a `focusCardAction` helper mirroring the existing `focusListAction`.

---

## Testing (NUnit, per-task TDD)

Mirror the list-move and card-delete suites — roughly 8–10 tests.

**Repository** (`CardRepositoryTests`)

- Reorder up / down within a list keeps positions gapless.
- Move to another list appends to its bottom; source and target both stay gapless `0..n`.
- Out-of-range position clamps (negative → top, oversized → bottom).
- Missing card → not-found result.
- Missing target list → not-found result.
- Cross-board target → cross-board result.

**API** (`CardApiTests`)

- `PUT …/move` within a list → `204`, order reflects.
- `PUT …/move` to another list → `204`; `GET` board shows the card at the new list's bottom.
- Missing card → `404`; cross-board target → `400`.

---

## Scope / non-goals

- **No Done/Archive interaction** (Plan 6+): every card is active, so move just orders the visible cards.
- **No mobile switcher** (Plan 8): ▲▼ + a select behave identically whether lists render as columns or stacked.
- **No drag-and-drop** (later slice): the move *operation* is the same either way; this builds it once.

---

## Build

Coached turn-based with Henry — alternating drivers, per-task red→green TDD, per-human commits with no AI attribution. Branch off `main` (suggested `feature/card-moving`); PR → peer review → merge via merge commit (not squash), per the multi-author convention.
