<div align="center">
  <img src="docs/brand/wend-readme-header.png" alt="Wend — open-source, accessible, dark-mode-first kanban" width="640">
</div>

# Wend

[![CI](https://github.com/wendhq/wend/actions/workflows/ci.yml/badge.svg)](https://github.com/wendhq/wend/actions/workflows/ci.yml)

A free, open-source, accessible, dark-mode-first kanban board — a calm alternative to Trello. Built by Malin Fossum and Henry Elendheim as a learning project for GET Prepared.

## Status

**Slice 2a — accounts and identity (in progress). Slice 1 — local single-user board (complete).**

Wend now has real accounts. You register with an email address and a display name, confirm the
address from an emailed link, sign in, and every board belongs to you and nobody else — a board you
do not own answers 404, not 403. A forgotten password is recoverable: request a link, set a new
password, and every live session for that account is signed out.

- **Done in 2a:** PostgreSQL and EF migrations · ASP.NET Core Identity with cookie sessions ·
  registration and email confirmation · sign in, sign out, lockout · forgot and reset password ·
  per-user ownership on every board, list, card, label and checklist endpoint.
- **Next in 2a:** account settings (change email and password, remember-me), account deletion,
  security hardening (rate limiting, antiforgery, HTTPS/HSTS), then deployment.
- **Then:** Slice 2b — sharing, board membership and invitations.

> Wend is still **localhost only** and is not deployed. `/api/auth/*` is not yet rate limited, and
> that is a deliberate, tracked gate: security hardening lands before Wend is ever exposed. See
> [`docs/backlog.md`](docs/backlog.md).

Slice 2a design specs: [`docs/2026-07-08-wend-slice2a-accounts-design.md`](docs/2026-07-08-wend-slice2a-accounts-design.md) (the parent spec), [`docs/2026-08-06-wend-slice2a-plan2-ownership-design.md`](docs/2026-08-06-wend-slice2a-plan2-ownership-design.md), [`docs/2026-08-10-wend-slice2a-plan4-login-design.md`](docs/2026-08-10-wend-slice2a-plan4-login-design.md), [`docs/2026-08-11-wend-slice2a-plan5-reset-design.md`](docs/2026-08-11-wend-slice2a-plan5-reset-design.md)

Slice 2a build plans: [`docs/plans/2026-07-11-slice2a-postgres-native.md`](docs/plans/2026-07-11-slice2a-postgres-native.md), [`docs/plans/2026-08-06-slice2a-identity-ownership.md`](docs/plans/2026-08-06-slice2a-identity-ownership.md), [`docs/plans/2026-08-10-slice2a-register-verify.md`](docs/plans/2026-08-10-slice2a-register-verify.md), [`docs/plans/2026-08-10-slice2a-plan4-login.md`](docs/plans/2026-08-10-slice2a-plan4-login.md), [`docs/plans/2026-08-11-slice2a-plan5-reset.md`](docs/plans/2026-08-11-slice2a-plan5-reset.md)

### Slice 1 — local single-user board (complete)

Boards, lists, and cards work end to end — create, rename, delete, and reorder lists inside a board, add cards to a list, move a card within its list or to another list, label them, delete a card with a one-click undo, open a card into a focused task view with an Edit mode, keep a per-card checklist (add, rename, reorder, check off into a collapsible Done strip, delete with undo) with progress shown on the board's card chips, and tune it all in a small settings screen — saved to PostgreSQL, accessible and dark-mode-first.

- **Done:** the board, list, card, label, and checklist backend (JSON APIs behind `IBoardRepository`, `IListRepository`, `ICardRepository`, `ILabelRepository`, and `IChecklistItemRepository` seams, EF Core + PostgreSQL with migrations, localhost-only) and the vanilla-JS MVC frontend (board-view navigation, accessible list reordering, card chips with a focused task view, accessible card moving with up/down buttons and a move-to-list dropdown, an inline label picker with soft-tint chips, a per-card checklist with a Done strip and chip progress bars, an undo-first delete for cards and checklist items with a transient "Deleted · Undo" toast, a task-view Edit mode, a localStorage settings screen gating the card Done checkboxes and the Delete card button, screen-reader announcements, keyboard focus management, per-list Done strips, and a mobile single-list switcher).

Slice 1 design specs: [`docs/2026-06-15-wend-slice1-design.md`](docs/2026-06-15-wend-slice1-design.md), [`docs/2026-06-19-wend-lists-design.md`](docs/2026-06-19-wend-lists-design.md), [`docs/2026-06-22-wend-cards-design.md`](docs/2026-06-22-wend-cards-design.md), [`docs/2026-06-23-wend-labels-design.md`](docs/2026-06-23-wend-labels-design.md), [`docs/2026-06-24-wend-card-moving-design.md`](docs/2026-06-24-wend-card-moving-design.md), [`docs/2026-06-25-wend-done-design.md`](docs/2026-06-25-wend-done-design.md), [`docs/2026-07-07-wend-delete-undo-design.md`](docs/2026-07-07-wend-delete-undo-design.md), [`docs/2026-07-07-wend-checklist-design.md`](docs/2026-07-07-wend-checklist-design.md), [`docs/2026-07-08-wend-mobile-a11y-polish-design.md`](docs/2026-07-08-wend-mobile-a11y-polish-design.md)

Slice 1 build plans: [`docs/plans/2026-06-16-slice1-foundation-boards.md`](docs/plans/2026-06-16-slice1-foundation-boards.md), [`docs/plans/2026-06-19-slice1-lists.md`](docs/plans/2026-06-19-slice1-lists.md), [`docs/plans/2026-06-22-slice1-cards.md`](docs/plans/2026-06-22-slice1-cards.md), [`docs/plans/2026-06-23-slice1-labels.md`](docs/plans/2026-06-23-slice1-labels.md), [`docs/plans/2026-06-24-slice1-card-moving.md`](docs/plans/2026-06-24-slice1-card-moving.md), [`docs/plans/2026-06-25-slice1-done.md`](docs/plans/2026-06-25-slice1-done.md), [`docs/plans/2026-07-07-slice1-delete-undo.md`](docs/plans/2026-07-07-slice1-delete-undo.md), [`docs/plans/2026-07-07-slice1-checklist.md`](docs/plans/2026-07-07-slice1-checklist.md), [`docs/plans/2026-07-08-slice1-mobile-a11y-polish.md`](docs/plans/2026-07-08-slice1-mobile-a11y-polish.md)

## Stack

- ASP.NET Core (`net10.0`) — minimal API, localhost only
- ASP.NET Core Identity with cookie sessions, behind hand-written `/api/auth/*` endpoints — no
  scaffolded Identity UI, so the frontend keeps its no-build-step, accessibility-first character
- EF Core → PostgreSQL for storage (EF migrations), behind an `IBoardRepository` seam
- Vanilla-JavaScript MVC frontend, served from `wwwroot`
- NUnit tests — 253 of them

## Structure

| Project | Responsibility |
|---|---|
| `Wend.Core` | Board domain, `WendUser`, the repository seams, EF Core data access |
| `Wend.Api` | Minimal API endpoints (boards and `/api/auth/*`), Identity and cookie configuration; serves the frontend |
| `Wend.Tests` | NUnit tests covering Core and the API |

## Run it

Wend stores data in **PostgreSQL**. Install a local server once — a normal Windows service, no Docker:

```
winget install --exact --id PostgreSQL.PostgreSQL.17
```

Set the `postgres` password to `postgres` and keep port `5432`. Store the dev connection string once:

```
dotnet user-secrets set "ConnectionStrings:WendDb" "Host=localhost;Port=5432;Database=wend;Username=postgres;Password=postgres" --project Wend.Api
```

Wend reads that secret only in the Development environment, and it **refuses to start outside it**
until a real email provider is configured — an auth system that cannot send mail should not boot.
There is no `launchSettings.json`, so set the environment explicitly:

```
$env:ASPNETCORE_ENVIRONMENT = "Development"   # PowerShell
dotnet run --project Wend.Api
```

Without it you get `ConnectionStrings:WendDb is not configured`, which looks like a missing secret
and is really a missing environment variable.

Then open http://127.0.0.1:5174. You will land on the sign-in screen, because every board belongs to
a user now.

### Making an account locally

There is no email provider in development. Wend writes every link it would have sent to a file:

```
%LOCALAPPDATA%\Wend\auth-emails.log
```

Register at `/register`, open that file, and follow the newest `/verify?...` link to confirm the
address — then sign in. `/forgot-password` works the same way: request a link, read it out of the
same file, and follow it. Confirmation links last 24 hours, reset links one hour.

Once signed in you can create boards, open one to manage its lists (create, rename, delete,
reorder), add cards and move them within or between lists, and open a card for its task view to edit
the title, notes, due date, labels, and a per-card checklist. The API lives under `/api/auth`,
`/api/boards`, `/api/lists`, `/api/cards`, `/api/labels`, and `/api/checklist-items`. The schema is
created and kept current by EF Core migrations on startup.

## Tests

The API integration tests need the local PostgreSQL server running (each creates a throwaway database on it); the repository unit tests run on in-memory SQLite.

```
dotnet test
```

## License

[MIT](LICENSE) © 2026 Malin Fossum and Henry Elendheim
