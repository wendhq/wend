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

- **The trap:** `UseStaticFiles` sends no `Cache-Control` at all, so a dev browser served stale JS/CSS from earlier `127.0.0.1:5174` sessions and a normal reload would not revalidate ES-module imports — two "bugs" in the 2026-07-08 a11y sweep were cache ghosts. The standing workaround was to hard-reload / disable cache before every browser check.
- **Resolved (2026-08-21):** Development sets `Cache-Control: no-cache` through
  `StaticFileOptions.OnPrepareResponse` in [`Program.cs`](../Wend.Api/Program.cs). `no-cache` still
  stores the response but revalidates, so an unchanged file costs a 304 rather than a resend.
  Production keeps the default. `MapFallbackToFile` takes the same options because it serves
  `index.html` through its own static-file pipeline — without them the shell alone would still come
  back from cache. Guarded by `ApiSmokeTests.Static_files_are_no_cache_in_development` and
  `The_shell_is_no_cache_in_development`, both verified red with the change reverted.
- **The hard-reload habit is no longer required in Development**, and the same housekeeping pass
  added `Wend.Api/Properties/launchSettings.json`, so `ASPNETCORE_ENVIRONMENT` no longer has to be
  set by hand before `dotnet run`.
- **Originally decided:** 2026-07-08 (Malin, Plan 8).

### Register leaks account existence through timing

- **Now:** `POST /api/auth/register` returns the same `204` whether or not the address is taken, but the taken path skips password hashing and so returns measurably faster. **`POST /api/auth/forgot-password` (Plan 5) has the same shape: the unknown-address branch generates no token and sends no mail, so it returns faster than a confirmed one.**
- **Later:** dummy-hash the skipped path on register, as login does, and equalise the forgot-password branches.
- **Why deferred:** the spec requires equalised timing for *login*; register was left as-is because the app is unreachable from another machine until deployment.
- **Revisit when:** **Plan 8 (security hardening) must close this.**
- **Decided:** 2026-08-10 (Slice 2a Plan 3).
- **Extended 2026-08-13 (Plan 6):** `POST /api/auth/change-email` has the same shape — the
  address-already-taken branch generates no token and sends no mail, so it returns faster than the free
  branch. It is behind a login, which makes it the least valuable of the three to an attacker, but it is
  the same fix.

### `/api/auth/*` is not rate limited

- **Now:** none of register, resend-verification, login, logout, forgot-password or reset-password is rate limited. Register, resend, the unconfirmed-account nudge on login and **forgot-password** all trigger outbound email, and login is the credential-stuffing surface. **`/api/auth/forgot-password` (Plan 5) is the cheapest of them to abuse: one anonymous request, no password needed, and the mail goes to a third party whose address is the only thing the caller has to know.**
- **Later:** rate limiting across `/api/auth/*`.
- **Why deferred:** deferred to Plan 8 per the spec's sequencing; the endpoints are unreachable from another machine until deployment.
- **Revisit when:** **This is a launch gate: Plan 9 must not deploy before Plan 8 lands.**
- **Decided:** 2026-08-10 (Slice 2a Plan 3).
- **Extended 2026-08-13 (Plan 6):** `change-email` and `confirm-email-change` join the list. Both send
  mail, but both sit behind a login, so neither is a cheap anonymous vector the way `forgot-password`
  is. The sharper Plan 6 addition is that **change-password's lockout accounting sets the same lock
  login checks** — so five deliberate wrong current passwords from a stolen session lock the real owner
  out of the whole application for fifteen minutes, repeatably. Same answer as the lockout-DoS entry
  below: per-IP rate limiting, not a weaker threshold.

### The registration form gives no Art. 13 notice

- **Now:** Wend collects an email address and a display name from members of the public, with no privacy policy or terms linked from the registration form.
- **Later:** the spec makes the privacy policy and terms a launch deliverable, linked *from the registration form*.
- **Why deferred:** it is lawful today only because registration is unreachable.
- **Revisit when:** **Launch gate for Plan 9: policy and terms exist, and the form links to them, before public sign-up opens.**
- **Decided:** 2026-08-10 (Slice 2a Plan 3).

### Verify and reset tokens travel in a query string

- **Now:** both emailed links — `/verify` (Plan 3) and `/reset-password` (Plan 5) — carry `userId` and `code` as query parameters. Both screens strip them from the address bar with `history.replaceState`, and Kestrel logs nothing at Information.
- **Later:** exclude `/verify` **and `/reset-password`** query strings from access logging.
- **Why deferred:** essentially every reverse proxy logs query strings by default, and there is no proxy until deployment.
- **Revisit when:** **Plan 9 must do this**, per the spec's "path-logging exclusion extends to query strings".
- **Decided:** 2026-08-10 (Slice 2a Plan 3).
- **Extended 2026-08-13 (Plan 6):** `/confirm-email-change` joins the list, and it raises the stakes —
  its query string carries **an email address as well as a token**, so a leak here discloses personal
  data on top of a credential. The exclusion is no longer only about token safety.

### Inactive-account retention has no stance

- **Now:** no retention period is set for inactive (confirmed) accounts. The 7-day purge covers *unverified* accounts only.
- **Later:** a retention period, or a documented decision to keep accounts indefinitely.
- **Why deferred:** the spec allows setting one at plan time *or* explicitly deferring; this is a legal-posture decision that belongs with the privacy policy rather than with registration code.
- **Revisit when:** **Plan 9 decides it.**
- **Decided:** 2026-08-10 (Slice 2a Plan 3).

### A mid-session 401 discards unsaved input

- **Now:** a 401 during an in-flight edit bounces the user to the login screen with the reason
  announced. Whatever they had typed is gone.
- **Later:** the spec asks that unsaved input be preserved "where feasible" — re-authenticate, then
  resume or return the user to what they were doing.
- **Why deferred:** every module would have to learn to stash and restore draft state, which is a
  larger job than the auth gate itself and does not belong in the plan whose job was to make login
  exist. The interruption is always announced meanwhile, never silent.
- **Revisit when:** an editing-heavy slice touches these modules anyway, or daily use shows real
  work being lost.
- **Decided:** 2026-08-10 (Slice 2a Plan 4).

### Lockout is a denial-of-service against any address someone knows

- **Now:** five wrong passwords lock an account for fifteen minutes, new accounts included, and
  nothing is rate limited. Six requests every fifteen minutes keep a named user permanently locked
  out.
- **Later:** per-IP rate limiting on `/api/auth/login`, so the attempts cost the attacker something.
- **Why deferred:** this is the inherent cost of per-account lockout, and the answer is rate
  limiting rather than a weaker threshold — raising the count just makes credential stuffing
  cheaper. Nothing is reachable from another machine until deployment.
- **Revisit when:** **Plan 8 must close this, alongside the other `/api/auth/*` limits. Launch gate.**
- **Decided:** 2026-08-10 (Slice 2a Plan 4).

### A stolen session is a full account takeover, because change-email needs no password

- **Now:** `POST /api/auth/change-email` (Plan 6) requires only a live cookie. `POST
  /api/auth/change-password` requires the current password and counts failures against lockout, so the
  cheaper of the two doors is the one with no lock on it.
- **The full path:** anyone holding a session — an unlocked laptop, a borrowed browser, a session left
  open on a shared machine — requests a change to their own address, clicks the link in their own
  mailbox, and takes both `Email` and `UserName`. `ChangeEmailAsync` rotates the security stamp, so the
  real owner's sessions die on their next request. The owner then has **no self-service route back**:
  login with the old address 401s because `FindByEmailAsync` finds nothing, and forgot-password on the
  old address answers a generic 204 with no mail, by design. The attacker runs forgot-password on the
  new address, sets a password, and owns every board. Wend has no support desk to escalate to, so the
  loss is permanent.
- **The mitigation that does exist:** the notice sent to the old address after a successful change
  (Plan 6). It is detection, not prevention — the person detecting it has no move left.
- **Later:** `/change-email` takes `currentPassword` and verifies it before minting a token, with the
  same locked-out → 401 / `AccessFailedAsync` / reset-count steps `/change-password` uses. Small, and
  it makes the two endpoints symmetric.
- **Why deferred:** raised in Plan 6's stress test and consciously left out of Plan 6's scope
  (Malin, 2026-08-13). The endpoints are unreachable from another machine until deployment, so nothing
  is exposed meanwhile.
- **Revisit when:** **Plan 8 must close this. Launch gate — Plan 9 must not deploy before it.**
- **Decided:** 2026-08-13 (Slice 2a Plan 6 stress test).

### Auth-form text inputs are 32px high, under the 44×44 target minimum

- **Resolved (2026-08-21):** the eight text inputs across the five `js/auth/*/view.js` screens now
  carry `class="input"`, so they take the design system's `min-height: 2.75rem`. Measured against
  the running app at 375×812 and 1280×900: every auth input is **51.59px** tall with a computed
  `min-height: 44px`, where the same fields measured **31.59px** before.
- **The trap it hid:** the fix was never a `min-height` override. The inputs carried **no class at
  all**, so no design-system input styling ever applied to them and DS 2.0.2's 44px control floor
  went straight past them. A raised floor in `.auth-form` would have worked too and left the real
  cause in place.
- **It traded one gap for another** — see the boundary-contrast entry below.
- **Originally decided:** 2026-08-11 (Slice 2a Plan 4), measured in Plan 4's Task 7 walk.

### A styled auth input has a 1.34:1 boundary against the page

- **Now:** with `class="input"` the field is a `--surface-2` fill inside a 1px `--border` edge on a
  `--page-bg` page — **1.09:1** fill-vs-page and **1.34:1** border-vs-page, measured on the running
  app. WCAG 2.2 SC 1.4.11 (AA) asks for 3:1 on the visual information needed to identify a control.
  The browser default these fields had before was a 2px `#858585` inset border at **5.13:1**, which
  the SC exempts precisely because it is user-agent styling the author has not touched.
- **No existing border token clears the bar:** `--border` 1.34:1, `--border-strong` 1.69:1,
  `--line` is a hairline overlay. `--text-faint` reaches 5.91:1 but is a text token.
- **Decided 2026-08-21 (Malin): fix it upstream in `workbench`'s design system** — a control-boundary
  token at ≥3:1 — the way the 42px floor was fixed and synced back as DS 2.0.2. **No local
  `app.css` override**: the point of a shared design system is that consumers stay in sync, and an
  override here would fix Wend's auth screens while leaving every other consumer wrong and Wend
  itself carrying a second source of truth for the same number. `design-system/` stays read-only.
- **What still identifies the field meanwhile:** its visible label, the hint text below it, 16.09:1
  text contrast inside the field, and a 2px `--accent-strong` focus ring at 9.15:1.
- **Revisit when:** the workbench token lands and syncs into Wend. It is a launch-relevant AA gap on the only
  screens the public reaches, so it wants an answer before Plan 9.
- **Raised:** 2026-08-21, while adding `class="input"`.
