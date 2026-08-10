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
- **Revisit when:** wanted — the blocker is gone: the checklist increment shipped a Settings
  screen (`js/prefs.js` + `js/settings/`), so this toggle now has a home to hang on.
- **Decided:** 2026-06-23 (Malin).

### Multi-card batch undo — restore several deleted cards at once

- **Now:** deleting a card shows a transient "Deleted · Undo" toast that restores that one card (Plan 7). Deleting several in quick succession **replaces** the toast each time, so only the most recent delete is undoable from the toast; earlier cards stay soft-deleted (recoverable later via the Trash screen).
- **Later:** let undo bring back **all** recently-deleted cards at once — e.g. a coalescing "Deleted N · Undo" toast whose Undo loops `POST /api/cards/{id}/restore` over the batch, each card returning to its original position (restore already clamps to the stored slot, so restoring in delete-order reconstructs the arrangement).
- **Why deferred:** it reverses the Plan 7 "one toast, replaces" call (chosen for a11y simplicity) and wants its own brainstorm + a fresh accessibility pass on the multi-item toast; it also overlaps the planned **Trash** screen, which already covers multi-card recovery. No data is at risk meanwhile — every delete is a soft-delete row.
- **Revisit when:** the Trash slice is scoped (fold it in there), or single-undo proves insufficient in daily use.
- **Decided:** 2026-07-07 (Malin & Henry, Plan 7 acceptance).

### Undo for checklist-item deletes

- **Resolved (checklist increment, feature/checklist):** shipped as specced — checklist items
  soft-delete (`DeletedAt` + query filter) with a "Deleted · Undo" toast that restores in place,
  sharing the cards' toast primitive and retention behaviour.
- **Originally decided:** 2026-07-07 (Malin & Henry, Plan 7 acceptance).

### Unmatched `/api/*` routes returned the SPA shell instead of 404

- **Resolved (2026-08-07, before Plan 3):** `app.Map("/api/{**path}", () => Results.NotFound())` now sits between the API endpoints and `MapFallbackToFile` in [`Program.cs`](../Wend.Api/Program.cs). Literal segments outrank a catch-all, so every real endpoint still matches first, and deep links like `/boards/42` still reach the shell.
- **The trap:** `MapFallbackToFile("index.html")` claims *everything* no endpoint matched, `/api/*` included. A typo'd or not-yet-wired API route therefore answered `200 text/html`. Because `api()` in `js/api.js` treats any 2xx as success and calls `res.json()`, the client failed with `JsonException: input does not contain any JSON tokens` — the same misleading symptom that cost 38 red tests in Plan 2 — and any check of the form "did this 401?" read the shell as success.
- **Why it was fixed rather than deferred:** Plan 3 adds `/api/auth/*` and a frontend auth gate keyed on 401, so this would have misfired exactly where it is hardest to diagnose.
- **Keep the catch-all.** Deleting it silently restores the old behaviour; `ApiSmokeTests.An_unmatched_api_route_is_404_not_the_shell` is the guard.
- **Found:** 2026-08-06 (Plan 2 Task 8 401 sweep).

### Dev static-file caching — a no-cache header for local development

- **Now:** `UseStaticFiles` sends no `Cache-Control`, so a dev browser can serve stale JS/CSS from earlier `127.0.0.1:5174` sessions and a normal reload won't revalidate ES-module imports — two "bugs" in the 2026-07-08 a11y sweep were cache ghosts. Workaround: hard-reload / disable cache before every browser check.
- **Later:** a dev-only middleware that sets `Cache-Control: no-cache` on static files (Development environment only), so reloads always revalidate.
- **Why deferred:** out of Plan 8's frontend-only scope; the hard-reload habit is a working stopgap and this touches server startup config.
- **Revisit when:** the next housekeeping pass, or if stale-cache ghosts keep biting acceptance runs.
- **Decided:** 2026-07-08 (Malin, Plan 8).

### Register leaks account existence through timing

- **Now:** `POST /api/auth/register` returns the same `204` whether or not the address is taken, but the taken path skips password hashing and so returns measurably faster.
- **Later:** dummy-hash the skipped path, as login will.
- **Why deferred:** the spec requires equalised timing for *login*; register was left as-is because the app is unreachable from another machine until deployment.
- **Revisit when:** **Plan 8 (security hardening) must close this.**
- **Decided:** 2026-08-10 (Slice 2a Plan 3).

### `/api/auth/*` is not rate limited

- **Now:** neither register nor resend-verification is rate limited. Both trigger outbound email, so both are email-bombing vectors, and register is a credential-stuffing surface.
- **Later:** rate limiting across `/api/auth/*`.
- **Why deferred:** deferred to Plan 8 per the spec's sequencing; the endpoints are unreachable from another machine until deployment.
- **Revisit when:** **This is a launch gate: Plan 9 must not deploy before Plan 8 lands.**
- **Decided:** 2026-08-10 (Slice 2a Plan 3).

### The registration form gives no Art. 13 notice

- **Now:** Wend collects an email address and a display name from members of the public, with no privacy policy or terms linked from the registration form.
- **Later:** the spec makes the privacy policy and terms a launch deliverable, linked *from the registration form*.
- **Why deferred:** it is lawful today only because registration is unreachable.
- **Revisit when:** **Launch gate for Plan 9: policy and terms exist, and the form links to them, before public sign-up opens.**
- **Decided:** 2026-08-10 (Slice 2a Plan 3).

### Verify tokens travel in a query string

- **Now:** the emailed link carries `userId` and `code` as query parameters. The SPA strips them from the address bar with `history.replaceState`, and Kestrel logs nothing at Information.
- **Later:** exclude `/verify` query strings from access logging.
- **Why deferred:** essentially every reverse proxy logs query strings by default, and there is no proxy until deployment.
- **Revisit when:** **Plan 9 must do this**, per the spec's "path-logging exclusion extends to query strings".
- **Decided:** 2026-08-10 (Slice 2a Plan 3).

### Inactive-account retention has no stance

- **Now:** no retention period is set for inactive (confirmed) accounts. The 7-day purge covers *unverified* accounts only.
- **Later:** a retention period, or a documented decision to keep accounts indefinitely.
- **Why deferred:** the spec allows setting one at plan time *or* explicitly deferring; this is a legal-posture decision that belongs with the privacy policy rather than with registration code.
- **Revisit when:** **Plan 9 decides it.**
- **Decided:** 2026-08-10 (Slice 2a Plan 3).
