# Wend — Slice 2a Plan 6 design: Account settings

- **Date:** 2026-08-13
- **Status:** Draft — brainstormed with Malin 2026-08-13; **stress-tested the same day across
  security / privacy / accessibility / loopholes — one critical finding, consciously deferred to Plan 8
  (see the footer and [`backlog.md`](backlog.md)); nine fixes folded in**; pending sign-off
- **Owners:** Malin & Henry (equal ownership)
- **Implementation:** **Malin writes it, Henry reviews and merges** (confirmed 2026-08-13). Reviewer's
  checklist: [`2026-08-13-wend-review-guide.md`](2026-08-13-wend-review-guide.md). See
  *Collaboration* below.
- **Repo:** `github.com/wendhq/wend`
- **Parent spec:** [`2026-07-08-wend-slice2a-accounts-design.md`](2026-07-08-wend-slice2a-accounts-design.md) (signed off, stress-tested)
- **Follows:** Plan 5 — forgot & reset password ([PR #44](https://github.com/wendhq/wend/pull/44), merged; suite at **253 green** as recorded at [PR #47](https://github.com/wendhq/wend/pull/47))
- **Depends on:** two smaller PRs landing first — the auth-input fix and remember-me (see *Depends on*
  below)

---

## Context — what this plan inherits

Plans 3, 4 and 5 built the account lifecycle up to the point where a person can register, confirm
their address, sign in, and recover a forgotten password. Every one of those flows is **anonymous**.
Nothing in Wend yet lets a signed-in user change anything about their own account.

Three inherited decisions shape this plan more than the parent spec does:

1. **`UserName` and `Email` are both set to the address at registration**
   ([`AuthEndpoints.cs:56`](../Wend.Api/AuthEndpoints.cs)). Nothing has ever needed to keep them in
   step, because nothing has ever changed either one. This plan is the first thing that does, and
   Identity does not do it for us — see *The `UserName` trap*, which is the single most important
   finding in this design.
2. **`options.User.RequireUniqueEmail = true`** ([`Program.cs:55`](../Wend.Api/Program.cs)). This
   switches on `UserValidator`'s uniqueness checks for **both** `Email` and `UserName`, which is what
   makes the trap above damaging rather than merely untidy — and which also means the parent spec's
   "uniqueness re-checked at confirm time" arrives free from the framework.
3. **`SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`**
   ([`Program.cs:137`](../Wend.Api/Program.cs)), bought in Plan 4 and first tested in Plan 5. Both
   flows in this plan rotate the stamp, so both evict live sessions on their next request. For change
   email that is the desired behaviour; for change password it would log the user out of the screen
   they are standing on, which is why `RefreshSignInAsync` appears below.

**Remember-me has been pulled out of this plan** and ships as its own small PR ahead of it, closing
the promise two code comments have been carrying since Plan 4 — *"Plan 6 adds remember-me as a
deliberate opt-in"* ([`Program.cs:111`](../Wend.Api/Program.cs)) and the hard-coded
`isPersistent: false` at [`AuthEndpoints.cs:246`](../Wend.Api/AuthEndpoints.cs). It was almost
independent of the rest of this plan, and three features in one PR is more than a reviewer who did not
write any of it should have to hold at once. One coupling survives the split and this plan owns it:
change-password reissues the cookie, so it has to preserve whatever persistence remember-me gave it.
See *Risks*.

---

## Goals & non-goals

**Goals**

- `POST /api/auth/change-password` — authenticated, current + new password, with lockout accounting.
- `POST /api/auth/change-email` and `POST /api/auth/confirm-email-change` — a two-step change with
  the confirmation link sent to the **new** address.
- A change-email token provider with its own one-hour lifespan.
- Two new `IAuthEmailSender` methods: the confirmation to the new address, and a notification to the
  **old** address that the sign-in address was changed.
- A new Account screen, and the link to it from Settings.

**Non-goals** (each named with the plan that owns it)

- **Remember-me on login** — pulled out and shipped as its own PR *ahead* of this plan. Its
  interaction with change-password's cookie reissue remains this plan's problem; see *Risks*.
- **Account deletion** — Plan 7, Malin's. It will want a section on this same Account screen; this
  plan builds the screen so Plan 7 inherits it rather than the reverse.
- **Rate limiting, antiforgery, HTTPS/HSTS, security headers, secrets posture** — Plan 8. Both new
  mail-sending paths ship unrate-limited, exactly as register, resend, login's nudge and
  forgot-password did. **Plan 8 remains a launch gate; Plan 9 must not deploy before it.**
- **Changing the display name.** The parent spec lists `DisplayName` as user-controlled content but
  never asks for an edit surface, and it is the one account field whose change has no security
  consequence at all — so it is a board-facing nicety that belongs with Slice 2b, where a display
  name first becomes visible to somebody else.
- **Preserving unsaved input across a mid-session 401** — still backlogged from Plan 4. Change email
  now produces such a bounce, which does not change the deferral.
- **The chips-vs-colour-bars label setting** ([`backlog.md`](backlog.md)), whose stated blocker was
  "no settings surface exists". A surface exists and this plan adds another; the item still waits for
  a slice that is about the board, not about accounts.

---

## Depends on

The auth text inputs measure ~32px against the 44×44 minimum ([`backlog.md`](backlog.md), deferred at
Plan 4 with the trigger *"revisit when the next slice touches auth styling"*). This plan is that slice,
and it adds two more forms of the same shape.

**The cause is not what the backlog says**, established 2026-08-13: the inputs carry **no `class`
attribute at all**, so they get no design-system input styling and compute `min-height: auto`. The fix
is `class="input"` on the seven inputs across the four auth views — the design-system component already
carries a 44px floor as of bundle 2.0.2 — not a `min-height` override in `app.css`, which would be a
second source of truth for the same number.

**It lands as its own PR before this plan starts.** Doing it first means this plan's new forms are the
first ones built against the correct floor rather than retrofitted in the same commit, and it isolates
the rendered-pixel measurement pass — owed since the design-system identity changed twice under Wend on
2026-08-12 and 2026-08-13 — into a PR that is only about that.

**Remember-me** lands as its own PR too, for the reasons in *Collaboration*. Its assertion that a
reissued cookie keeps its persistence is inherited here as task 5.

---

## Collaboration

**Malin writes the code; Henry reviews and merges** — agreed 2026-08-13, and it replaces the
coached turn-based mode. The two of them are no longer on the same team and Henry starts a new job at
the end of August, so the shape that survives is the one where his half is bounded and asynchronous. A
seven-task slice is not that; a review is.

This design was brainstormed and stress-tested jointly before the split was agreed, and none of it
changes as a result — but two things about *how it lands* do:

- **The review is the whole safety net now.** With one person writing all of it, nobody else has read
  the code before it reaches `main`. That is why the four findings below are each paired with the
  specific test that catches their absence, and why they are repeated as a reviewer's checklist in
  [`2026-08-13-wend-review-guide.md`](2026-08-13-wend-review-guide.md) rather than living only here.
- **Smaller PRs matter more, not less.** Remember-me was split out because a first solo slice should
  not carry three features; that reason is gone, but the conclusion holds for a better one — a
  reviewer who did not write any of it reviews three focused PRs more effectively than one large one.

Unchanged:

- **Nothing runs in parallel.** Plan 7 does not start until this merges — its account-deletion UI
  lands on the Account screen this plan creates. Two plans open at once collide in `AuthEndpoints.cs`,
  `main.js` routing and that screen simultaneously, which is the worst place in this codebase for a
  merge conflict. Plan 8 is the exception worth knowing about: it is middleware and configuration where
  this plan is handlers and screens, so those two *can* overlap if there is ever a reason.
- **Squash** — these branches are single-author now, so squash is the default. Merge-not-squash only
  applies to a branch carrying commits from both owners, where squashing would attach a co-author
  trailer. Then confirm the remote branch actually deleted; the auto-delete has silently no-op'd once.
- **No AI attribution** in commits or PR bodies.

---

## Decisions locked (brainstorm, 2026-08-13)

| Decision | Value | Why |
|---|---|---|
| **Where account settings live** | A new Account screen, `js/auth/account/`, reached by a link **on the Settings screen** — not a third control in the header | The existing settings model is localStorage prefs; folding server-backed account mutations into it gives one model two jobs and breaks the layer rule. A link from Settings also keeps `APP_CHROME` a two-item list, which is where the `[hidden]` trap lives. |
| **Whether the Account screen has a URL** | **No.** Reached by a link, exactly as Settings is reached by a header click | Settings has no URL either, so this matches the one precedent that exists, and it keeps the app's route table entirely anonymous — every one of the five existing routes is an auth screen reached before the gate. A gated route would make Account the first exception and invite exactly the mistake of adding it to the early `switch` by pattern-match, producing a screen that renders for a signed-out user and 401s on first use. Cost: no deep link and a refresh returns to the board overview. Revisit if Plan 7's deletion flow makes that bite. |
| **Change email: one step or two** | Two endpoints — request, then confirm from the emailed link | The parent spec's table shows one `POST /change-email` row, but the behaviour it describes ("confirm the new address via an emailed token before it takes effect") cannot be one round trip. Recorded as a deviation. |
| **Where the pending address lives** | In the confirmation link's query string, alongside `userId` and `code` | Identity binds the change-email token to the new address, so it has to travel with the token. The alternative — a `PendingEmail` column — buys "no PII in the URL" at the cost of a migration and genuinely stateful behaviour (overwrite rules, clearing, abandonment), and it does not remove the Plan 9 log-exclusion gate that `/verify` and `/reset-password` already sit behind. It only narrows it. |
| **`confirm-email-change` auth** | **Anonymous**, like `/verify` and `/reset-password` | The link lands in a mailbox the user may open in a different browser, and possession of a token bound to *(user, new address, security stamp)* is the proof. It also means the change completes even if the session expired in the meantime. |
| **Change email to an address another account holds** | Generic `204`, no mail | Without this, account settings becomes a user-table enumeration oracle an authenticated attacker drives from their own account — the same discipline register already applies, at a surface that looks private and is not. |
| **Change email to the address you already have** | `400 { error: "same" }` | The caller is authenticated and already knows their own address, so naming this leaks nothing — and a silent 204 produces a "check your inbox" for mail that will never arrive. |
| **Change-email token lifespan** | **1 hour**, in its own `WendChangeEmail` provider | Matching reset rather than confirmation's 24 hours: the user typed the address seconds ago and has one mailbox to reach. The dedicated provider is the standing rule — without one, a lifespan set here silently becomes the lifespan of every confirmation link. |
| **Notify the old address** | Yes, after a successful change | The cheapest real defence in the plan. An attacker holding a live session — stolen cookie, borrowed unlocked laptop — can otherwise repoint an account silently, and the owner finds out the next time they fail to log in. |
| **Change-password errors** | Distinguishable: `{error:"password"}` for policy, `{error:"current"}` for a wrong current password | The caller is already authenticated. There is no account existence left to leak, and one error for both produces a screen that blames the new password when the old one was mistyped. |
| **Lockout accounting on change-password** | `AccessFailedAsync` on a wrong current password, `ResetAccessFailedCountAsync` on success, refuse when locked out | `ChangePasswordAsync` does no lockout accounting, so without this the endpoint gives somebody holding a stolen session unlimited guesses at the current password — and a correct guess converts a borrowed session into permanent takeover. **Beyond the parent spec's one-line description; adjudicated in the brainstorm and recorded as a deviation.** |
| **The acting session after a change-password** | Survives, via `RefreshSignInAsync`; every other session dies | The stamp rotation is what kills the others and that is wanted. Bouncing the user off the screen they just used is not, and Identity provides the refresh for exactly this case. Contrast Plan 5, where there is deliberately no carve-out — a reset arrives by email and may not be the owner. |
| **Remember-me** | Out of this plan — its own PR ahead of it | Almost independent: one request field, one argument, one checkbox, two assertions. Splitting it keeps this PR to two features instead of three, which matters most for the person reviewing code they did not write. The one coupling it leaves behind is change-password's cookie reissue. |

---

## The `UserName` trap

**Verified against the `release/10.0` source, not assumed.** `UserManager.ChangeEmailAsync` mutates
`Email`, `NormalizedEmail`, `EmailConfirmed` and `SecurityStamp`. It **does not touch `UserName`.**

Wend sets `UserName = Email` at registration and nothing has ever changed either. So a change-email
that calls only `ChangeEmailAsync` leaves `UserName` holding the **old** address permanently. The
consequences, in the order someone would discover them:

- **Login keeps working**, because login resolves through `FindByEmailAsync` → `NormalizedEmail`. So
  the bug passes every obvious test.
- **The abandoned address stays occupied.** `RequireUniqueEmail = true` switches on `UserValidator`'s
  `UserName` uniqueness check as well as the email one, so the old address is still taken as a
  username even though no account presents it as an email any more.
- **A later registration to that address fails `DuplicateUserName`** — and register answers
  post-validation failures with **`204` and a code-only log line**
  ([`AuthEndpoints.cs:74`](../Wend.Api/AuthEndpoints.cs), written that way so a uniqueness race
  cannot leak existence). The caller sees success. No mail is ever sent. Nothing surfaces to the
  user, and the only trace is a warning in the server log.

So the failure is silent, arrives arbitrarily later than the change that caused it, and lands on a
*different person* than the one who caused it. `SetUserNameAsync` alongside `ChangeEmailAsync` is
therefore not tidiness — it is the plan's correctness requirement.

**The regression test is specified precisely because nothing else would catch it:** after a completed
change-email, register the **old** address fresh and assert a confirmation mail goes out. Without
`SetUserNameAsync` that test fails and every other test in the suite still passes.

---

## Token provider

`ChangeEmailTokenProvider<TUser>` and `ChangeEmailTokenProviderOptions`, a third instance of the
pattern [`PasswordResetTokenProvider`](../Wend.Api/PasswordResetTokenProvider.cs) exists to enforce:

```csharp
public class ChangeEmailTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public ChangeEmailTokenProviderOptions()
    {
        Name = "WendChangeEmailTokenProvider";
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
```

registered alongside the other two and wired through `options.Tokens.ChangeEmailTokenProvider`.

The existing both-lifespans test grows a third assertion rather than gaining a third test: the
failure mode being guarded is one provider's lifespan silently dragging another's, and that is one
property across three values, not three properties.

---

## Email seam

`IAuthEmailSender` gains two methods:

```csharp
Task SendEmailChangeConfirmationAsync(string newEmail, string link);

Task SendEmailChangedNoticeAsync(string oldEmail, string newEmail);
```

The second takes both addresses because the notice has to name what the address was changed *to* —
otherwise it tells the owner something happened without telling them enough to act on it.

`FileAuthEmailSender` implements both the way it implements the existing two, and the test fake keeps
recording message *type* as well as recipient — several tests below turn on **which** mail went out
and **to which of the two addresses**, not merely that one did.

Link construction reuses the existing shape exactly: Base64Url-encoded token, origin from
`Wend:PublicBaseUrl` rather than the request host, falling back to the request host in Development
only. The Host-header reasoning is unchanged and applies with full force here — a link that repoints
an account's login identity is worth as much to an attacker as a reset link.

---

## `POST /api/auth/change-password`

Request `{ currentPassword, newPassword }`. Response `204`, or `400` with an error code.
**Authenticated.**

The handler, in order:

1. Resolve the user from the principal. `GetUserAsync` returning null on an authenticated request
   means the account was deleted under a live cookie → `401`, not a 500.
2. **Policy** on the new password. Failure → `400 { error: "password" }`, before the current password
   is checked. Note the ordering differs from reset, deliberately: reset validates policy *before* the
   lookup because it is anonymous and an early 400 would otherwise be an existence oracle. Here the
   caller is already known, so the user is resolved first and the validators are handed the **real**
   user — which is what a future policy like "your password may not contain your email address" would
   need. The error precedence the screen sees is unchanged.
3. **Locked out → `401`**, without checking the password. A locked account is locked for this too;
   otherwise lockout is trivially sidestepped by anyone holding a session.
4. `ChangePasswordAsync(user, currentPassword, newPassword)`.
   - Failure → `AccessFailedAsync(user)`, then `400 { error: "current" }`.
   - Success → `ResetAccessFailedCountAsync(user)`.
5. `RefreshSignInAsync(user)` — **after** the change, so the rotated stamp lands in the reissued
   cookie. If it fails, still **204**, with a code-only warning log: the password genuinely changed,
   and telling the user otherwise would send them round the loop for nothing. The degraded outcome is
   that their next request 401s and they sign in again with the new password, which works. Same rule
   Plan 5 applies to a failed lockout clear.
6. `204`.

**Step 4's accounting is the security content of this endpoint.** `ChangePasswordAsync` verifies the
current password and does no lockout bookkeeping whatsoever, so without step 3 and the failure branch
of step 4, somebody holding a stolen cookie gets unlimited attempts at the current password — and
succeeding converts a session that dies on its own into a permanent takeover. Five attempts is the
same budget login allows, applied to the same secret.

**This endpoint is hardened against a stolen session and `/change-email` is not**, which is a known
asymmetry, decided rather than overlooked — see the *stolen-session takeover* entry in
[`backlog.md`](backlog.md) for the full path and the Plan 8 trigger.

**Step 5 is what keeps the user on the screen.** The password write rotates the security stamp, and
with `ValidationInterval = TimeSpan.Zero` that refuses every live cookie for this user on its next
request — including the browser that just submitted the form. Every *other* session dying is the
point; this one dying is a bug the user experiences as being logged out for changing their password.

**The distinction from Plan 5 is deliberate and should not be smoothed over.** Reset explicitly has
no carve-out for the acting session, because a reset link arrives by email and the person holding it
may not be the owner. Change-password required the current password from an already-authenticated
session, which is a materially stronger proof, and the acting session is one the user is standing in.

---

## `POST /api/auth/change-email`

Request `{ newEmail }`. Response `204`; a bare `400` for a malformed or over-length address; or
`400 { error: "same" }`. **Authenticated.**

The bare 400 carries no code deliberately: the screen validates format client-side first, so reaching
it means a caller bypassing the form, and there is no screen state that needs to tell the two apart.

The handler, in order:

1. Trim. Empty or over 254 characters → `400`. Malformed per `EmailAddressAttribute` → `400`.
   Input-only checks, before any lookup, per the standing rule.
2. Resolve the user from the principal; null → `401`.
3. **Same as the current address** (normalised comparison, not a raw string one) →
   `400 { error: "same" }`.
4. `FindByEmailAsync(newEmail)` **or** `FindByNameAsync(newEmail)` finds **another account — self
   excluded** → `204`, having done nothing. Both lookups, because the trap above means an address can
   be occupied as a username while free as an email — and shipping this endpoint without the
   `FindByNameAsync` check would produce a confirmation mail for an address that then fails at confirm
   time.
   **Self must be excluded, and it is reachable:** in the 409 half-changed state below, `Email` is the
   new address and `UserName` is still the old one, so `FindByNameAsync(old)` returns the caller.
   Excluding self lets that user re-request their old address and repair the desync; not excluding it
   hands them a silent 204 forever, on the one address they most want back.
5. Free → `GenerateChangeEmailTokenAsync(user, newEmail)`, build the link to
   `/confirm-email-change?userId=…&newEmail=…&code=…`, send via
   `SendEmailChangeConfirmationAsync`.
6. `204`.

**Step 4's generic 204 is the enumeration boundary, and it is easy to lose.** The endpoint is
authenticated and feels private, which is exactly why a `409 Conflict` looks reasonable here. It
would let any account holder walk the user table one address at a time from their own settings page.

**Nothing is written in step 5.** The account still has its old address until the link is clicked, so
an abandoned request leaves no state to clean up and no pending-change indicator to render. That is
the trade accepted with the query-string decision.

**A second request does not revoke the first.** Nothing rotates on request, so two requests leave two
live tokens, each good for its full hour, and whichever link is clicked first wins — the other then
fails its token check because the first rotated the stamp. Someone who re-requests *because they think
the first link was seen* has revoked nothing. This is the same property Plan 5 documented and tested
for reset tokens, and it gets the same treatment here: a test asserts an older change-email token
still works after a newer one is issued, so the behaviour is written down rather than discovered.

---

## `POST /api/auth/confirm-email-change`

Request `{ userId, newEmail, code }`. Response `200 { email }`, `400 { error: "token" }`, or
`409 { error: "taken" }`. **Anonymous.**

**`POST`, not `GET`, even though this arrives from an emailed link** — Plan 3's reasoning for `/verify`,
and it bites harder here. Corporate mail scanners and link-preview bots follow GET links automatically,
so a GET that applied the change would be fired by a robot before the human ever clicked, silently
repointing an account's login identity. The emailed link therefore points at the **SPA shell**, and the
screen POSTs the values back from the browser on mount.

The handler, in order:

1. `userId` missing → `400 { error: "token" }`. `FindByIdAsync` finds nothing →
   `400 { error: "token" }`.
2. Base64Url-decode the code; `FormatException` → `400 { error: "token" }`.
3. `ChangeEmailAsync(user, newEmail, token)`.
   - A token failure → `400 { error: "token" }`.
   - A `DuplicateEmail` failure → `409 { error: "taken" }`. This is `UserValidator` firing inside
     `UpdateUserAsync`, which is where the parent spec's "uniqueness re-checked at confirm time"
     actually happens — the framework does it, and this plan's job is to tell the two failures apart
     and test that it does.
4. `SetUserNameAsync(user, newEmail)`, on the **same `user` instance** — `ChangeEmailAsync` has just
   refreshed its concurrency stamp, and reloading here would work from a stale one. Same two-call
   pattern as Plan 5's lockout clear.
   - Failure → `409 { error: "taken" }` **and a code-only warning log**. The account is now in the
     half-changed state this design exists to prevent, and the endpoint must not report success.
5. Fire the old-address notice from `response.OnCompleted`, like login's nudge, so the send stays off
   the response path.
6. **`200 { email }`**, with the address read back off the user *after* both writes.

**Step 6 returns a body on purpose, and it is the only endpoint in `/api/auth/*` that does.** The
success screen has to name the new address, and the only two other sources are the query string — a
caller-controlled value on an anonymous page, which is the reflected-XSS shape the rule below forbids —
and nothing at all, which leaves the user to trust that the address they typed ten minutes ago is the
one that landed. Echoing the stored value means the screen reports what is actually in the database.

**Step 3's two failure codes are not interchangeable.** A bad token means "get a new link"; a taken
address means "pick a different address". Collapsing them produces the Plan 5 failure mode in
reverse — a screen telling someone their link expired when the link was fine and the address was
taken by somebody else four minutes ago.

**Step 4's failure is the narrowest path in the plan and still needs its branch.** Both writes
validate uniqueness on the same value, and Wend's invariant is `UserName == Email`, so an address
free as one is normally free as the other — step 4 of `/change-email` checks both precisely to keep
it that way. What remains is a genuine race between two confirmations, and the 409 plus the log is
how it surfaces instead of corrupting the account silently. A retry then fails the token check,
because step 3 already rotated the stamp, so the user needs a fresh link — which is what the screen
tells them.

**Why the stamp rotation is right here.** `ChangeEmailAsync` rotates it, so every live session for
this user is refused on its next request. There is no `RefreshSignInAsync` and there must not be
one: this request is anonymous and may be arriving from a different browser than the one holding the
session, so "refresh the acting session" has no meaning. The user signs in with their new address.

---

## What remember-me leaves behind

Remember-me ships ahead of this plan, so by the time change-password is written a login can already
have issued a **persistent** cookie. That is the one thing the split does not separate: step 5 of
`/change-password` reissues the cookie, and if the reissue does not carry the original's persistence
then changing your password silently demotes a remembered session to a session cookie. Nothing in
either feature looks wrong; the user simply finds themselves signed out a day later.

So this plan inherits one assertion from the remember-me PR rather than the feature itself: **sign in
with remember-me, change the password, and confirm the reissued cookie still carries `expires`.**

---

## Security posture

- **Enumeration.** `/change-email` answers `204` for both "free" and "held by someone else". The one
  `400` it has names a property of the caller's own account, which they already know.
- **Timing.** The taken branch skips token generation and the send, so it returns measurably faster
  than the free branch. Same side channel as register and forgot-password, and it joins their
  existing Plan 8 backlog item rather than getting a bespoke dummy-work path here.
- **Outbound mail.** Two new sends, both requiring an authenticated session — so neither is a cheap
  anonymous email-bomb vector in the way `/forgot-password` is. They are still unrate-limited and
  still belong in Plan 8's scope.
- **Session eviction.** Change-email evicts every session including the acting one, because the
  acting one may be in a different browser. Change-password evicts every session *except* the acting
  one, deliberately, on the strength of having just verified the current password.
- **Lockout.** Extended to change-password, which is a strengthening. It also creates a new denial
  surface, and it is wider than this endpoint: the lock it sets is the *same* lock login checks, so
  five deliberate wrong current passwords lock the real owner out of **the whole application** for
  fifteen minutes, repeatably. Same shape as the login-lockout DoS already in the backlog, and the
  same answer — Plan 8's rate limiting, not a weaker threshold.
- **Token handling.** One hour, stamp-bound, never logged. `/confirm-email-change` strips `userId`,
  `newEmail` and `code` from the address bar with `history.replaceState` on read, exactly as `/verify`
  and `/reset-password` do. **The query string now carries an email address as well as a token**, so
  the route joins the existing Plan 9 log-exclusion gate — and the backlog entry gets updated to say
  the exposure is PII, not only a credential.
- **Logging.** Error codes only, never addresses or tokens. Unchanged precedent, and it applies to
  the new `SetUserNameAsync` warning.
- **CSRF.** All three endpoints bind JSON only — the same reasoning that lets antiforgery wait for
  Plan 8, and the same form-encoded-POST rejection test each new endpoint carries.
- **XSS.** The Account screen renders the user's current address, and `/confirm-email-change`
  renders the new one. Both are user-controlled strings reaching a template literal, so both go
  through `escapeHtml`. `newEmail` additionally arrives from a query string on an anonymous page
  anybody can link to, which makes it the same reflected-XSS shape Plan 5's `code` was — so it
  follows Plan 5's rule: **the query-string value lives in the controller closure and the model, is
  never handed to the view, and is never rendered.** The success state renders the `email` field from
  the **`200` response body** instead, escaped — which is why that endpoint returns a body at all.
- **No third-party resources on `/confirm-email-change`**, the same rule `/verify` and
  `/reset-password` carry so no `Referer` header can hand a live token to another origin. Restated
  rather than inherited silently, because this screen's URL carries **an email address as well as a
  token**, so a leak here discloses PII on top of a credential.

---

## Frontend

**The Account screen** — `js/auth/account/{model,view,controller}.js`, no route, mounted by
`showAccount()` from inside an authenticated session.

- Shows the current address (escaped) and hosts two forms: change password, change email.
- **Two forms on one screen is the new wrinkle**, and every auth screen so far has had exactly one, so
  the announcer and the focus helpers have never had to disambiguate. Three rules, all three
  load-bearing:
  1. **Each form owns its own error region** — its own element, its own `aria-describedby`, its own
     disable-while-in-flight. A submit in one form **never clears or rewrites the other's** error: the
     user has not fixed it, and blanking it removes the only record of what is still wrong.
  2. **Focus after any submit stays inside the submitting form** — its first invalid field on a
     validation error, the announced summary on a server error that belongs to no field, and its own
     heading on success. This is not optional bookkeeping: success clears both fields and therefore
     repaints, and a full-`innerHTML` repaint that does not explicitly refocus drops the user on
     `<body>`, which is the single most repeated frontend bug in this codebase.
  3. **Each form gets its own `<h3>` and is associated with it via `aria-labelledby`.** Without it, a
     screen-reader user landing in the second of two password-and-email-shaped forms has no way to
     tell which one they are in.
- Announcements still go through the shared announcer outside `#app`, which replaces rather than
  stacks — so the most recent outcome is the one announced, and the error regions are what preserve
  the older one.
- `autocomplete="current-password"` and `autocomplete="new-password"` on the password form;
  `autocomplete="email"` on the email form.
- The new-password field carries register's hint text and `minlength="12"`, wired with
  `aria-describedby`, as register and reset both do.
- One password field, no confirm-password — the house call, unchanged since register.
- `.btn` plus a variant on every control; a bare `<button>` is 28px.
- A back link to the **Settings screen**, not to the board overview: the user came from Settings, and
  an `onBack` callback the way every other screen's back link works.
- Success on change-password → announce, clear both fields, keep the user on the screen. This is the
  visible payoff of `RefreshSignInAsync`, and if that call is missing the screen will bounce to login
  instead, which is the symptom to watch for during the walk.
- Success on change-email → a persistent state above the form saying a link has been sent to the new
  address, with the form **left usable and submit re-enabled**. Plan 5's forgot-screen reasoning
  applies exactly: someone who mistyped the new address learns nothing from the response, so retrying
  must cost nothing but typing.

**`/confirm-email-change`** — `js/auth/confirm-email/{model,view,controller}.js`, anonymous.

- Reads `userId`, `newEmail` and `code`, then `history.replaceState(null, "",
  "/confirm-email-change")` immediately.
- **POSTs on mount**, with no button to press — the endpoint is a POST precisely so a mail scanner
  following the emailed link cannot complete the change, and the shell-plus-JS shape is what makes that
  true. Same as `/verify`.
- Calls `hideAppChrome()` on mount, like every auth screen.
- **Loads no third-party resources**, so no `Referer` can carry the token *or* the address off-site.
- Four accessible states, each with its own message, focus and announcement:
  - **No link** — reload, bookmark or back-navigation arrives with nothing, because `replaceState`
    stripped it. Settled *before* the model subscription so it renders once, mirroring verify's
    `noLink()`: "Nothing to confirm — open the link from your email." No request is sent.
  - **Success** — "Your sign-in address is now …. Please sign in." with a link to `/login`. The
    address comes from the `200` response body and is escaped on the way in — **never** from the
    query-string value the controller is holding.
  - **Expired or used** — the accessible link-is-stale state, with a note that a new change can be
    started from Account settings. Never a raw error.
  - **Taken since** — "That address is now in use — try a different one from Account settings."
- **This state is reachable only with a valid token, which is only ever issued for an address that
  was free**, so naming the conflict here does not contradict `/change-email`'s generic 204. An
  attacker probing for taken addresses never receives a token and never sees this screen.

**Settings screen changes**

- A link to the Account screen. The prefs model is untouched — the link is markup in the view plus an
  action the controller forwards, not new model state.

**Routing.** **One** new route, not two. `/confirm-email-change` is anonymous, so it joins the early
`switch` in [`main.js:255`](../Wend.Api/wwwroot/js/main.js) alongside `/verify`, ahead of the gate,
because an emailed link has to land somewhere regardless of session state.

**The Account screen gets no route at all.** It is mounted by `showAccount()`, called from the
Settings screen's link, exactly as `showSettings()` is called from the header — so it is only ever
reachable from inside an authenticated session and the gate never has to think about it. This keeps
every route in the `switch` anonymous, which is what stops the next person from adding an
authenticated one to it by pattern-match. The cost is that `/account` is not a deep link and a refresh
returns to the board overview, which is the trade Settings already makes.

---

## Testing

The two-tier engine is unchanged: repository unit tests on in-memory SQLite, API integration tests
against a per-test throwaway PostgreSQL database. New files `AuthChangePasswordTests.cs` and
`AuthChangeEmailTests.cs`, following the one-file-per-flow convention.

**The three that carry real weight**

- **The `UserName` regression** — after a completed change-email, register the **old** address fresh
  and assert a confirmation mail goes out. Without `SetUserNameAsync` this fails and nothing else
  does.
- **Persistence survives a password change** — sign in with remember-me, change the password, and
  assert the reissued `Set-Cookie` still carries an `expires` attribute. This **cannot** run through
  the `Test` auth scheme, which issues no cookie at all, so it needs `useTestAuth: false`; a
  test-scheme version would pass while testing nothing. The inherited assertion from the remember-me
  PR, and the only place the two features touch.
- **Stamp interaction across flows** — a password change between a change-email request and its
  confirmation kills the pending link. Correct behaviour, and the kind of coupling that only shows up
  if something looks for it.

**Change-password**

- Anonymous → 401. A wrong current password → 400 with the `current` code. A weak new password → 400
  with the `password` code, **and the current password still works afterwards** (the ordering guard).
- Five wrong current passwords lock the account; the sixth attempt is refused without the password
  being checked; a successful change clears the failed count.
- The acting session survives — the same client's next request is 200, not 401.
- **Another session dies** — a second client signed in as the same user gets 401 on its next request.
  The pair of assertions is the point; either alone is satisfiable by the wrong implementation.

**Change-email**

- Anonymous → 401 on `/change-email`. The current address → 400 `same`. An address held by another
  account → 204 **with no mail sent**, asserted on the fake's recorded type and recipient.
- An address held only as a `UserName` by **another** account → the same 204 with no mail. An address
  held only as a `UserName` by **the caller** — the 409 half-changed state — proceeds and sends mail,
  because that is the repair path out of the desync.
- A free address → exactly one confirmation mail, to the **new** address, and none to the old one.
- Confirmation applies both `Email` and `UserName`; login with the new address succeeds and login
  with the old one is 401.
- **One notice to the old address**, after success only — not on a token failure, not on the 409.
- A reused link → 400 `token`. A tampered `newEmail` with a valid-looking code → 400 `token`, because
  the token is bound to the address. A token minted for user A cannot change user B's address.
- **An older change-email token still works after a newer one is issued** — the counterpart to the
  reuse test, and the one that pins down what "single-use" does *not* mean here. Mirrors Plan 5's
  equivalent for reset tokens; if it ever starts failing, the revocation model has changed and this
  document is wrong.
- **Success returns `200` with the stored address in the body**, matching what the row actually holds
  rather than what was requested.
- **`RefreshSignInAsync` failing still answers 204** — the password changed, so the response says so.
- Every live session is refused after confirmation, including the one that requested the change.
- The 409 path — a second account claims the address between request and confirmation.
- **All three lifespans in one test** — change-email 1 hour, reset 1 hour, confirmation 24 — because
  the failure being guarded is one dragging another.
- **All three new endpoints refuse a form-encoded POST**, extending the login-CSRF guard.

**Frontend** tasks have no automated tests — this repo has no JS test harness — so they carry
scripted manual checks. Every browser check hard-reloads, because `UseStaticFiles` sends no
`Cache-Control`. Beyond the standing set (44×44 on every control, focus placement per outcome,
announcements, keyboard walk):

- **`minlength="12"` is checked with real key events**, never a scripted `.value` assignment —
  `tooShort` only fires on a user-edited value, so a scripted check records a false pass.
- **Neither `code` nor `newEmail` appears anywhere in the DOM** after `/confirm-email-change` mounts.
- **Reloading `/confirm-email-change`** renders the no-link state and sends no request.
- **A successful change-password leaves the user on the Account screen** — the `RefreshSignInAsync`
  check, and the one manual check that catches its absence.
- **Both forms' error regions are independent** — an error in one does not steal focus from,
  describe, or **clear** the other. Checked in both directions: submit each form while the other is
  showing an error.
- **Focus lands inside the submitting form on every outcome**, and never on `<body>` — including
  after a successful change-password, which repaints to clear its fields.
- **Each form is reachable and identifiable by screen reader** — two `<h3>`s, each form associated
  with its own via `aria-labelledby`.

Exact per-task test totals are pinned when the plan is written, against the **253** baseline: a
paste-driven edit once silently dropped three tests behind a green suite.

---

## Task breakdown (detail is the writing-plans step)

1. **Token provider and the email seam** — `ChangeEmailTokenProvider`, the 1-hour lifespan wired
   through `options.Tokens`, both new `IAuthEmailSender` methods, the file sender, the test fake.
   Includes the three-lifespans assertion.
2. **`POST /api/auth/change-password`** — validation ordering, the two 400 codes, lockout accounting,
   `RefreshSignInAsync`, and the acting-survives / others-die pair.
3. **`POST /api/auth/change-email`** — the four branches behind one 204, the `FindByNameAsync` check
   with self excluded, and the older-token-still-works assertion.
4. **`POST /api/auth/confirm-email-change`** — `ChangeEmailAsync` plus `SetUserNameAsync` on one
   instance, the token/taken split, the `200 { email }` body, the old-address notice, and the
   `UserName` regression test.
5. **The Account screen** — MVC trio, `showAccount()`, both forms with the three two-form rules, the
   Settings link, and the persistence-survives-a-password-change assertion inherited from the
   remember-me PR.
6. **The `/confirm-email-change` screen** — MVC trio, the anonymous route, POST-on-mount,
   `replaceState`, and the four states.
7. **Docs** — the four deviations recorded in the PR body. **The backlog work is already done:** the
   timing, rate-limiting and log-exclusion entries were extended on 2026-08-13, along with the
   stolen-session entry from the stress test. Read them rather than re-deriving; nothing in
   [`backlog.md`](backlog.md) is waiting on this plan.

---

## Risks

- **The `UserName` desync is the plan's one genuinely dangerous failure**, and it is silent, delayed,
  and lands on a stranger rather than on the person who caused it. It is guarded by exactly one test,
  which is why that test is specified in this document rather than left to the plan.
- **`RefreshSignInAsync` and the already-shipped remember-me may not compose.** If the refresh does
  not carry the current cookie's persistence across, then changing your password silently downgrades a
  remembered session to a session cookie — a bug nobody would think to look for, in two features that
  each work perfectly alone. The split makes this *more* likely to be missed, not less: the two
  features arrive in different PRs with different reviews, and nothing about either one suggests the
  other. Verified against the source at plan time, and asserted, not assumed. See *Open items*.
- **Two forms on one screen is the plan's accessibility risk**, not the endpoints. Every auth screen
  so far has had exactly one form and one error region, so the announcer and focus helpers have never
  had to disambiguate. The failure mode is a change-email error announcing over a change-password
  error, or focus landing in the wrong form.
- **The old-address notice goes to an address the account no longer owns**, by design. If the change
  was legitimate, the user receives one mail at an old address they still control. If the address was
  never theirs — a typo at registration that was nonetheless confirmed — the notice reaches a
  stranger. That is strictly better than the alternative, and worth having considered rather than
  discovered.
- **Lockout on change-password is a new denial surface**, mitigated only by the fact that anyone who
  can reach it already holds a session. Recorded because the reasoning must survive review rather
  than be re-derived under pressure.
- **The Account screen has no URL, which is a deliberate loss with three edges, not one.** A user
  cannot bookmark it; a refresh drops them on the board overview; and **a mid-flight 401 is the worst
  of the three** — the auth gate bounces them to login, and after signing back in they land on the
  board overview with no way back but Settings → Account, having lost whatever they had typed (the
  unsaved-input deferral from Plan 4 applies here too). Settings already behaves this way so it is
  consistent rather than surprising, but Plan 7 puts account deletion here, and a destructive flow that
  evaporates on refresh is more irritating than a preferences screen that does. If it bites, the fix is
  a gated route, and the trap that opens is in the decisions table so whoever adds it knows not to put
  it in the early `switch`.

---

## Key decisions (and why)

- **Two endpoints for change-email** — a token that has to reach the new address and come back cannot
  be one round trip, whatever the parent spec's table suggests.
- **Anonymous confirmation** — the link lands in a mailbox, possibly in another browser, and the token
  is the proof. Requiring a session would mean signing in with the old address to adopt the new one.
- **The pending address in the URL rather than a column** — Identity binds the token to the address,
  the pattern is twice-proven in this repo, and the alternative buys one privacy improvement for a
  migration plus real stateful behaviour without closing the log-exclusion gate it aims at.
- **A generic 204 for a taken address** — an authenticated endpoint that names which addresses exist
  is a user-table oracle with a login in front of it.
- **`SetUserNameAsync` alongside `ChangeEmailAsync`** — not tidiness. Without it, an address is
  permanently squatted and the next person to want it gets a silent 204.
- **Lockout accounting on change-password** — the endpoint verifies a password, so it is a guessing
  surface, so it counts. `ChangePasswordAsync` not doing this for us is the trap.
- **`RefreshSignInAsync` on change-password but not on reset** — the two flows have different proof
  of who is acting, and the session handling should follow the proof rather than be uniform.
- **Notifying the old address** — the only mechanism by which the owner learns that somebody with a
  live session repointed their account.
- **Remember-me split out ahead of this plan** — it shares no code with either feature here, and two
  features review better than three. The one coupling it leaves behind is named and asserted rather
  than assumed away.
- **No URL for the Account screen** — matching Settings keeps every route in the `switch` anonymous,
  which is the property that stops an authenticated route being added to it by pattern-match.

---

## Open items — to confirm at plan time

- **Whether `RefreshSignInAsync` preserves the current cookie's `IsPersistent`.** The remember-me
  interaction above depends on it entirely. Verified against the v10 source, and asserted by a test
  that fails loudly if the answer is no — a remembered session that silently becomes a session cookie
  after a password change is invisible until a user complains.
- **Whether `ChangeEmailAsync` returns a distinguishable `DuplicateEmail` error code** in v10, or
  whether the taken case has to be detected before the call instead. The 409-vs-400 split depends on
  telling them apart; if the result does not distinguish them, the pre-check in step 4 of
  `/change-email` moves into the confirm handler as well.
- **Whether `SetUserNameAsync` rotates the security stamp a second time.** Harmless if it does — the
  session is already gone — but it would mean two writes where one was planned, and it changes what
  the concurrency-stamp reasoning in step 4 has to be true for.
- **Whether `GetUserAsync` on a principal whose account was deleted returns null or throws.** Step 2
  of both authenticated handlers is written for null; Plan 7 makes this case reachable in practice.
- **The exact 400/409 body shape** — bare `{ error: "..." }` versus problem details — decided against
  what the existing endpoints already return rather than invented here.
- **Whether the old-address notice should also fire on a *failed* attempt** — an attacker with a
  session who tries and fails is arguably worth telling the owner about. Left out of the design as
  scope, and worth a deliberate yes or no rather than silence.
- **Whether the Account link belongs on the Settings screen only**, or also somewhere a user who has
  never opened Settings would find it. The header stays at two controls either way; this is a
  discoverability question, not a chrome one.
- **Whether `showAccount()` belongs in `main.js` alongside `showSettings()`** or is passed into the
  settings controller as an `onAccount` callback the way `onBack` already is. The latter matches the
  existing wiring; the former is one fewer indirection. Decided against the code, not here.

---

*Draft 2026-08-13. Brainstormed against the signed-off Slice 2a spec, following the design-system
2.0.1 sync. **The load-bearing finding is the `UserName` desync — verified against the `release/10.0`
source, silent through every obvious test, and damaging to a third party rather than to the user who
triggers it.** Two revisions after the first draft, both on review: **remember-me split out into its
own PR ahead of the plan**, and **the Account screen given no URL**, matching Settings.

**Stress-tested 2026-08-13 across security / privacy / accessibility / loopholes — one critical
finding, nine fixes folded in.** The critical one was that `/change-email` requires no password, so a
stolen session becomes a permanent account takeover with no self-service recovery; **Malin's call was
to leave it out of Plan 6**, and it is recorded in [`backlog.md`](backlog.md) as a Plan 8 launch gate
with the full path written out. The nine folded in were the `200 { email }` response the success screen
needs (the spec previously told the reader to render from a body that did not exist), the three
two-form accessibility rules, the validator ordering, the third-party-resource rule restated for a URL
that now carries PII, the POST-on-mount shape, `RefreshSignInAsync`'s failure handling, self-exclusion
in the taken-address lookup, the non-revocation of an older token, and two understated risks.

**Working split settled 2026-08-13, after the design was signed off:** Malin writes the code, Henry
reviews and merges. The design is unchanged by it; what changed is that the review is now the only
outside check on this code, so each of the findings above is paired with the test that catches its
absence, and the set is repeated as a reviewer's checklist in
[`2026-08-13-wend-review-guide.md`](2026-08-13-wend-review-guide.md).

Next: the auth-input fix and remember-me as their own PRs, then Plan 6's implementation plan — written
against the tree as it is once those land, so the *Open items* above can be verified against the .NET 10
source rather than assumed.*
