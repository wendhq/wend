# Wend — Slice 2a Plan 4 design: Login, session & the auth gate

- **Date:** 2026-08-10
- **Status:** Draft — brainstormed with Malin 2026-08-10; **stress-tested 2026-08-10 across
  security / privacy / accessibility / loopholes, 12 findings folded in**; pending review and
  sign-off by both owners
- **Owners:** Malin & Henry (equal ownership)
- **Repo:** `github.com/wendhq/wend`
- **Parent spec:** [`2026-07-08-wend-slice2a-accounts-design.md`](2026-07-08-wend-slice2a-accounts-design.md) (signed off, stress-tested)
- **Follows:** Plan 3 — register & verify ([PR #41](https://github.com/wendhq/wend/pull/41), merged; suite at **205 green**)

---

## Context — what this plan inherits

Plan 2 put the ownership boundary in place behind an `ICurrentUser` seam that always returns `null`.
Plan 3 built registration and email confirmation on top of headless Identity — `UserManager` and the
token providers, deliberately no cookie scheme and no `SignInManager`.

The consequence has been carried openly since Plan 2: **`/api/boards` answers 401 to every request,
and Wend cannot show you a board in a browser.** A stranger can create an account and confirm their
address, and then there is nowhere to go. This is the plan that closes that gap.

Plan 3 explicitly deferred three obligations here, and they shape the design:

1. **A real `ICurrentUser`.** No authentication scheme exists yet — no `AddAuthentication`, no
   cookies, no `SignInManager`.
2. **Lockout thresholds.** `SignInManager` is their only reader, so setting them earlier would have
   been configuration nothing consulted.
3. **Constant-time login.** The parent spec requires timing equalisation for login specifically;
   register's timing side channel is Plan 8's to close and is already backlogged.

---

## Goals & non-goals

**Goals**

- Cookie authentication, `SignInManager`, and lockout — the machinery that turns a confirmed account
  into a session.
- `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`.
- The real `ICurrentUser`, reading the signed-in principal off the request.
- `RequireAuthorization()` in front of the board, list, card, label and checklist endpoint groups.
- The frontend **auth gate**: the app decides on boot between the board overview and a login screen,
  and a mid-session 401 bounces the user back to login with the reason announced.
- A `/login` route, a logout control, and the link out of Plan 3's verify success state — the three
  pieces that turn register → verify → sign in into a walkable flow rather than three screens.
- Sessions that die on a security event, so Plans 5 and 7 can make their promises truthfully.

**Non-goals** (each named with the plan that owns it)

- **Remember-me / persistent cookies** — Plan 6, with change email and change password. Every login
  in this plan is non-persistent.
- **Forgot / reset password** — Plan 5.
- **Account deletion** — Plan 7.
- **Antiforgery, rate limiting, HTTPS/HSTS, security headers, secrets posture** — Plan 8. Login,
  logout and the nudge below are all unrate-limited when this plan lands, exactly as register and
  resend already are. **Plan 8 is a launch gate; Plan 9 must not deploy before it.**
- **Preserving unsaved input across a mid-session 401** — see *Frontend*, deferred to the backlog
  with the reasoning.

---

## Decisions locked (brainstorm, 2026-08-10)

| Decision | Value | Why |
|---|---|---|
| **Enforcement** | `RequireAuthorization()` on the five board-family groups, **and** the 28 existing per-handler `ICurrentUser` guards stay | The parent spec asks for the attribute; the guards are defence in depth and are compile-enforced (`ownerId` only exists via the pattern match). An endpoint added later that forgets the guard still answers 401. |
| **Test seam** | A `Test` authentication scheme inside `WendApiFactory`, replacing the `ICurrentUser` override | Keeps all 205 existing test bodies unchanged *and* runs them through the real `HttpContextCurrentUser` and a real `ClaimsPrincipal`. More of the shipping path under test than today, not less. |
| **Real-cookie coverage** | A factory flag turns the test scheme off, so login tests drive the genuine cookie path end to end | A test scheme that is never bypassed would mean the cookie middleware ships untested. |
| **Lockout** | **5** failed attempts, **15**-minute lockout, `AllowedForNewUsers = true` | Small enough to blunt credential stuffing against one account, long enough to be costly, short enough that a real user who mistyped is not locked out for the afternoon. `AllowedForNewUsers` matters because otherwise a freshly registered account — the ones an attacker targets first — is exempt. |
| **Security-stamp validation** | `ValidationInterval = TimeSpan.Zero` — revalidate on every authenticated request | One extra read per request, and it is what makes "a password reset evicts the attacker" (Plan 5) and "a deleted user's cookie is refused on its next request" (Plan 7) true on the *next* request rather than up to N minutes later. The parent spec commits to those properties; a non-zero interval would make them approximations. |
| **Login failure response** | One generic `401` for unknown email, wrong password, unconfirmed account **and** locked-out account | The parent spec's enumeration requirement. "Your account is locked" confirms the account exists, which is the same leak by a different name. |
| **Constant time** | An unknown address verifies the supplied password against a dummy hash before answering, and no branch does extra work on the response path — including the nudge | Without it, "no such user" returns in the time of a database read while a real user costs a full password hash — a timing oracle that enumerates the user table regardless of how generic the response body is. |
| **Unconfirmed-account nudge** | Sent **only when the supplied password is correct** | `PasswordSignInAsync` returns `NotAllowed` for an unconfirmed account *without checking the password at all*. Nudging on that result alone would let anyone who knows an address bomb its inbox with login attempts. Checking the password first costs one extra verify on that branch and means only the real owner can trigger mail. The response is byte-identical either way. |
| **Cookie `SecurePolicy`** | `Always` outside Development, `SameAsRequest` in it | Dev runs plain HTTP on `127.0.0.1:5174`. `Always` there means the browser silently drops the cookie: login answers 204, the next request is anonymous, and it looks like a session bug rather than a config one. |
| **Cookie name** | `wend.session` | The default `.AspNetCore.Identity.Application` announces the stack to anyone reading response headers. Free to change, so change it. |
| **Cookie lifetime** | `ExpireTimeSpan` 7 days, `SlidingExpiration = true`, non-persistent | Non-persistent means the cookie dies with the browser session — the correct default until Plan 6 adds remember-me as a deliberate opt-in. |
| **Logout requires authentication** | `POST /api/auth/logout` carries `RequireAuthorization()` | An anonymous logout endpoint is a free CSRF target that costs an attacker nothing and a victim their session. Antiforgery on top of this is Plan 8's. |
| **Antiforgery stays Plan 8's — and login-CSRF is not live meanwhile** | Stated here rather than assumed | The parent spec calls login-CSRF a real vector, and this plan ships login and logout with no antiforgery, so the reason it is safe has to be written down. `/api/auth/*` binds **JSON only**: an HTML form cannot send `application/json`, and a cross-site `fetch` that does triggers a preflight there is no CORS policy to satisfy. That protection is invisible in the code and would evaporate silently the day someone adds form binding or opens CORS — so a test asserts a form-encoded POST to `/login` is refused. |
| **Logging on the login path** | A lockout is logged **by user id**; a failed login is not logged at all; passwords, tokens and email addresses never | Failed logins are the highest-value PII target in the slice, and the instinctive implementation logs the address. Plan 3 set the precedent — error codes only, never the address. Failed-login logging earns its place in Plan 8, where rate limiting gives it a context; until then it is only a log file full of other people's email addresses. |

---

## Authentication wiring

`AddIdentityCore<WendUser>()` gains `.AddSignInManager()`. Alongside it:

```csharp
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
```

and `app.UseAuthentication()` / `app.UseAuthorization()` before the endpoint mappings.

Cookie configuration: `HttpOnly`, `SameSite=Lax`, `Cookie.Name = "wend.session"`, `ExpireTimeSpan`
7 days with sliding expiration, and the environment-dependent `SecurePolicy` above.

**No login-redirect events are configured.** In .NET 10 the cookie handler already answers 401/403
for JSON minimal-API endpoints rather than redirecting to a server-rendered login page — the
`OnRedirectToLogin` workaround that older guides carry is gone. Wend has no server-rendered login
page to redirect to; the client owns routing. The existing anonymous-request tests assert the 401
and are the guard on this.

Identity options gained here:

```csharp
options.SignIn.RequireConfirmedAccount = true;
options.Lockout.MaxFailedAccessAttempts = 5;
options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
options.Lockout.AllowedForNewUsers = true;
```

plus `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`.

---

## `ICurrentUser`, for real

`NullCurrentUser` is replaced by `HttpContextCurrentUser`, which reads the `NameIdentifier` claim
from `IHttpContextAccessor.HttpContext?.User` and returns `null` unless the principal is
authenticated. `AddHttpContextAccessor()` is registered for it.

Nothing else changes. The interface, the 28 handler guards, and all 34 repository signatures are
untouched — which is what the Plan 2 seam was for.

`RequireAuthorization()` goes on the five board-family groups. `/api/auth/*` stays anonymous apart
from `/logout` and `/me`, which are authorized individually.

---

## `POST /api/auth/login`

Request `{ email, password }`. Success is `204` with the cookie set. Every failure is the same
`401` with the same body.

The handler in order:

1. Find the user by email. **If there is no such user**, verify the supplied password against a
   dummy hash, then return 401 — so the no-such-user path does the same hashing work as a real
   verification. Whether that hash is a constant or generated once at startup is an open item below.
2. `PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true)`.
3. `Succeeded` → 204.
4. `NotAllowed` (the unconfirmed case) → check the password with `CheckPasswordAsync`; if and only
   if it is correct, send a fresh confirmation link through the existing `IAuthEmailSender`. Return
   401 either way.
5. `LockedOut`, failure, anything else → 401.

**The nudge must not be awaited on the response path.** Steps 1–4 equalise the hashing work, and
then step 4 would undo it: with Plan 9's transactional provider, awaiting the send makes the
"correct password on an unconfirmed account" branch measurably slower than every other outcome —
which says *this address exists, is unconfirmed, and you guessed its password*. Narrow, but it is
the exact property this section claims to have closed. The send is therefore dispatched without
blocking the response, and the handler's own timing stays identical across all five outcomes.
Register's equivalent side channel is already Plan 8's, backlogged at Plan 3; this one is closed
here because login is where the parent spec asks for constant time by name.

The nudge reuses `IAuthEmailSender.SendEmailConfirmationAsync` and the link-building path Plan 3
already wrote — including its `Wend:PublicBaseUrl` origin, so a Host-header injection cannot forge
the link. No interface change.

---

## `GET /api/auth/me` and `POST /api/auth/logout`

`GET /api/auth/me` → `200 { displayName, email }` for a signed-in user, `401` otherwise. This is
what the frontend gate calls on boot, and the only endpoint whose 401 is an ordinary expected
answer rather than a problem.

`POST /api/auth/logout` → `204`, authorized, `SignOutAsync` clearing the cookie.

---

## Test seam

`WendApiFactory` currently overrides `ICurrentUser` with a mutable `TestCurrentUser`. That override
stops working the moment `RequireAuthorization()` exists, because authorization runs before the
handler that would have consulted it.

**The override is replaced by a test authentication scheme.** The factory registers a
`TestAuthHandler` that issues a ticket carrying `NameIdentifier = CurrentUser.UserId`, or returns
`NoResult()` when that is null, and makes it the default scheme. `TestCurrentUser` survives as the
"who am I acting as" dial the tests already use; every test body is unchanged.

The gain is not only compatibility: those 205 tests now run through the real
`HttpContextCurrentUser` reading a real `ClaimsPrincipal`, where today they inject past it.

`TestAuthHandler` lives in **`Wend.Tests`, never in `Wend.Api`**. A scheme that authenticates
whoever asks is an auth bypass the moment it is reachable from the shipping project, and the only
reliable way to keep it unreachable is for it not to be there.

**The real cookie path still has to be tested**, so the factory takes a constructor flag
(`useTestAuth`, default `true`). With it off, only the genuine Identity cookie scheme is present and
a test can walk register → verify → login → `/api/boards` → logout the way a browser does.

**That default is a trap, and it gets a canary.** `ConfigureClient` sets
`CurrentUser.UserId = DefaultUserId` on every `CreateClient()`, so a login test written against a
factory that forgot the flag is authenticated before it does anything — and passes while testing
nothing. This repo has been bitten by that shape twice already (the `CreateClient()` note in
`WendApiFactory`, and the three tests that once vanished behind a green suite). The real-cookie
suite therefore **opens by asserting `GET /api/auth/me` is 401 before anybody logs in**. With the
test scheme accidentally on, that returns 200 and the suite fails loudly on its first line.

---

## Frontend

**The gate.** `main.js` boots by calling `GET /api/auth/me`. A 200 mounts the board overview; a 401
mounts the login screen. `/register` and `/verify` still win by pathname, because an emailed link
has to land somewhere regardless of session state.

**Login gets a route of its own.** The gate mounts it as a *state*, which is enough for the bounce
and nothing else: a screen with no URL cannot be linked to, and this plan's own cross-links need an
href. `main.js` gains `case "/login"`, mounting the same screen directly. That is what makes the
register screen's "back to sign in" link, and the verify screen's handoff below, into real links
rather than described intentions.

**Mid-session 401.** The existing `reportLoadFailure` currently announces "You're not signed in".
It becomes a bounce: mount the login screen, announce "Your session expired — please sign in again",
and move focus to the login heading. Focus never drops to `<body>`.

**The login screen** is a `js/auth/login/{model,view,controller}.js` trio, matching the register and
verify screens Plan 3 wrote:

- `autocomplete="email"` and `autocomplete="current-password"`.
- `.btn` plus a variant on every control — a bare `<button>` is 28px and fails the 44×44 minimum.
  This was caught twice during Plan 3.
- Submit disabled while a request is in flight, so a double-click cannot burn two lockout attempts.
- An announced error summary, `aria-describedby` on the fields. **Focus placement depends on where
  the error came from:** a client-side validation error focuses the first offending field; a
  server-side 401 belongs to no field, so focus goes to the announced summary. Sending focus to the
  email input on a generic failure lands a screen-reader user mid-form having heard nothing useful.
- `escapeHtml` at every interpolation.
- A link to `/register`, and a matching link back from the register screen.

**After three consecutive failures the screen reveals static help.** The generic 401 is right for
security and brutal for the person on the other end: a locked-out user retries for fifteen minutes
with no signal, and an unverified user may never connect the nudge email to the failure. So the
controller counts consecutive failures **client-side** and, at three, reveals a help block — wait a
few minutes and try again, check your inbox for a verification link, or reset your password (Plan 5)
— and announces its appearance. The count is per screen instance, the text is identical for every
account, and the server sends no signal that drives it, so it leaks nothing an attacker did not
already know from counting their own attempts.

**Logout** joins Settings in the header, hidden by `hideAppChrome()` on auth screens for the same
reason Settings is: on an auth screen it is a trap sitting first in the tab order. Signing out
mounts the login screen, **moves focus to its heading, and announces "You're signed out."** — a
deliberate sign-out destroys the focused control exactly as the 401 bounce does, and without this
focus drops to `<body>`.

**`hideAppChrome()` needs an inverse.** It is a one-way switch today: register on `/register`, click
through to login, sign in, and the gate mounts the app with Settings — and now Logout — still
hidden until a full reload. A `showAppChrome()` covering both controls is called whenever the gate
mounts the app.

**The verify screen's success state is finished here.** Plan 3 shipped `/verify` when there was
nowhere to send a newly-confirmed user, so its success state dead-ends. It gains a link to `/login`
and the matching announcement. It is a small change and it is the handoff the whole slice has been
building toward — register → verify → **sign in** — so it belongs in the plan that makes the last
step exist.

**Deferred, deliberately.** The parent spec asks that a 401 during an in-flight edit *preserve the
user's unsaved input where feasible*. Doing that properly means every module learns to stash and
restore draft state — a larger job than the gate itself, and one that would swell this plan past the
thing it exists to deliver. Plan 4 ships the announce-and-bounce; input preservation goes to
`docs/backlog.md` as its own item, with this reasoning. **Deferred out loud, not skipped.**

---

## Testing

The two-tier engine is unchanged: repository unit tests on in-memory SQLite, API integration tests
against a per-test throwaway PostgreSQL database.

**New coverage**

- **Login responses are indistinguishable** — unknown email, wrong password, unconfirmed account and
  locked-out account all return the same status and body.
- **Lockout** — five failures lock the account; the sixth attempt with the *correct* password still
  fails; `AllowedForNewUsers` means a brand-new account locks too.
- **The nudge** — a correct password on an unconfirmed account sends exactly one confirmation email;
  a wrong password on the same account sends none.
- **Session** — `/me` answers 401 anonymous and 200 with the display name signed in; logout clears
  the cookie and the next `/api/boards` call is 401.
- **Login binds JSON only** — a form-encoded POST to `/api/auth/login` is refused. This is the guard
  on the reasoning that lets antiforgery wait for Plan 8; if someone later adds form binding, this
  test is what tells them they have opened login-CSRF.
- **The real cookie walk** — register → verify → login → `/api/boards` 200 → logout → 401, on a
  factory with the test scheme disabled, **opening with the `GET /api/auth/me` → 401 canary** that
  proves the test scheme really is off before anything else is asserted.
- **`RequireAuthorization()` is actually in front** — an anonymous request to each of the five
  groups is 401 before any handler runs.
- **Existing coverage stays green at 205** under the new test scheme, which is the check that the
  seam swap did not quietly widen or narrow the boundary.

Frontend tasks have no automated tests — this repo has no JS test harness, as through all of Slice 1
— so they carry scripted manual checks, exactly as Plan 3's screens did. Every browser check
hard-reloads, because `UseStaticFiles` sends no `Cache-Control`.

---

## Task breakdown (detail is the writing-plans step)

1. **Cookie auth wiring** — `AddSignInManager`, `AddIdentityCookies`, cookie and lockout options,
   `HttpContextCurrentUser`, `RequireAuthorization()` on the five groups, and the test scheme that
   keeps the 205 green.
2. **`POST /api/auth/login`** — generic response, lockout, the dummy-hash constant-time path.
3. **The unconfirmed-account nudge** — correct-password-only, dispatched off the response path,
   reusing the Plan 3 sender.
4. **`GET /api/auth/me` and `POST /api/auth/logout`**, plus the form-encoded-POST rejection test that
   guards the antiforgery deferral.
5. **The real-cookie end-to-end suite** — the factory flag, the `/me` canary, the browser-shaped walk.
6. **The login screen** — MVC trio, mobile-first CSS, the two focus rules, the three-failure help
   block, the accessibility checks.
7. **The auth gate** — `main.js` boot check and `/login` route, the mid-session bounce, the logout
   control with its focus and announcement, `showAppChrome()`, and the verify screen's link to login.
8. **Backlog and docs** — input preservation, and the Plan 8 launch gate: rate limiting for login,
   logout and the nudge, plus the lockout denial-of-service below.

Roughly 30 new tests on the 205 baseline; exact per-task totals are pinned when the plan is written,
because a paste-driven edit once silently dropped three tests behind a green suite.

---

## Risks

- **The seam swap is the whole suite at once.** Every one of the 205 tests changes how it
  authenticates, in a single task. Mitigated by doing nothing else in that task and by asserting the
  count before committing — but if it goes wrong it goes wrong everywhere, which is also why it goes
  first rather than last.
- **`TimeSpan.Zero` stamp validation adds a database read per authenticated request.** Trivial at
  Wend's scale and bought deliberately; worth revisiting only if Plan 9's hosting makes it visible.
- **Cookie `SecurePolicy` in dev.** Getting it wrong produces a login that succeeds and a session
  that does not exist — a failure that reads as a bug in the gate. Called out here so review looks
  for it.
- **The nudge is an unrate-limited outbound-mail path** reachable by anyone who knows an address
  *and its password*. That last condition makes it much narrower than register or resend, but it is
  still Plan 8's to close, and Plan 8 remains a launch gate.
- **Lockout is a denial-of-service against any address someone knows.** Six requests every fifteen
  minutes keep a named user permanently locked out, and nothing is rate-limited until Plan 8. This
  is the cost of per-account lockout and it is accepted deliberately: the answer is per-IP rate
  limiting, not a weaker threshold, because raising the count is just cheaper credential stuffing.
  Backlogged with the other Plan 8 gates so it cannot be discovered at deploy time.

---

## Key decisions (and why)

- **`RequireAuthorization()` *and* the handler guards** — the attribute is the boundary a future
  endpoint inherits for free; the guard is the one the compiler enforces. Keeping both costs a line
  per handler that is already written.
- **A test auth scheme rather than real logins in every test** — converting 205 tests to register,
  confirm and log in over HTTP would put a password hash in every setup, slow the suite, and mix
  authentication failures into tests about ownership. The scheme keeps those tests about what they
  are about, and a separate, small suite covers the cookie path they no longer touch.
- **Dropping the `ICurrentUser` override rather than keeping it alongside** — two seams doing the
  same job is how one of them quietly stops matching production.
- **Generic 401 for lockout too** — a distinct lockout response is the enumeration leak the rest of
  the slice is careful to avoid, wearing a helpful face.
- **Nudge only on a correct password** — the version that helps the real user without handing an
  attacker a one-request email bomb. `NotAllowed` arriving before any password check is the detail
  that makes the obvious implementation the wrong one.
- **`TimeSpan.Zero` stamp validation** — the parent spec promises that reset and deletion evict live
  sessions. A cache interval turns that promise into "within a few minutes", which is not what it
  says.
- **Input preservation deferred** — a cross-cutting change to every module does not belong in the
  plan whose job is to make login exist.

---

## Open items — to confirm at plan time

- Whether cookie options are configured via `ConfigureApplicationCookie` or the `AddIdentityCookies`
  builder overload under .NET 10 with `AddIdentityCore` — verified against current docs when the
  plan is written, not assumed from older `AddIdentity` guidance.
- The exact `SignInResult` surface `PasswordSignInAsync` returns for a locked-out *and* unconfirmed
  account, so the nudge branch cannot fire for someone who is locked out.
- Whether the dummy hash is best precomputed as a constant or generated once at startup from
  `IPasswordHasher`, given Identity's hash format may carry per-instance parameters.
- Whether `TestAuthHandler` needs to satisfy the security-stamp validator, or whether the validator
  is bound to the Identity cookie scheme only.
- What `ValidationInterval = TimeSpan.Zero` does to the response. The stamp validator refreshes the
  sign-in when it revalidates, so a zero interval plausibly means a `Set-Cookie` header on **every**
  authenticated response, and its interaction with 7-day sliding expiration is worth knowing before
  it is a surprise. Confirmed against .NET 10 behaviour at plan time, not assumed.
- How the nudge is dispatched off the response path without a fire-and-forget `Task` that a shutdown
  can drop — the shape is decided at plan time against what the codebase already has.
- Where the logout control sits in the header markup, decided against the design-system pattern
  rather than invented — and confirmed at 44×44.

---

*Draft 2026-08-10. Brainstormed against the signed-off Slice 2a spec, following Plan 3's merge.
**Stress-tested the same day — no critical findings; twelve fixes folded in, the load-bearing ones
being the JSON-only reasoning that lets antiforgery wait for Plan 8, moving the nudge off the
response path so the constant-time promise holds, and the three dead ends (`/login` had no route,
`hideAppChrome()` had no inverse, and the verify screen had nowhere to send anyone).**
Next: review by both owners, then the implementation plan.*
