<div align="center">
  <img src="docs/brand/wend-readme-header.png" alt="Wend — open-source, accessible, dark-mode-first kanban" width="640">
</div>

# Wend

[![CI](https://github.com/wendhq/wend/actions/workflows/ci.yml/badge.svg)](https://github.com/wendhq/wend/actions/workflows/ci.yml)

A free, open-source, accessible, dark-mode-first kanban board — a calm alternative to Trello. Built by Malin Fossum and Henry Elendheim as a learning project for GET Prepared.

## Status

**Slice 1 — local single-user board (in progress).**

Boards work end to end — create, rename, and delete from the browser, saved to SQLite — accessible and dark-mode-first.

- **Done:** the board backend (full CRUD as a JSON API behind an `IBoardRepository` seam, EF Core + SQLite, 14 NUnit tests, localhost-only) and the vanilla-JS MVC frontend (labelled controls, screen-reader announcements, keyboard focus management).
- **Next:** lists and cards.

Design spec: [`docs/2026-06-15-wend-slice1-design.md`](docs/2026-06-15-wend-slice1-design.md) · Build plan: [`docs/plans/2026-06-16-slice1-foundation-boards.md`](docs/plans/2026-06-16-slice1-foundation-boards.md)

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

Then open http://127.0.0.1:5174 to create, rename, and delete boards. The board API lives under `/api/boards`. On first run the SQLite database is created at `%LOCALAPPDATA%\Wend\data.db`.

## Tests

```
dotnet test
```

## License

[MIT](LICENSE) © 2026 Malin Fossum and Henry Elendheim
