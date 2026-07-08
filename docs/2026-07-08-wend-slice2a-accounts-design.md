# Wend — Slice 2a design spec: Accounts & Identity

- **Date:** 2026-07-08
- **Status:** Draft — direction verbally confirmed by both owners; **stress-tested 2026-07-08 (12 findings folded in)**; pending Malin's review + sign-off
- **Owners:** Malin & Henry (equal ownership)
- **Repo:** `github.com/wendhq/wend`
- **Slice:** 2a of **Slice 2 — Sharing & Accounts** (2a Accounts first, 2b Sharing next)

---

## Context — why we're building this

Slice 1 shipped a genuinely usable, **local, single-user** kanban board. Slice 2's headline is **sharing** — but sharing is really two stacked subsystems, and only one of them can come first:

- **2a — Accounts & identity** (this spec): who you are, logging in, and boards that *belong to* someone.
- **2b — Sharing & collaboration** (next spec): board membership, roles, and invitations.

We split them deliberately, and do **2a first**, for three reasons:

1. **Sharing is impossible without it.** Slice 1 binds Kestrel to `localhost` only — two people can't reach the same `127.0.0.1:5174`. Ownership and login have to exist before "who else can touch this board" even means anything.
2. **It isolates the scary parts.** Authentication and the EF **migrations** switch (deferred through all of Slice 1) are best landed and tested on their own, before collaboration semantics stack on top.
3. **It's already a full slice.** Each is big enough for its own spec → plan → build cycle, exactly like every Slice 1 plan.

Same three goals as always: a **real tool** we use daily, a **portfolio piece**, and a **learning vehicle** — and public-facing authentication is the most interview-legible backend work we'll do this year.

---

## Goals & non-goals

**2a goals**

- **Public multi-user accounts** (real SaaS posture — anyone can register).
- Full account lifecycle: **register → verify email → log in / log out → forgot-password reset**, plus **change email/password**, **account deletion** (GDPR erasure), **login lockout**, and **remember-me**.
- **Per-user data isolation** — every board belongs to a user; you only ever see or touch your own.
- Adopt EF Core **migrations** and move persistence to **PostgreSQL**.
- Move Wend from a localhost-only single-user app to a **hosted, authenticated web app**.

**2a non-goals** (deferred)

- **Sharing, board membership, roles, invitations** — that *is* Slice 2b, and it's the reason 2a exists.
- **Two-factor auth** and **social / OAuth login** — additive, large surface, not launch-critical.
- Real-time sync, comments, an admin console, email-based notifications beyond auth.

---

## Product overview

Wend becomes a **hosted, authenticated web app**. The same single ASP.NET Core app still serves both the minimal API and the vanilla-JS frontend from one origin — but now the frontend sits behind a login, and every board is owned by the signed-in user.

Authentication uses **ASP.NET Core Identity** for the security-critical internals (password hashing, email-confirmation and reset tokens, lockout), driven by **our own minimal-API endpoints and hand-authored vanilla-JS screens** — so Wend keeps its no-build-step, accessibility-first, design-system-driven character rather than swallowing a scaffolded framework UI.

---

## Decisions locked (brainstorm, 2026-07-08)

| Decision | Choice |
|---|---|
| **Audience** | Public sign-up (real SaaS posture) |
| **Auth mechanism** | ASP.NET Core **Identity** + **cookie** sessions |
| **Identity ↔ frontend** | Custom minimal-API auth endpoints + hand-authored vanilla-JS screens (leaning on Identity internally) |
| **Database** | **PostgreSQL** in dev, test, and prod; EF **migrations** adopted, fresh baseline, dev data dropped |
| **Identity model** | Email = login credential; plus a `DisplayName` |
| **Email-verify gate** | Confirmed email **required before first login** (blocks email-squatting) |

---

## Architecture

One ASP.NET Core app (`net10.0`), same three-project shape as Slice 1 — extended, not restructured.

| Layer | Project | Slice 2a additions |
|---|---|---|
| Frontend | `Wend.Api/wwwroot` | `js/auth/` MVC modules (login · register · verify · forgot/reset · account-settings); an auth gate in `main.js`; CSRF-token handling in `js/api.js` |
| Web API | `Wend.Api` | `AuthEndpoints` (`/api/auth/*`); Identity + cookie-auth configuration; security middleware (antiforgery, rate limiting, HTTPS/HSTS); per-user authorization on existing endpoints |
| Domain | `Wend.Core` | `WendUser : IdentityUser`; `WendDbContext : IdentityDbContext<WendUser>`; `Board.OwnerId`; the `IEmailSender` seam; **EF migrations** |
| Tests | `Wend.Tests` | Testcontainers Postgres; per-user isolation + auth-flow coverage |

```
Auth + Board UI (wwwroot, vanilla JS MVC + design-system)
        │  fetch() · JSON · auth cookie + CSRF token
   Wend.Api  (minimal API: /api/auth/*, /api/boards, …)
        │      · Identity · cookie auth · antiforgery · rate limiting
   Wend.Core (WendUser · IdentityDbContext · IBoardRepository · IEmailSender)
        │  EF Core + migrations
   PostgreSQL   (dev: Docker · test: Testcontainers · prod: managed)
        │
   Email provider (behind IEmailSender; dev = console/file)
```

**Storage seam.** Persistence still sits behind the `IBoardRepository` family; 2a swaps the EF provider from SQLite to **Npgsql/PostgreSQL** and adds Identity's stores against the same `WendDbContext`. Because the app depends on the interfaces, the provider swap is contained.

---

## Data model

```
WendUser ──1:*── Board ──1:*── List ──1:*── Card ──*:*── Label
   │                                          │
   │  (Identity tables: AspNetUsers,          └──1:*── ChecklistItem
   │   AspNetUserTokens, AspNetUserClaims, …)
```

| Entity | Change in 2a |
|---|---|
| **WendUser** | *New.* `IdentityUser` subclass — Id (string PK) · Email (login) · `DisplayName` · plus Identity's fields (password hash, security stamp, lockout, email-confirmed). |
| **Board** | Gains a **required `OwnerId`** FK → `WendUser`. |
| List · Card · Label · CardLabel · ChecklistItem | Unchanged shape; reached only through a board the current user owns. |

- **Ownership cascade:** deleting a `WendUser` cascades to their boards → lists → cards → labels/checklist items. This is what makes **account deletion a clean GDPR erasure** — one delete removes the account and every trace of its data.
- **Per-user scoping is the security boundary.** Every board/list/card/label query filters to the current user. A board the user doesn't own returns **404, not 403** — the API never confirms that another user's board exists (enumeration resistance carried into the data layer).
- **`DisplayName` and email are user-controlled content.** They're validated at write time (length cap, control characters stripped) and **escaped at every interpolation** — DisplayName especially, because Slice 2b will render it on *other users'* boards, so an unescaped value would be stored XSS across a trust boundary.
- `WendDbContext` becomes `IdentityDbContext<WendUser>`, so Identity's schema is created and migrated alongside Wend's own tables.

---

## Authentication & session

- **ASP.NET Core Identity** with **cookie** authentication. Cookie is `HttpOnly` · `Secure` · `SameSite=Lax`, sliding expiration; **remember-me** issues a longer-lived persistent cookie. No token in `localStorage` — this preserves the Slice 1 XSS discipline.
- **SPA-shaped responses:** on `/api/*`, an unauthenticated request returns **401** rather than redirecting to a server-rendered login page (there isn't one — the client owns routing).
- **Email confirmation required** before first login (`RequireConfirmedAccount`).
- **Password policy:** favour length over forced complexity (aligned with current NIST guidance); exact minimum and rules confirmed at plan time against Identity's options.
- **Login lockout:** temporary lockout after a small number of failed attempts; exact threshold/window set at plan time.
- **Session invalidation on security events:** set a short `SecurityStampValidationInterval` so cookies re-validate quickly. Password reset, change-password, change-email, and account deletion all bump the security stamp (Identity does this) — a **reset additionally forces sign-out-everywhere**, and a **deleted** user's cookie is refused on its next request. This is the difference between "reset my password because I'm compromised" actually evicting the attacker vs. leaving them a live session; it's covered by tests, not left to the default validation interval.

---

## Auth flows & API surface (illustrative)

Custom minimal-API endpoints under `/api/auth/`, written in the existing `BoardEndpoints` style, calling Identity's `UserManager` / `SignInManager` internally:

| Endpoint | Behaviour |
|---|---|
| `POST /register` | Create user (email · password · display name), issue an email-confirmation token, send the verification email. **Returns generic success even if the email already exists** (enumeration resistance). Rate-limited (see below). |
| `GET  /verify` | Confirm email from the token in the emailed link (**single-use, short expiry**). An invalid / expired / already-used link lands on an accessible "request a new one" state, never a raw error. |
| `POST /resend-verification` | Re-send the confirmation email for an unverified account. Rate-limited; generic success. |
| `POST /login` | `PasswordSignInAsync` with lockout + remember-me; sets the auth cookie. Returns a **single generic "invalid email or password"** for bad creds, unknown email, **and** unconfirmed account — no response distinguishes them (no account-existence leak); unknown users are dummy-hashed for constant time. Re-confirmation is nudged **out-of-band by email**, never in this response. |
| `POST /logout` | Sign out, clear the cookie. |
| `GET  /me` | Current user (display name, email) for the SPA's boot-time session check; 401 if not signed in. |
| `POST /forgot-password` | Issue a reset token, email it. **Always returns generic success** (no enumeration). Rate-limited. |
| `POST /reset-password` | Validate the (single-use, short-expiry) reset token, set the new password, invalidate outstanding reset tokens, sign out everywhere. |
| `POST /change-password` | Authenticated; old + new password. |
| `POST /change-email` | Authenticated; confirm the new address via an emailed token before it takes effect (uniqueness re-checked at confirm time). |
| `DELETE /me` | Authenticated account deletion; re-enter password to confirm; cascades all owned data (GDPR erasure) and invalidates the session. |

All existing board/list/card/label/checklist endpoints gain **`RequireAuthorization()`** and per-user ownership checks.

**Unverified-account lifecycle.** Registration creates an unverified account that cannot log in. Unverified accounts are **purged after a set window** (duration at plan time), and the rate-limited `POST /resend-verification` lets a real owner re-send the link — or reclaim an email that a bot registered first, closing the squat. Register, forgot-password, and resend are all treated as **rate-limited email-sending endpoints** (per-IP + per-account), since each triggers outbound mail and is otherwise an email-bombing vector.

---

## Security posture

Moving off localhost flips the threat model from "single trusted user" to "hostile public internet." Baked in from day one:

- **CSRF / antiforgery** on all state-changing endpoints, including **login and logout** (cookies auto-send, so `SameSite` alone isn't sufficient, and login-CSRF is a real vector). Exact pattern — antiforgery token header vs double-submit cookie — confirmed at plan time.
- **Rate limiting** (ASP.NET Core rate-limiting middleware) on `/api/auth/*`, especially login / register / forgot-password / resend, to blunt credential stuffing and email-bombing. Per-account lockout complements per-IP limiting.
- **Enumeration resistance** on register, forgot-password, **and login** — all give generic responses, and login additionally equalises timing — carried into the 404-not-403 data scoping.
- **Email token handling:** verification and reset tokens are **single-use with short expiry** (verify: hours; reset: ~1 hour) and are **never written to logs** (the Slice 1 path-logging exclusion extends to query strings). The verify/reset landing pages load **zero third-party resources**, so no `Referer` header can leak a live token; any password change invalidates outstanding reset tokens.
- **HTTPS + HSTS** in production; `Secure` cookies require it. Dev runs over the HTTPS dev cert.
- **Secrets** (DB connection string, email-provider key) via **user-secrets** in dev, **environment variables** in prod — never committed to `appsettings`.
- **Same-origin, no CORS.** The frontend is served by the same app; we keep it that way and never open cross-origin access.
- **Logging hygiene** — request paths/bodies/query-strings stay out of framework logs; never log tokens, passwords, or PII. Behind a reverse proxy, honour `ForwardedHeaders` for correct scheme/IP (also so per-IP rate limiting keys on the real client).

---

## Email

- All outbound auth email goes through an **`IEmailSender`** seam.
- **Dev:** log the verification / reset link to the console (and/or a file) — click through locally, no real send, no provider account needed to build the whole slice.
- **Prod:** a transactional email provider (candidates: Postmark, Resend, Brevo). **Provider chosen at the deployment plan** — current pricing, free-tier limits, and EU-data-residency verified then, not guessed now. The design commits only to the seam; the provider is a **data processor** (see *Legal & privacy*).

---

## Legal & privacy

Public sign-up that collects personal data brings obligations Slice 1 never had. These are **launch** deliverables — required before public registration goes live, scheduled in the deployment plan:

- **Privacy policy + terms**, linked from the registration form, satisfying the GDPR Art. 13 duty to give notice at the point of collection.
- **Data-processing agreement (DPA)** with the transactional email provider — it's a processor receiving users' email addresses; EU data residency is a selection criterion.
- **Lawful basis + personal-data inventory.** The data processed is **email** (login), **display name**, and **transient IP** (rate limiting only). Basis: contract/consent for the account itself, legitimate interest for abuse prevention.
- **Retention.** Unverified accounts are purged (above); a stance on inactive-account retention is set at plan time (or explicitly deferred). Account deletion erases primary app data immediately; residue in **backups and logs** ages out on their own retention cycle — a known, time-bounded exception, not indefinite storage.

---

## Frontend

- New **`js/auth/`** modules — login · register · verify · forgot-password · reset-password · account-settings (change email/password, delete account) — each the same model/view/controller trio as `boards/`, `board/`, `card/`. The aria-live **`#status` + `#toast-region` shell wraps the auth screens too** (not just the authenticated app), so every async outcome — "verification email sent", "login failed", "session expired" — reaches screen-reader users.
- **Auth gate in `main.js`:** on boot the coordinator calls `GET /api/auth/me` → mounts the login module (401) or the app (200). On **any** gate mount — boot 401 or a mid-session bounce — focus moves to the login heading / first field with an announced reason ("Your session expired — please sign in again"); focus never drops to `<body>`.
- **Session expiry mid-edit:** a **401 during an in-flight edit** preserves the user's unsaved input where feasible (re-auth, then resume or return), and always announces the interruption rather than discarding work silently.
- **Expired / used / invalid links:** the verify and reset screens render an accessible **"this link expired or was already used — request a new one"** state (message + resend action + focus moved to it + announcement), never a raw error page.
- **Double-submit guards:** every auth form disables its submit control while a request is in flight — so a double-clicked login can't burn two lockout attempts and a double register can't send two verification emails.
- `js/api.js` gains the auth calls and **CSRF-token handling**; reuses `js/announce.js`, `js/escape.js` (the escaping discipline extends to **all** auth-form input and to DisplayName), `js/toast.js`, `js/prefs.js`.

---

## Accessibility commitments

The Slice 1 bar carries forward unchanged — auth is not an excuse to drop it:

- Dark-mode-first + design-system tokens; forced-colors and reduced-motion honoured.
- Every auth form: programmatically-labelled fields, `aria-describedby` for inline errors, an **announced error summary**, and **focus moved to the first error** (or the confirmation) after submit.
- **Correct `autocomplete` tokens** on every field — `email` / `username`, `current-password` (login, change-password), `new-password` (register, reset) — a win for password managers and SR autofill that also cuts mistyped-credential lockouts. New forms honour the Slice 1 touch-target baseline.
- Full keyboard operation through register / verify / login / reset / account-settings; visible focus rings; no sticky mouse-focus.
- The verify-email and reset landing pages — including their expired/used-link states — are first-class accessible screens, not afterthoughts.

---

## Testing

- **Two-tier test engine** — repository *unit* tests stay on fast in-memory SQLite (engine-agnostic CRUD); **API integration tests** — the real HTTP + EF path that ships — run on a throwaway **Testcontainers Postgres** (one container per run, a fresh database per test; CI already has Docker). The shipping path is Postgres-tested, so there is no production engine drift. *(Refined 2026-07-08 at Plan 1.)*
- **Critical coverage:**
  - **Per-user isolation** — user A cannot read, mutate, move, or delete any of user B's boards/lists/cards (each returns 404). The highest-value test set in the slice.
  - **Session invalidation** — a session is refused on its next request after a password reset and after account deletion.
  - **Enumeration resistance** — register, forgot-password, **and login** give identical responses for known vs unknown (and unconfirmed) emails.
  - **Lockout** — repeated failures lock the account for the window; a double-clicked login records **one** attempt, not two.
  - **Unverified-account lifecycle** — accounts purge after the window; resend / reclaim works.
  - **Token safety** — verify/reset tokens are single-use and expire; a reused link resolves to the accessible expired state.
  - Each flow end-to-end: register → verify → login → reset → change email/password → delete.
  - Migrations apply cleanly from an empty database.

---

## Sequencing (plan preview — detailed breakdown is the writing-plans step)

Roughly six to eight plans:

1. **Foundation** — Postgres + Npgsql, EF migrations adopted, `WendUser`, `IdentityDbContext`, `Board.OwnerId`, per-user scoping on existing endpoints, Testcontainers.
2. **Register + verify** — registration, email-confirmation tokens, the `IEmailSender` seam + dev sender, unverified-account lifecycle + resend.
3. **Login / session** — cookie auth, lockout, security-stamp invalidation, `/me`, `/logout`, the frontend auth gate.
4. **Forgot / reset password.**
5. **Account settings** — change email/password + remember-me.
6. **Account deletion** (GDPR erasure).
7. **Security hardening** — rate limiting, antiforgery, HTTPS/HSTS, headers, secrets.
8. **Deployment** — host + managed Postgres + TLS + live email provider + privacy policy / terms / DPA.

---

## Collaboration & workflow

Unchanged from Slice 1, with one addition:

- **Equal ownership**, org `wendhq`, repo `wend`. Branch → pull request → the other reviews & merges — nothing lands without two sets of eyes.
- Tests in **NUnit**; TDD where it fits. Commits authored by each person under their own account; **no AI/co-author attribution** in commit metadata.
- **New:** local dev now needs **Docker** (for the Postgres container in dev + tests). Turn-based work: whoever drives brings their Postgres container up; connection details come from user-secrets, not committed config.

---

## Key decisions (and why)

- **Split 2a/2b, accounts first** — sharing can't exist on localhost, and isolating auth + migrations de-risks the scary parts before collaboration stacks on.
- **Public SaaS posture** — the most interview-legible backend work available, and the honest target if Wend is a real tool other people can use.
- **ASP.NET Core Identity, not hand-rolled** — leans on battle-tested code for the parts that are dangerous to get subtly wrong on a public app (hashing, token generation, lockout); hand-rolling was kept out as a throwaway learning exercise, not production auth.
- **Custom endpoints + hand-authored screens (not scaffolded Identity UI)** — the only integration that survives contact with Wend's no-build-step, accessibility-first architecture.
- **Cookie sessions, not tokens-in-`localStorage`** — the safe default for a same-origin browser app; keeps the XSS discipline.
- **PostgreSQL now, at the migration boundary** — SQLite is single-writer (a real bottleneck once 2b has concurrent writers) and hosted platforms want a managed DB; deciding at the moment we adopt migrations avoids rebaselining a SQLite history later. App + **integration** tests + prod all on Postgres (the shipping path has no engine drift); repo unit tests stay on in-memory SQLite for speed.
- **404-not-403, and generic auth responses** — the API never leaks that another user's data, or a given email account, exists.
- **Sessions die on security events** — reset and deletion evict live sessions rather than trusting a cookie's clock; this is what makes those flows mean anything.
- **Deploy host + email provider deferred to the deployment plan** — neither changes the accounts design, and both deserve current-fact verification rather than a guess today.

---

## Open items — to confirm at plan time

- Email **provider** choice (deployment plan) and **deploy host** (Azure App Service · a PaaS · a VPS — tied to the GET Prepared backend-learning goal).
- Exact **password policy** and **lockout** thresholds (verified against Identity's options at plan time).
- Exact durations: **token expiries** (verify / reset), **unverified-account purge window**, **security-stamp validation interval**, and the **inactive-account retention** stance.
- Exact **CSRF** mechanism (antiforgery token header vs double-submit cookie).
- Framework specifics (Identity registration, Npgsql wiring, Testcontainers setup) verified against current .NET 10 docs when the first plan is written.

---

*Draft 2026-07-08. **Stress-tested 2026-07-08 across security / privacy / accessibility / loopholes — 12 findings folded in.** Next: Malin's review + sign-off — followed by the Slice 2a implementation plan.*
