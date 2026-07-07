# Wend — backlog & deferred decisions

Things we've consciously chosen to do *later*, each with the reason and the trigger for revisiting. Keeps the "not now, but not forgotten" list out of our heads.

## Deferred decisions

### Design-system distribution — promote to its own repo + git submodule

- **Now:** Wend vendors a committed copy of the shared design-system in `Wend.Api/wwwroot/design-system`, refreshed with [`sync-design-system.ps1`](../sync-design-system.ps1).
- **Later:** extract the design-system into its own git repo and consume it as a git **submodule** across projects, so updates propagate through git instead of a manual re-copy.
- **Why deferred:** at two-person / few-project scale, the submodule overhead (recurse-submodule clones, two-step pointer updates, a concept every contributor must learn) costs more than the occasional copy saves.
- **Revisit when:** the same bundle is maintained across several active projects and keeping the copies in sync becomes a real chore.
- **Decided:** 2026-06-18 (Malin).

### NU1903 / CVE-2025-6965 — accept the unpatched transitive SQLite advisory

- **Now:** `Microsoft.EntityFrameworkCore.Sqlite` pulls in the native package `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which carries a High-severity SQLite memory-corruption advisory ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) / CVE-2025-6965). Suppressed solution-wide in [`Directory.Build.props`](../Directory.Build.props) via `NuGetAuditSuppress`.
- **Why accept it:** there is no fix to take — 2.1.11 is the last 2.1.x build (the advisory lists *"Patched versions: None"*), and the only newer build is the major release 3.0.x, which EF Core 10 was not built against. The flaw is also unreachable in Wend: the app is localhost-only and single-user, and every query is EF-generated from typed LINQ, so there is no way to run the attacker-controlled aggregate SQL the CVE requires.
- **Revisit when:** EF Core ships on a patched SQLitePCLRaw (or a patched 2.1.x / safe 3.x is released) — then bump it and delete the suppression. Re-check with `dotnet list package --vulnerable --include-transitive`.
- **Decided:** 2026-06-19 (Malin & Henry).

### Label display — per-user choice of chips vs colour bars on the board front

- **Now:** the board card-front shows labels as full soft-tint **chips** (name + colour) — the accessible default shipped in Plan 4.
- **Later:** a per-user **setting** to switch the board front to compact **colour bars** (Trello-style, space-efficient); the user picks which they prefer, and the task view keeps full chips either way.
- **Why deferred:** colour-bars-only makes colour the sole *visible* signal, so it needs the label names carried in screen-reader text **and** a real settings surface to store the preference — neither exists in Slice 1, and chips are the safe default to ship first.
- **Revisit when:** Slice 1 grows a settings / preferences home to hang it on (or users ask for a denser board).
- **Decided:** 2026-06-23 (Malin).

### Multi-card batch undo — restore several deleted cards at once

- **Now:** deleting a card shows a transient "Deleted · Undo" toast that restores that one card (Plan 7). Deleting several in quick succession **replaces** the toast each time, so only the most recent delete is undoable from the toast; earlier cards stay soft-deleted (recoverable later via the Trash screen).
- **Later:** let undo bring back **all** recently-deleted cards at once — e.g. a coalescing "Deleted N · Undo" toast whose Undo loops `POST /api/cards/{id}/restore` over the batch, each card returning to its original position (restore already clamps to the stored slot, so restoring in delete-order reconstructs the arrangement).
- **Why deferred:** it reverses the Plan 7 "one toast, replaces" call (chosen for a11y simplicity) and wants its own brainstorm + a fresh accessibility pass on the multi-item toast; it also overlaps the planned **Trash** screen, which already covers multi-card recovery. No data is at risk meanwhile — every delete is a soft-delete row.
- **Revisit when:** the Trash slice is scoped (fold it in there), or single-undo proves insufficient in daily use.
- **Decided:** 2026-07-07 (Malin & Henry, Plan 7 acceptance).

### Undo for checklist-item deletes

- **Now:** the per-card checklist isn't built yet (it's the next increment); undo-first delete applies to cards only.
- **Later:** when the checklist ships, deleting a checklist item ("task") should be undoable the same way a card is — soft-delete + a "Deleted · Undo" toast (or the batch mechanism above) — not an immediate hard delete.
- **Why deferred:** there's nothing to undo until the checklist exists; this is a requirement to bake into the **checklist increment**, not a Plan 7 change.
- **Revisit when:** building the per-card checklist — carry this into its spec.
- **Decided:** 2026-07-07 (Malin & Henry, Plan 7 acceptance).
