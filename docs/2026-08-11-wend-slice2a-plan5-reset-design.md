# Wend — Slice 2a Plan 5 design: Forgot & reset password

- **Date:** 2026-08-11
- **Status:** Draft — brainstormed 2026-08-11; **stress-tested the same day across
  security / privacy / accessibility / loopholes, no critical findings, nine fixes folded in**;
  pending review and sign-off by both owners
- **Owners:** Malin & Henry (equal ownership)
- **Repo:** `github.com/wendhq/wend`
- **Parent spec:** [`2026-07-08-wend-slice2a-accounts-design.md`](2026-07-08-wend-slice2a-accounts-design.md) (signed off, stress-tested)
- **Follows:** Plan 4 — login, session & the auth gate ([PR #43](https://github.com/wendhq/wend/pull/43), merged; suite at **231 green**)

---

## Context — what this plan inherits

Plan 3 built registration and email confirmation on headless Identity. Plan 4 added the cookie
scheme, `SignInManager`, lockout, and the frontend auth gate, so a confirmed account can now sign in
and see a board.

What it cannot do is recover. A forgotten password is currently a dead end, and the login screen
admits as much in so many words — *"Password reset arrives in the next release."* This is that
release.

Two earlier decisions were made specifically so this plan would be small, and both pay out here:

1. **Plan 3 wrote `EmailConfirmationTokenProvider` for no immediate benefit of its own.** Its class
   comment says why: without a dedicated provider, the global `DataProtectionTokenProviderOptions`
   governs every Identity token, so shortening the lifespan to the hour a reset wants would silently
   shorten email confirmation to an hour too. Plan 5 adds the mirror-image provider and both
   lifespans stay independent.
2. **Plan 4 bought `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`** at the cost
   of one database read per authenticated request, on the stated grounds that it is what makes "a
   password reset evicts the attacker" true on the *next* request rather than up to N minutes later.
   That promise is this plan's to keep, and to test.

---

## Goals & non-goals

**Goals**

- `POST /api/auth/forgot-password` and `POST /api/auth/reset-password`.
- A password-reset token provider with its own one-hour lifespan.
- `IAuthEmailSender.SendPasswordResetAsync` — the second message type the seam has ever carried.
- Two new screens (`/forgot-password`, `/reset-password`) and the links into them from login.
- A reset that genuinely evicts live sessions and genuinely clears a lockout, rather than leaving
  the user with a working password they still cannot use.

**Non-goals** (each named with the plan that owns it)

- **Change password while signed in** — Plan 6, with change email and remember-me. This plan's
  endpoints are anonymous; a signed-in user who wants a new password uses the same emailed flow.
- **Account deletion** — Plan 7.
- **Rate limiting, antiforgery, HTTPS/HSTS, security headers, secrets posture** — Plan 8.
  `/forgot-password` ships unrate-limited exactly as register, resend and login did. **Plan 8 is a
  launch gate; Plan 9 must not deploy before it.** See *Risks* — this plan makes that gate more
  load-bearing than it was.
- **Preserving unsaved input across a mid-session 401** — still backlogged from Plan 4. A reset now
  produces such a bounce for other sessions, which does not change the deferral, but is worth
  knowing when the backlog item is picked up.

---

## Decisions locked (brainstorm, 2026-08-11)

| Decision | Value | Why |
|---|---|---|
| **After a successful reset** | The API returns 204 and does **not** sign the user in; the screen mounts login with "Your password has been changed — please sign in." | A reset link arrives by email. Auto-signing-in makes that emailed token a login credential, so a forwarded, cached or screenshotted link becomes one click from a live session. Typing the new password once more also confirms the user remembers what they just set. |
| **Unconfirmed account asks for a reset** | Send a fresh **confirmation** link instead. No reset token is ever issued for an unconfirmed account. | `ResetPasswordAsync` does not confirm an email, and `RequireConfirmedAccount` still blocks login — so a plain reset would succeed and leave the user at the same wall, with no explanation the response is allowed to give. The confirmation link is the mail that actually unblocks them. It also keeps reset from becoming a second, quieter path around the verification gate. |
| **Lockout after a reset** | Cleared — both `LockoutEnd` and `AccessFailedCount`. | Lockout defends a password that no longer exists. The person who just proved mailbox control is the owner; leaving them locked out means an attacker's five failed guesses keep the real user out for fifteen minutes *after* they have fixed the problem. |
| **Expired or used link** | Discovered on submit. No token pre-check endpoint. | A pre-check is a new anonymous endpoint whose entire job is answering "is this token good?" — a free validity oracle and one more surface for Plan 8 to rate-limit. The cost is that a user types a password before learning the link is stale, and the accessible expired state below is what pays it. |
| **Reset token lifespan** | **1 hour**, in its own provider | The parent spec's figure. Short because a reset link is the single most powerful string the system emails. Email confirmation stays at 24 hours; keeping them independent is the whole reason Plan 3's provider exists. |
| **Single-use** | Falls out of the security stamp; no separate guard | Identity's data-protector tokens are stamp-bound, and a completed reset rotates the stamp — so every outstanding reset token for that user dies with it. Plan 3 needed an explicit `EmailConfirmed` guard because confirmation does *not* rotate anything. Different mechanism, same user-visible promise; it gets a test rather than a comment. |
| **`/forgot-password` response** | `204` for every input, malformed included | The parent spec's enumeration requirement. Mirrors `resend-verification`, which deliberately has no 400 branch either: telling a caller their address is malformed is harmless, but a second response shape on an endpoint whose job is looking identical from outside is a liability nobody needs. |
| **`/reset-password` response** | `204`, or `400` carrying one of two error codes: the password failed policy, or the link is bad | Both are 400 and neither mentions an account, so this is not an enumeration surface. It exists because one 400 for both cases produces a screen that says "this link has expired" when the link is fine and the password was eleven characters. |
| **Validation order** | Password policy is checked **before** the user lookup and token redemption | The house rule from Plan 3's stress test, applied here for the error-message reason above rather than the enumeration one. Same ordering, different payoff. |
| **New-password field** | One field, no confirm-password | Register ships without one and this screen mirrors it. A typo here costs a second reset, not an account; a confirm field costs every user an extra field forever. |

---

## Token provider

`PasswordResetTokenProvider<TUser>` and `PasswordResetTokenProviderOptions`, a direct mirror of
`EmailConfirmationTokenProvider`:

```csharp
public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        Name = "WendPasswordResetTokenProvider";
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
```

registered alongside the confirmation provider and wired through
`options.Tokens.PasswordResetTokenProvider`.

The pairing is the point: a test asserts **both** lifespans, so a future change to either one cannot
quietly drag the other with it. That is the failure Plan 3 wrote the first provider to prevent, and
it is invisible until somebody's confirmation link dies in an hour.

**Single-use is narrower than it sounds, and the difference is worth stating.** Only a *completed*
reset rotates the stamp. Requesting a second link does not revoke the first: a user who clicks "send
me a link" three times has three live tokens, every one of them good for its full hour. Someone who
asks for a fresh link *because they think the old one was seen* has therefore revoked nothing — the
only revocation is finishing a reset. A test asserts an older token still works after a newer one is
issued, so the property is documented rather than discovered by whoever first wonders.

---

## Email seam

`IAuthEmailSender` gains a second method:

```csharp
Task SendPasswordResetAsync(string email, string link);
```

`FileAuthEmailSender` implements it the same way it implements confirmation, and the test fake in
`WendApiFactory` grows the same method — recording message *type* as well as recipient, because
several of the tests below turn on *which* mail went out, not just that one did.

Link construction reuses Plan 3's shape exactly: Base64Url-encoded token, origin from
`Wend:PublicBaseUrl` rather than the request host — so a Host-header injection cannot get Wend to
email a victim a genuine-looking link pointing at the attacker's server — falling back to the
request host in Development only.

---

## `POST /api/auth/forgot-password`

Request `{ email }`. Response **always `204`**. Anonymous.

The handler, in order:

1. Trim the address. If it is empty or over 254 characters, return 204 having done nothing.
2. `FindByEmailAsync`. No user → return 204 having done nothing.
3. **Unconfirmed user** → send a fresh confirmation link, reusing Plan 3's `SendConfirmationAsync`.
   No reset token is generated.
4. **Confirmed user** → `GeneratePasswordResetTokenAsync`, build the link to
   `/reset-password?userId=…&code=…`, send it through `SendPasswordResetAsync`.

Locked-out and confirmed is an ordinary case: the link goes out. Reset is precisely how a locked-out
owner gets back in, and refusing here would only punish the victim of somebody else's guessing.

---

## `POST /api/auth/reset-password`

Request `{ userId, code, password }`. Response `204`, or `400` with an error code. Anonymous.

The handler, in order:

1. **Password policy first.** Run `users.PasswordValidators` against the supplied password. Failure
   → `400 { error: "password" }`, before any lookup or token work.
2. `FindByIdAsync`. No user → `400 { error: "token" }`.
3. Base64Url-decode the code; a `FormatException` → `400 { error: "token" }` (Plan 3's `/verify`
   already has this shape).
4. `ResetPasswordAsync(user, token, password)`. Failure → `400 { error: "token" }`.
5. Success → clear the lockout: `SetLockoutEndDateAsync(user, null)` **and**
   `ResetAccessFailedCountAsync(user)`.
6. `204`.

**Step 5's two calls are not redundant.** `LockoutEnd` and `AccessFailedCount` are separate columns;
resetting the count leaves a live `LockoutEnd` in place, and the user gets a working password they
still cannot use for fifteen minutes. Both, or the decision above is not implemented.

**Steps 4 and 5 are three separate writes, not one transaction, and the order is deliberate.** The
password goes first because it is the security-critical write and the only one the user's next login
depends on. All three run against the **same `user` instance**, whose concurrency stamp
`ResetPasswordAsync` has just refreshed — reloading the user in between would work with a stale stamp
and fail. If either lockout call fails, the handler still answers **204**, because the password
genuinely did change and telling the user otherwise would send them round the loop for nothing; the
failure is logged by error code, and the user waits out a lockout window that should have been
cleared. That is the worst outcome available and it is the right one.

**Step 4 is what evicts everyone.** `ResetPasswordAsync` updates the password hash, which rotates the
security stamp. With Plan 4's `ValidationInterval = TimeSpan.Zero`, every live cookie for that user
fails its next request — the attacker with a stolen session included, **and the browser that
performed the reset**, which is why this flow hands off to the login screen rather than anywhere
else. There is no carve-out for the current session and there must not be one. Every outstanding
reset token dies with the stamp it was bound to.

**A failed reset must not be a free lockout bypass.** Steps 1–4 never touch `AccessFailedCount`, and
the token is unguessable, so a wrong code is not a password guess and should not be counted as one.
Step 5 runs only after a *successful* reset, which by definition required the emailed token.

---

## Security posture

- **Enumeration.** `/forgot-password` is one response for every input. Nothing in the body, status
  or headers distinguishes unknown, unconfirmed, confirmed or locked out.
- **Timing.** The unknown-address branch does no token generation and no send, so it returns
  measurably faster than a confirmed one. This is the same side channel register already has, and it
  joins register's existing Plan 8 backlog item rather than getting a bespoke dummy-work path here.
  The parent spec asks for constant time on **login** by name, and Plan 4 closed that one; equalising
  the whole `/api/auth/*` surface belongs with the rate limiting that makes repeated measurement
  expensive in the first place.
- **Outbound mail.** `/forgot-password` becomes the cheapest mail trigger in the application — one
  anonymous request, and the only thing the caller needs to know is somebody's address. Login's
  unconfirmed nudge at least required knowing the password. See *Risks*.
- **Token handling.** One hour, stamp-bound, never logged. The reset screen strips `userId` and
  `code` from the address bar with `history.replaceState` as soon as it reads them, exactly as
  `/verify` does, so a live token does not sit in a history entry or a screenshot. The landing page
  loads zero third-party resources, so no `Referer` can carry it off-site.
- **Logging.** Error codes only, never addresses or tokens — Plan 3's precedent, unchanged.
  `/reset-password` query strings join `/verify` in the "kept out of access logs" launch gate that is
  already backlogged for Plan 9.
- **CSRF.** Both endpoints bind JSON only, which is the same reasoning that lets antiforgery wait for
  Plan 8: an HTML form cannot send `application/json`, and a cross-site `fetch` that does triggers a
  preflight there is no CORS policy to satisfy. Plan 4 made that reasoning testable rather than
  assumed; both new endpoints get the same form-encoded-POST rejection test.

---

## Frontend

Two new MVC trios under `js/auth/`, matching register, verify and login. A combined "recover" module
with two states was considered and rejected: one trio per screen is the pattern through all of
Slice 1 and both Plan 3 screens, and these two screens share nothing but a heading style.

**`/forgot-password`** — `js/auth/forgot/{model,view,controller}.js`

- One email field, `autocomplete="email"`, `maxlength="254"`.
- `.btn` plus a variant on every control; a bare `<button>` is 28px and fails the 44×44 minimum.
  Caught twice in Plan 3 and worth repeating for every new screen.
- Submit disabled while a request is in flight.
- **One success state for every outcome:** "If that address has an account, we've sent a link — check
  your inbox." Focus moves to it, and it is announced. The screen cannot say more than the API does.
- **The success message renders above a form that stays usable**, with submit re-enabled. Verify's
  `sent` state replaces its whole screen, and copying that here would strand anyone who mistyped
  their address: the mail never arrives, and the control they would retype into has left the tab
  order entirely. Since the response is identical for every address, a typo is invisible until the
  inbox stays empty — so retrying has to cost nothing but typing.
- A link back to `/login`.

**`/reset-password`** — `js/auth/reset/{model,view,controller}.js`

- Reads `userId` and `code` from the query string, then `history.replaceState(null, "",
  "/reset-password")` immediately.
- **`userId` and `code` live in the controller closure and the model only.** They are never passed
  to the view, never rendered, and never carried in a hidden input — the controller merges them with
  the submitted password on the way to the API. This is the one screen in the app that both reads
  the URL and owns a form, so the obvious way to get all three values into one `FormData` is a
  hidden field, and every view here renders through a template literal into `innerHTML`:
  `value="${code}"` on an anonymous page anyone can link to is reflected XSS. Verify keeps these
  values out of the DOM too, but only because it has no form to put them in — that is an accident of
  its shape, not a rule, so this is the rule. The screen's manual check confirms neither string
  appears anywhere in the DOM after mount.
- **No link, no request.** A reload, a bookmark or a back-navigation arrives here with no `userId`
  and no `code`, because `replaceState` has already stripped them — and posting two empty strings
  would burn a request to be told the link "expired or was already used", which is untrue and sends
  the user to replace a link sitting perfectly fine in their inbox. Mirroring verify's `noLink()`,
  this state is settled **before** the model subscription so it renders and announces once: "Nothing
  to reset — open the link from your email, or request a new one", its own heading, focus on it, and
  a link to `/forgot-password`.
- One password field, `autocomplete="new-password"`, `minlength="12"`, carrying register's hint text
  ("At least 12 characters. A memorable phrase beats a short tangle of symbols.") and wired to it
  with `aria-describedby`, as register does.
- Plan 4's two focus rules, unchanged: a client-side validation error focuses the offending field; a
  server-side error belongs to no field, so focus goes to the announced summary.
- **Submit is disabled in flight, and the controller ignores any submit once a 204 has landed.** The
  disable alone loses the race when both clicks land before the first response — and the second
  request carries a token the first one has just killed by rotating the stamp, so the screen would
  announce "this link has expired" to somebody whose password had just been changed successfully.
  They would then request another link and reset a password that was already correct.
- `400 { error: "password" }` → the summary states the policy. The link is still valid; the user
  tries again in place.
- `400 { error: "token" }` → the screen swaps to the accessible **"This link has expired or was
  already used"** state: message, focus moved to it, announced, and a link to `/forgot-password` to
  request a new one. Never a raw error.
- `204` → mount login with "Your password has been changed — please sign in.", focus on the heading.

**Login screen changes**

- A permanent "Forgotten your password?" link in `.auth-links`, next to the existing register link.
  Permanent, not only inside the three-failure help block: someone who knows they have forgotten
  their password should not have to fail three times to be offered the way out.
- The help block's third line — currently "Forgotten the password? Password reset arrives in the next
  release." — becomes a real link to `/forgot-password`.

**Routing.** `main.js` gains `case "/forgot-password"` and `case "/reset-password"`, alongside
`/register`, `/verify` and `/login`, all of them ahead of the auth gate because an emailed link has
to land somewhere regardless of session state. Both screens call `hideAppChrome()` on mount, like
every other auth screen.

---

## Testing

The two-tier engine is unchanged: repository unit tests on in-memory SQLite, API integration tests
against a per-test throwaway PostgreSQL database.

**New coverage**

- **`/forgot-password` is one response** — unknown, malformed, unconfirmed, confirmed and locked-out
  addresses all return the same 204 with the same body.
- **The right mail, exactly once** — a confirmed address produces one *reset* mail; an unconfirmed
  address produces one *confirmation* mail and **no reset mail**; unknown and malformed produce none.
  This is the test that proves the unconfirmed decision was implemented rather than described.
- **A reset link is never issued for an unconfirmed account** — asserted on the sender's recorded
  message type, not inferred from a count.
- **The real-cookie walk** — register → verify → login → forgot → reset → the old password is 401 and
  the new one is 204, on a factory with the test scheme disabled.
- **Live sessions die** — a signed-in client's next request is 401 after that user's password is
  reset. This is the assertion Plan 4's `TimeSpan.Zero` was bought for and the first time anything
  has checked it.
- **A reused reset token is 400** — the single-use property, which here is a consequence of stamp
  rotation rather than an explicit guard, so it needs the test more than usual.
- **An older token still works after a newer one is issued** — the counterpart to the test above, and
  the one that pins down what "single-use" does *not* mean here. If this ever starts failing, the
  revocation model has changed and the docs are wrong.
- **A tampered or foreign token is 400**, and a token minted for user A cannot reset user B.
- **Lockout is cleared** — five failures lock the account, a reset completes, and the next login
  succeeds immediately rather than after fifteen minutes.
- **A weak password is 400 with the password code, and the token still works afterwards** — the guard
  on the validation ordering.
- **Both lifespans** — reset is 1 hour and email confirmation is still 24. One test, two assertions,
  because the failure mode is one silently dragging the other.
- **Both new endpoints refuse a form-encoded POST**, extending Plan 4's login-CSRF guard to the two
  new anonymous state-changing routes.

Frontend tasks have no automated tests — this repo has no JS test harness, as through all of Slice 1
— so they carry scripted manual checks: 44×44 on every control, focus placement on each of the three
outcomes, the announcements, and a keyboard walk. Every browser check hard-reloads, because
`UseStaticFiles` sends no `Cache-Control`. Four of these checks are specific to this plan:

- **The `minlength="12"` check uses real key events**, never a scripted `.value` assignment.
  `tooShort` only fires on a *user-edited* value, so setting the value from script leaves the field
  non-dirty, native validation passes, the request goes out, and the walk records a false pass on the
  one constraint the one field has. Focus the field, then type.
- **Neither `userId` nor `code` appears in the DOM** after the reset screen mounts.
- **A double-submitted reset** does not surface the expired state.
- **Reloading `/reset-password`** renders the no-link state and sends no request.

---

## Task breakdown (detail is the writing-plans step)

1. **Token provider and the email seam** — `PasswordResetTokenProvider`, the 1-hour lifespan wired
   through `options.Tokens`, `SendPasswordResetAsync` on `IAuthEmailSender`, the file sender, and the
   test fake's message-type recording. Includes the both-lifespans test.
2. **`POST /api/auth/forgot-password`** — the four branches behind one 204.
3. **`POST /api/auth/reset-password`** — validation ordering, the two 400 codes, stamp rotation, and
   the two-call lockout clear.
4. **The real-cookie suite** — old password dead, new password works, live session evicted, token
   reuse refused, lockout cleared.
5. **The forgot screen** — MVC trio, route, the single success state, and the two login-screen links.
6. **The reset screen** — MVC trio, route, `replaceState`, the expired state, and the handoff to
   login.
7. **Backlog and docs** — `/forgot-password` added to the Plan 8 rate-limiting gate, `/reset-password`
   query strings added to the log-exclusion gate, and the timing note folded into register's existing
   item.

Roughly 25 to 30 new tests on the **231** baseline; exact per-task totals are pinned when the plan is
written, because a paste-driven edit once silently dropped three tests behind a green suite.

---

## Risks

- **`/forgot-password` is an unrate-limited email bomb aimed at anyone whose address you know.** One
  anonymous request, no password required, and the mail goes to a third party rather than the caller.
  It is strictly cheaper to abuse than register (which at least needs a novel address) or login's
  nudge (which needs the password). Plan 8's rate limiting was already a launch gate; this plan is
  the reason it is the most important one. Recorded in the backlog with that framing, not left as a
  line in a risks section.
- **The lockout clear is a deliberate weakening of lockout**, and the reasoning has to survive review:
  anyone who can read the user's email can already reset the password outright, so clearing lockout
  grants an attacker with mailbox access nothing they did not already have — while denying the
  legitimate owner the fifteen-minute wait an attacker imposed on them.
- **The unconfirmed branch sends confirmation mail in response to a reset request**, which means a
  caller who knows an unconfirmed address can trigger confirmation mail from *two* endpoints now
  (resend and forgot). Same Plan 8 answer; noted so it is not discovered as a surprise.
- **A one-hour token will generate support-shaped confusion** — "the link doesn't work" from someone
  who opened it the next morning. The accessible expired state with its request-a-new-one link is the
  entire mitigation, which is why it is a first-class screen rather than an error toast.
- **The stamp-rotation eviction is invisible until it breaks.** Nothing in the reset code says "this
  is what logs everyone out"; it is a consequence of a password hash update two layers down,
  activated by a setting in `Program.cs` from another plan. The test is the documentation.

---

## Key decisions (and why)

- **No auto sign-in after reset** — the moment a reset completes, the emailed token has done its job;
  turning it into a session extends the life of the most powerful string the system sends by email.
- **Confirmation link instead of a reset link for unconfirmed accounts** — the alternative is a flow
  that succeeds end to end and still leaves the user unable to log in, with the enumeration rules
  forbidding any explanation. Sending the mail that actually unblocks them costs one branch.
- **Clearing the lockout** — a lockout protecting a password that has just been replaced is not
  protecting anything, and the person it locks out is the owner.
- **No token pre-check endpoint** — a validity oracle for one screen's worth of politeness, when an
  accessible expired state delivers the same politeness with no new surface.
- **Password validated before the token is redeemed** — the same ordering Plan 3's stress test forced
  for enumeration reasons, here earning its place by letting the screen tell the truth about which of
  the two things went wrong.
- **Single-use via the security stamp rather than a guard** — the mechanism already exists and is
  exactly right; the work is in testing it, not in building it.
- **Two trios, not one recover module** — the house pattern, and these screens have nothing in common
  but a heading.

---

## Open items — to confirm at plan time

- **That `ResetPasswordAsync` rotates the security stamp in Identity 10.** The whole eviction promise
  rests on it. Verified against the v10 source when the plan is written, not assumed from the shape
  of `UpdatePasswordHash` — and the test is written so it fails loudly if the assumption is wrong.
- **Whether `SetLockoutEndDateAsync(user, null)` is accepted for a user whose lockout has already
  expired**, and whether either call rotates the stamp a second time (harmless, but it would mean two
  writes where one was planned).
- **Whether the two lockout calls survive on the same `user` instance** after `ResetPasswordAsync`
  has updated it, or whether `UserManager` returns a concurrency failure that the 204-anyway rule
  above would then swallow silently. If it does, the handler needs to log that specific code loudly
  enough to notice, because the user-visible symptom is a fifteen-minute wait nobody can explain.
- **The exact 400 body shape** — a bare `{ error: "..." }` versus a problem-details payload — decided
  against what the existing endpoints already return rather than invented here.
- **Whether the reset mail is dispatched inline or via `response.OnCompleted`** like login's nudge.
  This endpoint has no constant-time commitment, so inline is probably right; login's shape exists
  for a reason that does not apply here, and copying it without that reason would be cargo cult.
- **Whether `WendApiFactory`'s fake sender records enough today** to assert message *type*, or whether
  it needs a small shape change — task 1 either way, but it decides whether task 1 touches the
  factory.
- **Where the "Forgotten your password?" link sits** relative to the register link in `.auth-links`,
  decided against the design system rather than invented, and confirmed at 44×44.
- **Which `WendUser` instance the password validators are handed in step 1**, given the real user has
  not been looked up yet. Register builds a `candidate` for the same reason and a throwaway works
  here too — Wend registers no custom validator that reads the user — so this is a shape question,
  not a threat to the ordering. Worth writing down because a future custom validator that *does* read
  the user would turn it into one.

---

*Draft 2026-08-11. Brainstormed against the signed-off Slice 2a spec, following Plan 4's merge.
**Stress-tested the same day — no critical findings; nine fixes folded in, the load-bearing ones
being the rule that keeps the emailed `code` out of the DOM, the double-submit that would have
announced "this link has expired" to someone whose reset had just succeeded, and the reload state
that verify solved a plan ago and this spec had forgotten.** Next: review by both owners, then the
implementation plan.*
