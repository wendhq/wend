# Wend — Henry's implementation guide

- **Date:** 2026-08-13
- **For:** Henry, working solo with his own Claude
- **Covers:** three small PRs, then Slice 2a Plan 6
- **Written by:** Malin & Claude

Welcome back to the codebase. This guide gets you from a clean clone to a merged PR three times over,
on deliberately small changes, before you take on a whole plan. Each of the first three is real work
that needed doing — none of them is a toy exercise — but none can break authentication either.

Read this file top to bottom once before starting. The **Ground rules** section is the part that will
save you the most time; every item in it cost somebody a debugging session already.

---

## Ground rules

**Before any Wend work, in this order:**

```powershell
Start-Service postgresql-x64-17
```

The service is set to Manual start, so if you skip this you get connection-refused errors and EF
timeouts that read like a code bug. They aren't.

```powershell
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force
```

The process is called **`Wend.Api`**, not `Wend`. A leftover one holds a lock on the DLL and the build
fails with MSB3021/MSB3027 — a copy-lock, not a test failure.

**Running the app** needs an environment variable set first, or it dies claiming
`ConnectionStrings:WendDb is not configured`, which reads as a missing secret rather than a missing
env var:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"; dotnet run --project Wend.Api
```

(PR 1 below is the fix for exactly this annoyance.)

**Running the tests** needs no env var — the test factory pins `Development` itself:

```bash
dotnet test
```

**Baseline: 253 tests, all green.** Check the count every time you run them. A paste-driven edit once
silently dropped three tests behind a green suite, and nobody noticed until the totals were compared
against the plan.

**Every browser check must hard-reload.** `UseStaticFiles` sends no `Cache-Control`, so the browser
will happily serve you JS and CSS from an earlier `127.0.0.1:5174` session — and a normal reload does
not revalidate ES-module imports. Two "bugs" in a previous accessibility sweep were cache ghosts. (PR
1 also fixes this.)

**Do not edit anything under `Wend.Api/wwwroot/design-system/`.** It is a vendored copy of a shared
library, refreshed by `sync-design-system.ps1`. Changes there get silently overwritten on the next
sync. If something in it is wrong, say so and it gets fixed upstream — that is exactly what happened
on 2026-08-13, which is why the bundle is at 2.0.2.

**Git conventions:**

- Branch from `main`, push, open a PR. `main` requires a PR and a green `Build & test` check; nothing
  lands without review.
- **No AI attribution in commits or PR bodies.** No `Co-Authored-By`, no "Generated with" trailers.
  You are the sole listed author of your commits.
- Commit messages: subject line in the imperative, then a body explaining *why*, not *what*. The diff
  already says what. Look at `git log` for the house voice.
- **Your PR body is where deviations live.** If you had to depart from this guide, say so there with
  the reason. Later plans read PR bodies rather than re-deriving decisions, so a PR body that hides a
  deviation costs somebody real time months later.
- `.editorconfig` trims trailing whitespace and enforces a final newline (`.md` files exempt).

**If a required check never runs, suspect the webhook, not your branch.** A GitHub Actions outage once
throttled webhooks so pushes created no runs, leaving a PR permanently unmergeable behind a strict
required check. Close/reopen does not help. Push an empty commit, or trigger `ci.yml` manually via
`workflow_dispatch`.

---

## PR 1 — Local dev: `launchSettings.json` + a dev-only no-cache header

**Why:** the two annoyances in the ground rules above. Both are recorded in
[`backlog.md`](backlog.md) with their reasoning; this PR closes them.

**Scope:** two changes, no product behaviour, no tests to break.

### Task 1.1 — `Wend.Api/Properties/launchSettings.json`

The file does not exist yet. Create it so `dotnet run --project Wend.Api` works without setting an
environment variable by hand.

- The app listens on `127.0.0.1:5174`, configured in code via `builder.WebHost.ConfigureKestrel` with
  a `Wend:Port` seam ([`Program.cs`](../Wend.Api/Program.cs)). **Do not add an `applicationUrl` that
  fights that.** Your profile's job is the environment variable, not the address.
- Set `ASPNETCORE_ENVIRONMENT` to `Development`.
- Do not set `launchBrowser` to true — the app is an API plus a static frontend and a browser opening
  on every `dotnet run` gets old fast.

**Verify:** in a *fresh* shell with no `ASPNETCORE_ENVIRONMENT` set, `dotnet run --project Wend.Api`
starts and serves `http://127.0.0.1:5174/login` with a 200. Then confirm `dotnet test` is still 253
green — `launchSettings.json` should not affect it at all, and if it does, something is wrong.

### Task 1.2 — a dev-only no-cache header on static files

Currently `app.UseStaticFiles()` sends no `Cache-Control` at all, so browsers cache aggressively and
stale JS/CSS survives across sessions.

- Add `Cache-Control: no-cache` to static-file responses **in Development only**. `UseStaticFiles`
  takes a `StaticFileOptions` with an `OnPrepareResponse` callback; that is the hook.
- Guard it on `app.Environment.IsDevelopment()`. In production these files *should* be cacheable —
  making them uncacheable everywhere would be a performance regression dressed as a fix.
- `no-cache` is the right value, not `no-store`: it means "revalidate before using", which is what
  fixes the stale-module problem while still allowing a 304.
- Leave `UseDefaultFiles()` above it alone.

**Verify:** with the app running, request a CSS file twice and confirm the response carries
`Cache-Control: no-cache`. `curl -I http://127.0.0.1:5174/css/app.css` is enough. Then check that a
`dotnet test` run is still 253 green.

**PR body should note:** both backlog items closed, and that the header is Development-only and why.

---

## PR 2 — Auth form inputs: adopt the design-system input, then measure

**Why:** every text input on the register, login, forgot-password and reset-password screens measures
**31.6px** high against the project's 44×44 touch-target minimum. It is in
[`backlog.md`](backlog.md), deferred at Plan 4 with the trigger *"revisit when the next slice touches
auth styling"* — and Plan 6 adds two more forms of exactly this shape, so this is the moment.

**The cause is not what the backlog says.** It says to raise the inputs to the `.btn` floor. In fact
the auth inputs carry **no `class` attribute at all** — look at
[`js/auth/login/view.js`](../Wend.Api/wwwroot/js/auth/login/view.js), line 31 — so they get no
design-system input styling whatsoever and compute `min-height: auto`, i.e. the browser default.

### Task 2.1 — add `class="input"` to every auth text input

The design system already has the right component. `.input` carries `min-height: 2.75rem` (44px) as of
bundle 2.0.2, plus the padding, border, background and focus treatment every other Wend control uses.

**Seven inputs across four files** — one MVC trio per screen, and the inputs live in the **view**:

| File | Inputs |
|---|---|
| `js/auth/register/view.js` | `#reg-name`, `#reg-email`, `#reg-password` |
| `js/auth/login/view.js` | `#login-email`, `#login-password` |
| `js/auth/forgot/view.js` | `#forgot-email` |
| `js/auth/reset/view.js` | `#reset-password` |

Add `class="input"` to each of the seven. If you end up with a different count, you have either missed
one or styled something that is not a text input. **Do not** add a `min-height` rule to
`css/app.css` — using the component is the fix; a local override would be a second source of truth
for the same number.

Leave every other attribute alone: `id`, `name`, `type`, `autocomplete`, `minlength`, `maxlength`,
`aria-describedby` and `required` are all load-bearing and several are tested.

### Task 2.2 — the measurement pass

This is the part that matters, and it is owed twice over: the design-system identity changed under
Wend on 2026-08-12 and again on 2026-08-13, and **nobody has yet measured a rendered pixel in the new
identity.** Your PR is where that happens.

Measure, on both the board screens and every auth screen:

- Every interactive control clears **44×44**. Buttons, inputs, checkboxes, links styled as controls,
  the header chrome, the mobile list switcher.
- The focus ring is visible on every control, and tabbing through each screen never lands on `<body>`.
- Nothing overlaps or clips at 375px wide, and the new input height has not broken any layout.

**How to measure honestly — three traps, all documented:**

1. **Hard-reload before every measurement.** See the ground rules.
2. **Use `getBoundingClientRect()` / computed styles, not eyeballing.** A 42px control looks
   identical to a 44px one.
3. **If your tooling reports `innerWidth: 0`, your numbers are garbage.** A browser pane that is not
   being displayed stops compositing, and every rect comes back zero or nonsense while scripts and
   the accessibility tree keep working normally. This bit us on 2026-08-13 — a measurement claimed the
   "Sign in" button was 34px wide. Check `innerWidth` first, every session.

Also worth knowing: **synthetic clicks from browser automation do not dispatch** in this setup. They
report plausible coordinates and nothing happens — no submit, no error. Drive forms with
`form.requestSubmit()` or `el.click()` and real key events. And `minlength` validation only fires on a
**user-edited** value, so setting `.value` from a script leaves the field non-dirty and native
validation wrongly passes.

**Verify:** `dotnet test` still 253 green (this is a frontend-only change, so the count must not move),
plus your measurement notes.

**PR body should note:** that the real cause was a missing class rather than a missing floor, the
measured before/after height, and — explicitly — **whether a human pointer and keyboard actually
touched the screens, or whether it was measured programmatically only.** That distinction has been
recorded honestly in every previous Wend PR and it should stay that way.

---

## PR 3 — Remember-me on login

**Why:** two code comments have promised this since Plan 4 —
[`Program.cs`](../Wend.Api/Program.cs) says *"Plan 6 adds remember-me as a deliberate opt-in"*, and
[`AuthEndpoints.cs`](../Wend.Api/AuthEndpoints.cs) hard-codes `isPersistent: false` with a comment
pointing at Plan 6. It was pulled out of Plan 6 into its own PR because it shares no code with the
rest of that plan, and this is a complete vertical slice at a tenth of the size: endpoint, request
shape, frontend, tests.

### Task 3.1 — the endpoint

- `LoginRequest` gains a `RememberMe` boolean. An omitted value deserializes to `false`, which is both
  the safe default and backwards compatible with the current client — so no existing test should break
  on the shape change. If one does, read it before changing it.
- `PasswordSignInAsync(user, password, isPersistent: req.RememberMe, lockoutOnFailure: true)`.
- Update the comment above it. It currently explains why the flag is false; it should now explain what
  it carries.
- **No cookie configuration changes.** `ExpireTimeSpan = 7 days` and `SlidingExpiration = true` are
  already set. `isPersistent` decides only whether the cookie gets an `Expires` attribute, and
  therefore whether it survives a browser restart. Do not lengthen the window — that is a separate
  decision and it has no reason to ride along here.

### Task 3.2 — the login screen

- A checkbox, **unchecked by default**, in `js/auth/login/view.js`, with a real `<label>` and a short
  hint saying what it does. A persistent cookie on a shared computer is a real risk and the label
  should be honest rather than coy.
- The controller sends its value in the login request body.
- **The checkbox needs its own touch-target check.** PR 2 fixes text inputs; a native checkbox is
  smaller than 44×44 on its own and is not covered by `.input`.

### Task 3.3 — tests

Add to `Wend.Tests`. The important one:

- **`Set-Cookie` carries an `expires` attribute when `RememberMe` is true, and does not when it is
  false.** This test **cannot** run through the `Test` auth scheme — that scheme issues no cookie at
  all, so a test-scheme version would pass while testing nothing. Use `WendApiFactory` with
  `useTestAuth: false`; `RealCookieAuthTests.cs` is your model.
- A login with `RememberMe` omitted still succeeds and behaves as non-persistent.

**Verify:** `dotnet test` green with the count risen by however many tests you added — state the new
total in the PR body.

---

## Then: Slice 2a Plan 6 — account settings

Design doc:
[`2026-08-13-wend-slice2a-plan6-account-settings-design.md`](2026-08-13-wend-slice2a-plan6-account-settings-design.md).
Read it end to end before writing anything. It is signed off and stress-tested, and it contains the
findings that make the difference between a working implementation and a subtly broken one.

**The two things to internalise before you start:**

1. **`ChangeEmailAsync` does not touch `UserName`.** Verified against the `release/10.0` source. Wend
   sets `UserName = Email` at registration, so a change-email that calls only `ChangeEmailAsync`
   leaves the old address occupied forever — and the next person to register it gets a silent `204`
   with no mail. `SetUserNameAsync` alongside it is the plan's correctness requirement, and the design
   doc specifies the one regression test that catches its absence.
2. **`/change-email` answers a generic `204` for an address another account holds.** The endpoint is
   authenticated and feels private, which is exactly why a `409` looks reasonable. It would turn
   account settings into a user-table enumeration oracle.

**A step-by-step implementation plan for Plan 6 will be written after PR 3 merges**, not now —
deliberately. The design doc's *Open items* list several framework behaviours to verify against the
.NET 10 source at plan time rather than assume, and the plan should be written against the tree as it
actually is once your three PRs have landed. Ask for it when you get there.

**One thing that is not in the design doc's scope but you should know about:** the stress test found
that `/change-email` requires no password, which makes a stolen session a permanent account takeover
with no self-service recovery. Malin decided to leave the fix out of Plan 6; it is recorded in
[`backlog.md`](backlog.md) as a Plan 8 launch gate with the full attack path written out. Do not
"helpfully" fix it inside Plan 6 — it is a deliberate deferral, and quietly closing it would make your
PR harder to review, not better.

---

## When you are stuck

Two files carry almost everything that has ever cost someone a debugging session on this project, and
between them they will answer most questions faster than reading source:

- **`docs/backlog.md`** — every consciously-deferred decision, with the reason and the trigger for
  revisiting. Read it before proposing anything; there is a good chance a decision you are about to
  make has already been made and written down. It also records *resolved* entries, which is where a
  fixed trap's reasoning survives.
- **The PR bodies of #41, #43, #44 and #47** — Plans 3, 4, 5 and the design-system sync. Each one
  records its own deviations. That is the house habit, and it is why the project's history is
  readable.

If something in this guide turns out to be wrong, say so in your PR rather than working around it
silently. The guide being wrong is more useful to know than a PR that quietly compensated for it.
