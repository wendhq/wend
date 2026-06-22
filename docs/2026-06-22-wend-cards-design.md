# Wend — Slice 1, Plan 3: Cards + task view — design

- **Date:** 2026-06-22
- **Status:** Approved 2026-06-22 — ready for the implementation plan
- **Owners:** Malin & Henry (equal ownership)
- **Extends:** [`docs/2026-06-15-wend-slice1-design.md`](2026-06-15-wend-slice1-design.md) (signed-off Slice 1 spec)
- **Builds on:** [`docs/2026-06-19-wend-lists-design.md`](2026-06-19-wend-lists-design.md) (Plan 2 — Lists, merged) and the model → repository → API → frontend → test pattern it established.

---

## Context

Plan 3 is the third of Slice 1's eight shippable plans. Plan 1 delivered board CRUD; Plan 2 added lists inside a board plus navigation into a single board. Plan 3 adds **Cards** inside a list — create, read, edit (title, notes, due date), and delete — plus the **task view**, a focused full-screen card-detail screen.

It introduces the third screen (Overview → Board → Task view) and the `Card` entity at the foot of the Board → List → Card tree. Card **moving/reordering** is deliberately Plan 5, **Done** is Plan 6, **labels + checklist** are Plan 4, and **undo-delete** is Plan 7 — so Plan 3 stays a thin, shippable vertical slice.

## Decisions from this brainstorm

Settled with both owners on 2026-06-22 (visual brainstorm):

1. **Full `Card` schema now, thin behaviour.** The `Card` table is created with all its Slice-1 columns — including the lifecycle timestamps `CompletedAt` / `ArchivedAt` / `DeletedAt` — even though Plan 3 only uses the core fields. Rationale: `EnsureCreated` cannot alter an existing database, so adding columns later forces a destructive db reset; carrying them now makes **Plan 6 (Done)** and **Plan 7 (undo-delete)** reset-free for a tool we use daily. *(New tables — labels/checklist in Plan 4 — will still need one reset; migrations remain a Slice 1→2 concern, per `Program.cs`.)*
2. **Task view = a separate screen, not a modal.** Navigation becomes Overview → Board → **Card**, reusing the existing coordinator pattern (fresh mount per nav, back link, focus the heading). Full-screen on every viewport. *(Rejected: modal/panel — needs focus-trap / ESC / scroll-lock / `aria-modal` and breaks the established nav pattern. Rejected: responsive hybrid — two code paths. A desktop modal can be a later enhancement.)*
3. **Cards live inside the board screen; click a chip to open it.** Each list shows its cards as title chips (with a due-date pill when set); a **persistent "Add a card…" input** at the bottom of each list mirrors the existing Add-list form. New cards **append**.
4. **Cascade via EF navigation + required FK (mirrors Plan 2).** Add a `List.Cards` navigation collection exactly as `Board.Lists` already does, and — with `Card.ListId` a required FK — EF's default cascade deletes a list's cards. Board→list already cascades the same way (Plan 2, with a passing test), so the full chain Board → List → Card cleans up on delete: one convention throughout, no Fluent API needed for cascade.
5. **Rename `js/lists/` → `js/board/`.** That module already renders the whole board screen (back link, title, add-list form, columns); "lists" was already a misnomer, and cards make it more so.
6. **Card edits save together via one `PUT /api/cards/{id}`** carrying title + notes + due date.
7. **Card delete is immediate — no confirm, no undo yet.** This matches the signed-off slice spec (cards use undo, not a confirm dialog). The undo toast arrives in Plan 7; until the slice is finalised Wend is only run for development/testing, so there is no real board data to protect in the interim. Deleting a card removes it straight away. *(List and board deletes keep their confirm dialog — those stay "big destructive actions" per the slice spec.)*

## Data model

Add a `Card` entity:

| Entity | Fields |
|---|---|
| **Card** | Id (PK) · ListId (FK) · Title · Description? · DueDate? · Position · CreatedAt · CompletedAt? · ArchivedAt? · DeletedAt? |

- **Relationship:** `List ──1:*── Card`, completing the chain `Board ──1:*── List ──1:*── Card`. A `List.Cards` navigation collection (mirroring `Board.Lists`) plus the required `Card.ListId` FK gives EF its default cascade: deleting a list removes its cards, and — since deleting a board already cascades to its lists — deleting a board removes its lists and their cards.
- **Position** — 0-based, contiguous within a list: **create** appends (`Position` = the list's current card count); **delete** re-compacts survivors so positions stay gapless. (Moving/reordering is Plan 5 — there is no move endpoint in Plan 3.)
- **Types** — `Description` is nullable free text; `DueDate` is a nullable date (`DateOnly?`, serialised `yyyy-MM-dd`); `CreatedAt` is set on insert (UTC); the three lifecycle timestamps are nullable `DateTime?`, **carried but never written in Plan 3**.
- **Query filter** — `Card` carries an EF global query filter `DeletedAt == null && ArchivedAt == null`, so every card read hides deleted/archived cards automatically. Inert in Plan 3 (nothing sets those), correct and ready for Plans 6 / 7.

## API surface

Mirrors the validated-endpoint style from Plans 1–2 (trim + length guard, fail-closed bodyless errors):

- `POST /api/lists/{listId}/cards` `{ title }` → 201 with the created card (400 blank / over-long title, 404 missing list)
- `GET  /api/cards/{id}` → 200 `{ id, listId, title, description, dueDate, position }` (404) — feeds the task view
- `PUT  /api/cards/{id}` `{ title, description?, dueDate? }` → 204 (400 / 404) — saves the task-view form in one call
- `DELETE /api/cards/{id}` → 204 (404) — hard delete + re-compact the list's remaining card positions
- **Validation:** `Title` trimmed, required, ≤ 200 chars (as elsewhere); `Description` ≤ 5000 chars, optional; `DueDate` a valid date or null.

**Reading cards — nested in the board detail.** Extend `GET /api/boards/{id}` so each list carries its cards:

```
GET /api/boards/{id} → { id, title, lists: [ { id, title, position,
                          cards: [ { id, title, dueDate, position }, … ] }, … ] }
                        // lists and cards each ordered by position
```

The board screen still loads in a single request. `GET /api/boards` (the overview) is unchanged and returns no lists or cards.

## Frontend — board screen + task-view MVC

- **`js/lists/` → `js/board/`** (model / view / controller), still mounted by `main.js` `showBoard`. It now renders, per list: the list header (title + Rename · Delete · Move left · Move right — unchanged) · the list's **card chips** (title + due-date pill; `data-action="open-card"`, accessible name `Open card: {title}`) · a persistent **"Add a card…"** form (`data-action="create-card"`). The board **model** gains `createCard(listId, title)` (reload-after-change — the server stays authoritative); list operations are unchanged.
- **New `js/card/` trio** = the task-view screen. `main.js` gains `showCard(cardId, boardId)` (mounts the trio on a fresh root; back link → `showBoard(boardId)`), wired from the board controller's `open-card`. The card **model** does `load(id)` (GET), `save({ title, description, dueDate })` (PUT), `remove()` (DELETE). The **view** renders the back link, an editable title, the "In list: {name}" line, a due-date input, a notes textarea, **Save changes**, and **Delete card**. The **controller** wires save (announce "Card saved."), delete (on success announce + navigate back to the board), and back.
- **Shared `escapeHtml`** is extracted to `js/escape.js` and imported by the three views (`boards`, `board`, `card`) — the 3rd copy, the project's documented trigger to extract.
- **Server stays authoritative** — both screens re-fetch after a mutation, as the boards and lists models already do.
- **Layout (mobile-first).** Card chips are full-width within a list; lists stack in one column on phones and lay out as horizontal columns at `min-width: 768px` (unchanged from Plan 2). The phone "one list at a time" switcher stays a **Plan 8** concern.
- **Accessibility.**
  - Focus moves to the task-view title on open, and back to the originating card chip on return (the `focusOpen` pattern Plan 2 built for boards).
  - The `aria-live` region announces add / save / delete.
  - Card chips are real `<button>`s; the add-card input is labelled.
  - Deleting a card removes it immediately (no confirm — decision 7), then returns focus to the board.

## Testing (NUnit — same split as Plans 1–2)

- **Repository** (`EfCardRepository`, throwaway SQLite): create appends position; get-by-id; get-for-list returns position-ordered; edit updates title / description / dueDate; delete re-compacts positions; **deleting a list cascades to its cards**; **deleting a board cascades to its lists and their cards**; the query filter excludes cards with `DeletedAt` / `ArchivedAt` set.
- **API** (via `WendApiFactory`, throwaway DB): POST creates under a list (201), 404s a missing list, 400s blank / over-long titles; `GET /api/cards/{id}` returns the card (404 missing); nested `GET /api/boards/{id}` returns each list's cards in order; PUT edits (204) and rejects invalid input (400 / 404); DELETE (204 / 404).
- **Frontend:** manual browser + screen-reader pass — add cards to a list, open a card, edit title / notes / due and save, delete, and confirm focus + announcements across board ↔ card navigation.

## Out of scope (later plans)

Card move / reorder (Plan 5) · Done checkmark + Done strip (Plan 6) · labels + checklist (Plan 4) · undo toast + restore + Trash (Plan 7) · phone one-list-at-a-time switcher (Plan 8) · drag-and-drop · URL-hash routing.

## Definition of done

- `Card` entity + `ICardRepository` / `EfCardRepository` behind the seam; full Slice-1 schema created; the `Card` query filter hides deleted / archived cards.
- Cascade covers Board → List → Card via the `List.Cards` navigation + required FK (mirroring `Board.Lists`): deleting a list removes its cards; deleting a board removes its lists and their cards.
- All four card endpoints live and validated; `GET /api/boards/{id}` returns nested, position-ordered cards.
- From the browser: add cards to a list, open a card into the task view, edit title / notes / due and save, delete — all persisted across a restart.
- `escapeHtml` extracted to a shared module; `js/lists/` renamed to `js/board/`.
- Dark-mode-first, keyboard-operable; focus managed across board ↔ card; add / save / delete announced.
- NUnit green (repository + API), `dotnet build` clean (0 warnings); tests never touch the real DB.
