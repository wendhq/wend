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
