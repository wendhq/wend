# Wend — Slice 1 design spec

- **Date:** 2026-06-15
- **Status:** Signed off (2026-06-16) — ready for paired setup
- **Owners:** Malin & Henry (equal ownership)
- **Repo:** `github.com/wendhq/wend`

---

## Context — why we're building this

Two students in GET Prepared need a project-management / kanban tool for the coming autumn and would rather not lean on Trello, whose genuinely useful features sit behind a paywall. **Wend** is our free, open-source, accessible alternative.

It serves three goals at once:

1. **A real tool** we'll use every day.
2. **A portfolio piece** to show future employers.
3. **A learning vehicle** for the C#, API, and backend practice we both want.

The strategy is to ship a tight, genuinely usable **Slice 1**, then grow it in slices — the same way the *Tidsro* project was built. A clean working thing with a visible roadmap is also the strongest possible portfolio story.

---

## Goals & non-goals

**Slice 1 goals**

- A genuinely usable, local, single-user kanban board.
- Dark-mode-first, accessible, mobile-first from day one.
- Clean architecture we can both understand and explain in an interview.

**Slice 1 non-goals** (deliberately deferred — see roadmap)

- Accounts, multi-user, sharing, real-time sync.
- Drag-and-drop.
- Dedicated Archive / Trash *screens* (the underlying data ships now; the screens come later).
- Outline / tree view.
- Comments, attachments, cloud deploy.

---

## Product overview

Wend is a **local-first** application: one ASP.NET Core app you run on your own machine, with your boards saved to a SQLite database on disk. The web frontend is served by that same app — one thing to run, no separate servers.

Multi-user sharing is the headline of Slice 2; Slice 1 is architected so it drops in later rather than forcing a rewrite.

---

## Slice 1 scope

- **Boards → Lists → Cards**, with full create / edit / delete.
- **Card detail:** title, description (free-text notes), due date, labels, and an in-card **checklist**.
- **Done checkmark:** checking a card sets it done and tucks it into a minimal, collapsible **Done** section — out of the active flow but still visible, one tap to expand or un-check. Shown two ways from the same data: a **global Done area** on the board overview, and a **per-list Done strip** when a single list is in focus (the default on mobile).
- **Accessible move:** reorder cards within a list and move them between lists using **buttons + keyboard** (not drag-and-drop). One underlying "move" operation.
- **Undo-first delete:** deleting a card soft-deletes it and shows a **"Deleted · Undo"** toast. A confirm dialog is reserved only for big destructive actions (deleting a whole list or board).
- **Mobile-first:** phone shows one list at a time with a switcher, plus a full-screen task view; layers up to the familiar multi-column board on tablet/desktop via `min-width` queries.
- **Accessibility & dark mode** inherited from the shared design-system (focus rings, reduced-motion, forced-colors, skip link), plus keyboard operation and screen-reader announcements throughout.

---

## Architecture

One ASP.NET Core app (`net10.0`), mirroring the structure already proven in the **Kenaz** project.

| Layer | Project | Responsibility |
|---|---|---|
| Frontend | `Wend.Api/wwwroot` | Vanilla-JS MVC (model / view / controller) + bundled design-system; talks to the API over `fetch` (JSON) |
| Web API | `Wend.Api` | Minimal API endpoints; serves the frontend; SPA fallback to `index.html` |
| Domain | `Wend.Core` | Board rules, the `IBoardRepository` seam, EF Core `DbContext` |
| Tests | `Wend.Tests` | NUnit, covering Core + Api |

```
Board UI (wwwroot, vanilla JS MVC + design-system)
        │  fetch() · JSON
   Wend.Api  (minimal API: /api/boards, /api/cards, …)
        │
   Wend.Core (board logic + IBoardRepository seam)
        │  EF Core
   SQLite database (data.db)
```

**Storage seam.** All persistence sits behind `IBoardRepository`. Slice 1 implements it with **EF Core → SQLite**, the database file living at `%LOCALAPPDATA%\Wend\data.db` (user AppData — survives rebuilds, never git-committed). Because the rest of the app only depends on the interface, storage can be swapped (e.g. back to a JSON file, or forward to another database) without touching board logic or the API.

---

## Data model

```
Board ──1:*── List ──1:*── Card ──*:*── Label   (via CardLabel join; Labels are defined per Board)
                              │
                              └──1:*── ChecklistItem
```

| Entity | Fields |
|---|---|
| **Board** | Id (PK) · Title |
| **List** | Id (PK) · BoardId (FK) · Title · Position |
| **Card** | Id (PK) · ListId (FK) · Title · Description? · DueDate? · Position · CreatedAt · CompletedAt? · ArchivedAt? · DeletedAt? |
| **Label** | Id (PK) · BoardId (FK) · Name · Colour |
| **CardLabel** | CardId (FK) · LabelId (FK) — join table |
| **ChecklistItem** | Id (PK) · CardId (FK) · Text · IsChecked · Position |

- `Position` (on List and Card) drives ordering, and is what the "move" operation changes.
- Card state is carried by three nullable timestamps — `CompletedAt`, `ArchivedAt`, `DeletedAt`. EF Core **query filters** keep archived and deleted cards out of normal board queries automatically.
- No `User` entity yet — that arrives with sharing in Slice 2.

---

## Card lifecycle

- **Active** → lives in a list.
- **Done** → checkmark sets `CompletedAt`; the card leaves the active flow into the collapsible **Done** section — the **global Done area** on the board overview, or a list's **own Done strip** when that single list is in focus. Un-checking clears `CompletedAt` and returns it to its place (it keeps its `ListId` + `Position`).
- **Archived** → `ArchivedAt` set; filed out of the board into the **Archive** (completed-work view + stats). *Archive screen = follow-up slice; the field ships in Slice 1.*
- **Deleted** → `DeletedAt` set (soft delete); the undo toast catches instant slips, and a **Trash** holds it for real recovery until emptied. *Trash screen = follow-up slice; the field + undo toast ship in Slice 1.*

---

## API surface (illustrative)

- Boards: `GET /api/boards` · `GET /api/boards/{id}` · `POST` · `PUT` · `DELETE`
- Lists: `POST /api/boards/{id}/lists` · `PUT /api/lists/{id}` · `DELETE /api/lists/{id}`
- Cards: `POST /api/lists/{id}/cards` · `GET /api/cards/{id}` · `PUT /api/cards/{id}` · `DELETE /api/cards/{id}` (soft) · `POST /api/cards/{id}/restore`
- **Move:** `PUT /api/cards/{id}/move` `{ listId, position }` — the single operation behind both the accessible move buttons and (later) drag-and-drop.
- Done: toggled via `PUT /api/cards/{id}` (sets/clears `CompletedAt`).
- Checklist: `POST /api/cards/{id}/checklist` · `PUT /api/checklist-items/{id}` · `DELETE /api/checklist-items/{id}`
- Labels: `GET`/`POST /api/boards/{id}/labels`; attach/detach on a card.

---

## Views & navigation

- **Board (overview)** — active cards in their lists, with a single collapsible **global Done area** rolling up everything completed across the board. On tablet/desktop: the multi-column board with the global Done area below the columns. On a phone: one list at a time via a segmented switcher — each list shows its **own Done strip** at the bottom, and the global rollup is its own segment in the switcher.
- **Task view** — focused card detail: done toggle, which list, due date, labels, description/notes, checklist, and an (always-undoable) delete. Full-screen on phone; modal/panel on desktop.
- **Outline / tree view** *(follow-up slice)* — a collapsible nested view of the whole project (board → lists → cards) as an accessibility-first alternative to the visual board, reusing the same model, API, and move operations.

---

## Accessibility commitments

- Dark-mode-first, design-system tokens; works in forced-colors and reduced-motion.
- **Everything keyboard-operable** — move cards, toggle done, edit, delete, navigate.
- Cards move via buttons/keyboard, never drag-only; the outline view is an additional accessible path.
- Screen-reader support: semantic markup, labelled controls, and an ARIA live region announcing moves and undo actions.
- Visible focus rings; mouse interaction never leaves a sticky focus state.

---

## Roadmap

| Slice | Delivers |
|---|---|
| **1** | The usable local board (this spec) |
| **Next (fast follow-ups)** | Outline/tree view · Archive + Trash screens + stats · drag-and-drop (layered on the move operation) |
| **2 — Sharing** *(headline)* | Accounts + a hosted server → shared boards, multi-user. Adds a `User` entity and authentication |
| **Later** | Real-time sync · comments · cloud deploy · polish |

---

## Collaboration & workflow

- **Equal ownership**, shared GitHub org **`wendhq`**, repo **`wend`**.
- **Pair on setup** (scaffold + shared model together, so the whole thing makes sense to both and Henry gets comfortable with the tooling), then **split by feature** end-to-end (model → API → UI).
- **branch → pull request → the other reviews & merges** — nothing lands without two sets of eyes.
- Tests in **NUnit**; TDD where it fits.
- Commits are authored by each person under their own account; no AI/co-author attribution in commit metadata.

---

## Key decisions (and why)

- **Local-first first** — fastest path to a usable tool and the cleanest way to learn the fundamentals; sharing becomes the Slice 2 headline rather than a day-one wall.
- **SQLite via EF Core** — chosen over a JSON file for the learning + portfolio value; it's the team's current curriculum topic. Kept behind `IBoardRepository` so it's swappable.
- **Done, shown two ways** — a **global Done area** on the board overview and a **per-list Done strip** when a single list is in focus; not a separate column, never vanishing. Same `CompletedAt` data grouped to fit the context — clean lists, visible wins.
- **Undo-first deletes** — gentler and more accessible than confirm-on-everything; confirm reserved for big destructive actions.
- **Accessible move buttons before drag-and-drop** — the move *operation* is identical to either input; build it once, add drag-and-drop as its own slice.
- **Mobile-first** — house convention; the board is designed phone-first and layered up.

---

## Decisions confirmed on review (2026-06-16)

1. **Done placement** — **both**: a global Done area on the board overview *and* a per-list Done strip when a single list is in focus. Same data, grouped to context; no model change.
2. **Checklists** — **one simple checklist per card** for Slice 1 (multiple named checklists deferred).
3. **Test framework** — **NUnit** (matching Kenaz).
4. **Database file location** — **user AppData**: `%LOCALAPPDATA%\Wend\data.db` — survives rebuilds, never git-committed.

---

*Signed off 2026-06-16. Next: a paired setup session — create the `wendhq` org and scaffold the repo together — followed by an implementation plan for Slice 1.*
