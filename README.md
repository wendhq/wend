<div align="center">
  <img src="docs/brand/wend-readme-header.png" alt="Wend — open-source, accessible, dark-mode-first kanban" width="640">
</div>

# Wend

[![CI](https://github.com/wendhq/wend/actions/workflows/ci.yml/badge.svg)](https://github.com/wendhq/wend/actions/workflows/ci.yml)

A free, open-source, accessible, dark-mode-first kanban board — a calm alternative to Trello. Built by Malin Fossum and Henry Elendheim as a learning project for GET Prepared.

## Status

**Slice 1 — local single-user board (in progress).**

Boards, lists, and cards work end to end — create, rename, delete, and reorder lists inside a board, add cards to a list, move a card within its list or to another list, label them, delete a card with a one-click undo, open a card into a focused task view with an Edit mode, keep a per-card checklist (add, rename, reorder, check off into a collapsible Done strip, delete with undo) with progress shown on the board's card chips, and tune it all in a small settings screen — saved to SQLite, accessible and dark-mode-first.

- **Done:** the board, list, card, label, and checklist backend (JSON APIs behind `IBoardRepository`, `IListRepository`, `ICardRepository`, `ILabelRepository`, and `IChecklistItemRepository` seams, EF Core + SQLite, 147 NUnit tests, localhost-only) and the vanilla-JS MVC frontend (board-view navigation, accessible list reordering, card chips with a focused task view, accessible card moving with up/down buttons and a move-to-list dropdown, an inline label picker with soft-tint chips, a per-card checklist with a Done strip and chip progress bars, an undo-first delete for cards and checklist items with a transient "Deleted · Undo" toast, a task-view Edit mode, a localStorage settings screen gating the card Done checkboxes and the Delete card button, screen-reader announcements, keyboard focus management).
- **Next:** mobile + accessibility polish (per-list Done strips and a single-list phone view).

Design specs: [`docs/2026-06-15-wend-slice1-design.md`](docs/2026-06-15-wend-slice1-design.md), [`docs/2026-06-19-wend-lists-design.md`](docs/2026-06-19-wend-lists-design.md), [`docs/2026-06-22-wend-cards-design.md`](docs/2026-06-22-wend-cards-design.md), [`docs/2026-06-23-wend-labels-design.md`](docs/2026-06-23-wend-labels-design.md), [`docs/2026-06-24-wend-card-moving-design.md`](docs/2026-06-24-wend-card-moving-design.md), [`docs/2026-06-25-wend-done-design.md`](docs/2026-06-25-wend-done-design.md), [`docs/2026-07-07-wend-delete-undo-design.md`](docs/2026-07-07-wend-delete-undo-design.md), [`docs/2026-07-07-wend-checklist-design.md`](docs/2026-07-07-wend-checklist-design.md) 

Build plans: [`docs/plans/2026-06-16-slice1-foundation-boards.md`](docs/plans/2026-06-16-slice1-foundation-boards.md), [`docs/plans/2026-06-19-slice1-lists.md`](docs/plans/2026-06-19-slice1-lists.md), [`docs/plans/2026-06-22-slice1-cards.md`](docs/plans/2026-06-22-slice1-cards.md), [`docs/plans/2026-06-23-slice1-labels.md`](docs/plans/2026-06-23-slice1-labels.md), [`docs/plans/2026-06-24-slice1-card-moving.md`](docs/plans/2026-06-24-slice1-card-moving.md), [`docs/plans/2026-06-25-slice1-done.md`](docs/plans/2026-06-25-slice1-done.md), [`docs/plans/2026-07-07-slice1-delete-undo.md`](docs/plans/2026-07-07-slice1-delete-undo.md), [`docs/plans/2026-07-07-slice1-checklist.md`](docs/plans/2026-07-07-slice1-checklist.md)

## Stack

- ASP.NET Core (`net10.0`) — minimal API, localhost only
- EF Core → SQLite for storage, behind an `IBoardRepository` seam
- Vanilla-JavaScript MVC frontend, served from `wwwroot`
- NUnit tests

## Structure

| Project | Responsibility |
|---|---|
| `Wend.Core` | Board domain, the `IBoardRepository` seam, EF Core data access |
| `Wend.Api` | Minimal API endpoints; serves the frontend |
| `Wend.Tests` | NUnit tests covering Core and the API |

## Run it

```
dotnet run --project Wend.Api
```

Then open http://127.0.0.1:5174 to create boards, open one to manage its lists (create, rename, delete, reorder), add cards and move them within or between lists — open a card for its task view to edit the title, notes, due date, labels, and a per-card checklist. The API lives under `/api/boards`, `/api/lists`, `/api/cards`, `/api/labels`, and `/api/checklist-items`. On first run the SQLite database is created at `%LOCALAPPDATA%\Wend\data.db`.

## Tests

```
dotnet test
```

## License

[MIT](LICENSE) © 2026 Malin Fossum and Henry Elendheim
