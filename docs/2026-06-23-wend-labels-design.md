# Wend — Slice 1, Plan 4: Labels — design

- **Date:** 2026-06-23
- **Status:** Approved 2026-06-23 — ready for the implementation plan
- **Owners:** Malin & Henry (equal ownership)
- **Extends:** [`docs/2026-06-15-wend-slice1-design.md`](2026-06-15-wend-slice1-design.md) (signed-off Slice 1 spec)
- **Builds on:** [`docs/2026-06-22-wend-cards-design.md`](2026-06-22-wend-cards-design.md) (Plan 3 — Cards + task view, merged) and the model → repository → API → frontend → test pattern it established.

---

## Context

Plan 4 is the fourth of Slice 1's eight shippable plans. The signed-off slice spec scoped Plan 4 as "card detail (labels + checklist)". On review (2026-06-23) we **split it into two sequential increments** — **Plan 4 = Labels** (this spec) and a following increment for the **checklist** — because they are two independent verticals (each a full entity → repository → API → frontend slice) that share only the task view as a host. Smaller PRs review better, it keeps the turn-based rhythm with both owners tight, and Plan 3 just taught us a large plan can hide a dropped test behind a green suite. "Plan 4 = card detail" stays one roadmap line, delivered in two passes.

Labels are **board-scoped, reusable** tags (a name + a colour) that any card on that board can carry, many-to-many. They render as soft-tint chips both on the board's card fronts and in the task view, and are managed inline from the card.

## Decisions from this brainstorm

Settled with both owners on 2026-06-23 (visual brainstorm):

1. **Split from the checklist.** Plan 4 ships labels only; the checklist becomes its own brainstorm → spec → plan → build cycle next.
2. **Board-scoped reusable labels, managed inline (Trello-style).** A board owns a palette of labels; cards attach/detach them many-to-many. Create, rename, recolour and delete all happen in an inline **picker popover on the card** — no separate management screen. *(Rejected: a dedicated board "Manage labels" screen — more correct but front-loads a whole screen for Slice 1. Rejected: per-card free-text tags — simplest, but no reuse/consistency, no many-to-many, weakest learning value.)*
3. **Soft-tint chips, curated palette, name always shown.** Labels render as a faint colour wash with colour-matched text and the label name. Colours come from a **curated six-colour palette** (not a free hex picker, which invites low-contrast choices). The **name is always visible**, so colour is never the only signal — colour-blind and forced-colors safe. *(Rejected: solid-fill chips — louder on dark with several labels. Rejected: dot + name — reads as metadata, not a label.)*
4. **Labels show on the board card fronts, not only in the task view.** Each card chip renders its labels as compact soft-tint chips above the title — at-a-glance scanning, names visible (the a11y rule holds on the board too). *(A space-efficient colour-bars-only front is deferred as a per-user setting — see Out of scope.)*
5. **Colour stored as a palette key.** The `Colour` field holds a key (`"mint"`, `"cyan"`, `"amber"`, `"rose"`, `"lilac"`, `"slate"`), validated server-side against the curated set. The actual colour values live in CSS / the design-system, not the database — a re-theme never touches data, and an invalid colour can't be stored.
6. **Deleting a label confirms.** A label delete is board-wide and has no undo in Slice 1, so it gets a confirm dialog ("Delete 'Urgent'? It will be removed from every card that uses it."), consistent with the slice spec reserving confirms for big destructive actions. Attach, detach, create and rename/recolour are all instant.
7. **Creating a label from a card auto-attaches it** to that card — you are clearly labelling the card in front of you.

## Data model

Two new tables complete `Board ──1:*── Label` and `Card ──*:*── Label`:

| Entity | Fields |
|---|---|
| **Label** | Id (PK) · BoardId (FK) · Name · Colour |
| **CardLabel** | CardId (FK) · LabelId (FK) — composite PK |

- **Relationships & cascade.** `Board.Labels` navigation + required `Label.BoardId` FK → deleting a board removes its labels. `CardLabel` is the join: deleting a **label** removes its join rows (detached from every card); deleting a **card** removes its join rows. No orphans, EF default cascade throughout (mirrors the `Board.Lists` / `List.Cards` convention).
- **Colour** — a palette key string, validated against the fixed set of six; never a raw hex.
- **Name** — trimmed, required, ≤ 50 chars. Duplicate names and duplicate colours are allowed (the name distinguishes them); no uniqueness constraint — matches how lists/cards already behave.
- **Board scope on attach** — a card may only carry labels from its own board; the attach endpoint rejects a label from another board.
- No label ordering and no per-card label cap — YAGNI for Slice 1 (labels list in creation order, by Id).

## API surface

New `ILabelRepository` seam (mirrors Board / List / Card), validated-endpoint style (trim + length guard, fail-closed bodyless errors):

- `GET    /api/boards/{boardId}/labels` → 200 `[ { id, name, colour }, … ]` (404 missing board) — the board's palette
- `POST   /api/boards/{boardId}/labels` `{ name, colour }` → 201 with the label (400 blank / over-long name / colour not in palette, 404 missing board)
- `PUT    /api/labels/{id}` `{ name, colour }` → 204 (400 / 404) — rename / recolour
- `DELETE /api/labels/{id}` → 204 (404) — delete + cascade-detach from all cards
- `POST   /api/cards/{cardId}/labels` `{ labelId }` → 204 (404 missing card/label, 400 cross-board) — attach; **idempotent** (a no-op 204 if already attached)
- `DELETE /api/cards/{cardId}/labels/{labelId}` → 204 — detach; **idempotent** (a not-attached or missing pair is a benign no-op 204, so a double-click or stale state never surfaces an error)

**Reading labels — nested in existing detail responses:**

- `GET /api/boards/{id}` gains a board-level `labels` palette and a `labelIds` array on each card, so the board screen renders card-front chips in its existing single request:
  ```
  GET /api/boards/{id} → { id, title, labels: [ { id, name, colour }, … ],
                           lists: [ { …, cards: [ { id, title, dueDate, position, labelIds: [..] }, … ] } ] }
  ```
- `GET /api/cards/{id}` gains `boardId` (so the picker can fetch the palette) and the card's attached `labels` (full `{ id, name, colour }` for immediate display).

## Frontend — board chips + task-view picker

- **Board view (`js/board/`).** Card chips resolve their `labelIds` against the board `labels` palette and render compact soft-tint chips above the title. Purely additive to the Plan 3 chip; the board model already loads board detail in one request. Any `labelId` not found in the palette is skipped defensively — never rendered as `undefined`.
- **Task view (`js/card/`).** A new **Labels** section shows the card's attached chips and a **"＋ Labels"** button that opens an inline **picker popover**:
  - the board palette as a list, each row a toggle (attached / not) with its colour + name;
  - **"＋ Create label"** — a name field + the six-swatch palette picker (the first swatch preselected, so a colour is always chosen); on create the label is added to the board and **auto-attached** to this card;
  - per-label **edit** (rename / recolour) and **delete** (with the confirm from decision 6).
  - The picker is its own focused unit (a small view + the label API calls), mounted by the card controller, so `card/view.js` does not bloat — the same "one focused unit per concern" instinct that split the earlier modules.
- **Server stays authoritative** — attach / detach / create / edit / delete each re-fetch (card detail and/or palette), as every Wend model already does.
- **Rendering & escaping.** Label names are user input, so every render site — board card-front chip, task-view chip, and picker row — escapes the name via the shared `escapeHtml` (the Plan 3 helper). Colour is applied by mapping the server-validated palette key to a fixed CSS class (`label-chip--{key}`), never interpolated into a `style` string — so even a bad stored value can't inject. This matters now as self-XSS, and more under Slice 2 sharing, where a label name comes from another user.
- **Accessibility.**
  - The picker is a **non-modal disclosure** (not a modal — consistent with Plan 3 rejecting modals): the "＋ Labels" trigger carries `aria-haspopup` + `aria-expanded`, opens the picker, and focus moves into it; **Escape and an outside click both close it and return focus to the trigger**; no focus trap.
  - Every control is keyboard-operable with an accessible name — palette toggles are real checkboxes/buttons named by their label; the per-label **edit / delete icon buttons are named "Edit label {name}" / "Delete label {name}"**, never icon-only; all picker targets are ≥ 44×44 px.
  - Attach / detach / create / rename / delete are announced via the existing `aria-live` region, count-free ("Added label Urgent." / "Removed label Urgent." / "Label deleted.").
  - Chips never rely on colour alone — the name is always rendered; colour is a `background` / `color` accent only.
  - **Contrast** — each of the six palette colours meets WCAG AA (≥ 4.5:1) as chip text on *both* the dark surface and the tinted chip background; any that fall short are lightened (slate is the one to check). Colours are design-system tokens and degrade gracefully under forced-colors (chip border + name remain).
- **Layout (mobile-first).** Chips wrap; the picker is full-width within the task view on phones and a contained popover at `min-width: 768px`. No new breakpoints beyond the existing tablet/desktop ones.

## Testing (NUnit — same split as Plans 1–3)

- **Repository** (`EfLabelRepository`, throwaway SQLite): create returns a board-scoped label; get-for-board lists in creation order; edit updates name / colour; delete removes the label; **deleting a label cascades its join rows**; attach links a card and a label; **attach is idempotent** (twice = one row, still succeeds); detach unlinks; **detach is a no-op when not attached** (still succeeds); **deleting a card removes its join rows**; **deleting a board removes its labels and their joins**; attach across boards is rejected; an invalid colour key is rejected.
- **API** (via `WendApiFactory`, throwaway DB): list palette (200 / 404 board); POST creates (201), 400s blank / over-long name and bad colour, 404s missing board; PUT edits (204 / 400 / 404); DELETE (204 / 404); attach (204), 400 cross-board, 404 missing card/label; detach (always 204 — idempotent, including a not-attached pair); nested `GET /api/boards/{id}` returns the palette + each card's `labelIds`; `GET /api/cards/{id}` returns `boardId` + attached labels.
- **Frontend:** manual browser + screen-reader pass — create a label from a card (auto-attached), attach/detach existing ones, rename + recolour, delete (confirm + announcement), and confirm chips render on the board front and the task view with names + focus management across opening/closing the picker.

## Out of scope (later plans / backlog)

- **Per-user "chips ↔ colour bars" board-front setting** (Malin's idea) — both display styles, user picks; deferred to `docs/backlog.md`. Plan 4 ships the chips front only.
- Label reorder, label search / filtering the board by label — later.
- The checklist (the other half of card detail) — its own next increment.
- Move / Done / undo-delete remain Plans 5 / 6 / 7.

## Definition of done

- `Label` + `CardLabel` entities; `ILabelRepository` / `EfLabelRepository` behind the seam; colour validated against the curated palette.
- Cascade is clean: delete a board → labels + joins; delete a label → joins; delete a card → joins. No orphans.
- All six label endpoints live and validated; cross-board attach rejected; `GET /api/boards/{id}` nests the palette + per-card `labelIds`; `GET /api/cards/{id}` returns `boardId` + attached labels.
- From the browser: create a label on a card (auto-attached), attach/detach, rename/recolour, delete (with confirm) — all persisted across a restart, chips visible on the board front and the task view.
- Soft-tint chips from design-system tokens (each AA as chip text); names escaped and always shown; non-modal picker — keyboard-operable, named controls, focus return, count-free `aria-live` announcements; forced-colors safe.
- NUnit green (repository + API), `dotnet build` clean (0 warnings); tests never touch the real DB.
- One local `%LOCALAPPDATA%\Wend\data.db` reset before first run (new tables; `EnsureCreated` doesn't migrate).
