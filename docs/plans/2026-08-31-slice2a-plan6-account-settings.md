# Slice 2a Plan 6 — Account settings: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** A signed-in user can change their password and change the address they sign in with,
the second confirmed from a link sent to the new address — without ever leaving an abandoned
address squatted behind them.

**Architecture:** Three minimal-API handlers added to the existing `AuthEndpoints` group
(`/change-password` and `/change-email` authenticated, `/confirm-email-change` anonymous), a
change-email token provider with its own one-hour lifespan, two more methods on the
`IAuthEmailSender` seam, and two hand-authored vanilla-JS MVC screens — an Account screen with no
URL, mounted from Settings, and a `/confirm-email-change` screen on its own anonymous route.
Nothing is restructured; every piece mirrors a shape Plan 3, 4 or 5 already established.

**Tech stack:** .NET 10, ASP.NET Core minimal APIs, ASP.NET Core Identity (`AddIdentityCore` +
cookie auth), EF Core 10 on PostgreSQL 17, NUnit 4 with `WebApplicationFactory`, vanilla ES modules
with no build step.

**Design spec:**
[`docs/2026-08-13-wend-slice2a-plan6-account-settings-design.md`](../2026-08-13-wend-slice2a-plan6-account-settings-design.md)
— signed off, stress-tested, one critical finding consciously deferred to Plan 8, nine fixes folded
in. **Read it before Task 1.** The reviewer's checklist that pairs with it is
[`docs/2026-08-13-wend-review-guide.md`](../2026-08-13-wend-review-guide.md) § *Plan 6 review
checklist*.

## Global Constraints

- **Start PostgreSQL before anything:** the service is Manual start, so connection refused and EF
  timeouts mean it is stopped, not a code bug. **Since a PostgreSQL update reset the service
  descriptor, starting it needs elevation** — run
  `Start-Process pwsh -Verb RunAs -Wait -ArgumentList '-NoProfile','-Command','Start-Service postgresql-x64-17'`
  and approve the UAC prompt.
- **Kill the app before any build or test run:** the process is `Wend.Api`, **not** `Wend`. A
  running instance holds the DLL and produces MSB3021/3027, which is a copy lock, not a test failure.
- **Test baseline is 257 green** (measured on this tree, 2026-08-31). Check the running total
  against each task's stated total. A paste-driven edit once silently dropped three tests behind a
  green suite.
- **No AI attribution** in commit messages or the PR body — no `Co-Authored-By`, no "Generated
  with" trailer.
- **`design-system/` is read-only.** No task here needs a change in it; every class used already
  exists (`.btn`, `.btn-primary`, `.btn-ghost`, `.input`, `.alert`, `.alert-danger`,
  `.alert-success`).
- **`escapeHtml` at every user-content interpolation**, `#status` stays outside `#app`, and focus
  never drops to `<body>`.
- **Password policy is 12 characters, no composition rules** (`Program.cs:47-51`). Test passwords
  use `"correct horse battery staple"`, as every existing auth test does.
- **Emailed links are built from `Wend:PublicBaseUrl`**, never from `http.Host`.
- **Logging carries error codes only** — never an address, never a token, never a password.
- **Every browser check hard-reloads** (Ctrl+Shift+R or `cache: 'reload'`): `UseStaticFiles` sends
  no `Cache-Control`, so ES-module imports are served stale otherwise.
- **Squash-merge**, single-author branch. Henry reviews and merges.

---

## Open items — resolved at plan time

The design closed with eight items to verify "against the .NET 10 source rather than assumed".
All eight were checked against `dotnet/aspnetcore` `release/10.0` on 2026-08-31 before this plan was
written. Three of the answers changed what the code below does.

| Open item | Answer | Where it lands |
|---|---|---|
| Does `RefreshSignInAsync` preserve the cookie's `IsPersistent`? | **Yes.** `RefreshSignInCoreAsync` re-reads the ticket with `Context.AuthenticateAsync` and hands `auth.Properties` to `SignInWithClaimsAsync`, so `IsPersistent`, `IssuedUtc` and `ExpiresUtc` all carry forward. `CookieAuthenticationHandler` writes `Expires` only `if (Properties.IsPersistent)`. **Caveat:** the reissued cookie inherits the *original* absolute expiry rather than a fresh `ExpireTimeSpan`; sliding renewal still extends it later. | Task 5, asserted rather than trusted. |
| Does `ChangeEmailAsync` return a distinguishable `DuplicateEmail`? | **Yes.** `ChangeEmailCoreAsync` returns `ErrorDescriber.InvalidToken()` (code `"InvalidToken"`) on a token failure; otherwise it reaches `UpdateUserAsync` → `ValidateUserAsync` → `UserValidator`, whose duplicate branches use `Code = nameof(DuplicateUserName)` and `Code = nameof(DuplicateEmail)`. The 409-vs-400 split works off the code. No pre-check has to move into the confirm handler. | Task 4, step 3. |
| Does `SetUserNameAsync` rotate the security stamp a second time? | **Yes** — `Store.SetUserNameAsync` → `UpdateSecurityStampInternal` → `UpdateUserAndRecordMetricAsync`. Harmless (every session is already gone from the first rotation), but it means two writes and two rotations, and it is why the second call must use the **same** `user` instance. | Task 4, step 4, and the comment there. |
| Does `GetUserAsync` throw for a deleted account? | **No.** It returns `null` when `GetUserId` is null or `FindByIdAsync` misses. Step 2 of both authenticated handlers is written for null and gets a test. | Tasks 2 and 3. |
| The exact 400/409 body shape | `Results.BadRequest(new { error = "..." })` / `Results.Conflict(new { error = "..." })`, matching `/reset-password` exactly. Not problem details. | Tasks 2–4. |
| Should the old-address notice also fire on a *failed* attempt? | **No.** Deliberate: a failed attempt is unauthenticated noise an attacker can generate at will, so a notice on failure is an email-bomb vector aimed at the victim, using the victim's own account. Recorded here so the silence is a decision. | Task 4, step 5. |
| Is the Account link on Settings only? | **Yes, Settings only** for this plan. The header stays at two controls. Discoverability is a question for the dashboard work, not for a plan that has no other surface to put it on. | Task 6. |
| `showAccount()` in `main.js` or an `onAccount` callback? | **`onAccount` callback**, decided against the code: `showSettings()` already passes `{ onBack }` into `createSettingsController`, so a second callback matches the wiring that exists rather than adding a second style beside it. | Task 6. |

**Two further findings from the same pass, neither of which the design anticipated:**

- **`RefreshSignInAsync` returns `Task`, not `Task<IdentityResult>`.** The design's step 5 says "if
  it fails, still 204" — but there is no result to inspect, so the only failure it can report is an
  exception. Task 2 wraps it in `try`/`catch`, which is what "if it fails" has to mean.
- **Under the `Test` auth scheme, `RefreshSignInAsync` is a silent no-op.** It calls
  `Context.AuthenticateAsync(IdentityConstants.ApplicationScheme)`, finds no cookie, logs
  *"RefreshSignInAsync prevented because the user is not currently authenticated"* and returns
  without signing anyone in. It does not throw. That is not a bug — but it means **no test-scheme
  test can prove the acting session survives**, and neither can a test-scheme test prove another
  session dies, because `SecurityStampValidator` hangs off the cookie and never runs there. Both
  assertions live in Task 5's real-cookie suite, which is why Task 5 exists at all.

---

## File structure

**Created**

| File | Responsibility |
|---|---|
| `Wend.Api/ChangeEmailTokenProvider.cs` | A `DataProtectorTokenProvider` subclass plus its options, so change-email tokens carry a one-hour lifespan independent of every other Identity token. Third instance of the pattern. |
| `Wend.Tests/AuthChangePasswordTests.cs` | `/api/auth/change-password` — the two 400 codes, the validator ordering, and the lockout accounting `ChangePasswordAsync` does not do. |
| `Wend.Tests/AuthChangeEmailTests.cs` | `/api/auth/change-email` — the four branches behind one 204, the `FindByNameAsync` check with self excluded, token non-revocation. |
| `Wend.Tests/AuthConfirmEmailChangeTests.cs` | `/api/auth/confirm-email-change` — the token/taken split, the `200 { email }` body, the old-address notice, and **the `UserName` regression**. |
| `Wend.Tests/RealCookieAccountTests.cs` | The browser-shaped walk on the genuine cookie scheme: acting session survives, others die, persistence survives. Opens with the test-scheme canary. |
| `Wend.Api/wwwroot/js/auth/account/{model,view,controller}.js` | The Account screen — current address plus two independent forms. |
| `Wend.Api/wwwroot/js/auth/confirm-email/{model,view,controller}.js` | The `/confirm-email-change` screen — POSTs on mount, four states. |

**Modified**

| File | Change |
|---|---|
| `Wend.Core/IAuthEmailSender.cs` | `SendEmailChangeConfirmationAsync` and `SendEmailChangedNoticeAsync` on the seam. |
| `Wend.Api/FileAuthEmailSender.cs` | Implements both. |
| `Wend.Tests/FakeAuthEmailSender.cs` | Implements both, recording kinds `"change-email"` and `"changed-notice"`. |
| `Wend.Api/Program.cs:68-83` | Registers the change-email provider and points `options.Tokens.ChangeEmailTokenProvider` at it. |
| `Wend.Api/AuthEndpoints.cs` | Three handlers, one link-builder, three request records. |
| `Wend.Tests/AuthConfigurationTests.cs:83-102` | The lifespans test grows a third provider rather than gaining a third test. |
| `Wend.Api/wwwroot/js/main.js` | Six imports, two mount functions, one new route, one `onAccount` callback. |
| `Wend.Api/wwwroot/js/settings/view.js` | An Account button and its `data-action`. |
| `Wend.Api/wwwroot/js/settings/controller.js` | Forwards it to `onAccount`. |
| `Wend.Api/wwwroot/css/app.css` | An `.account-*` block; the forms reuse `.auth-form`. |

**Test totals:** 257 baseline → 257 (T1, no new tests by design) → 266 (T2) → 276 (T3) → 287 (T4)
→ **291** (T5). Tasks 6–8 add no automated tests, because this repo has no JS test harness; they
carry scripted manual checks instead.

---

## Deviations to record in the PR body

Four inherited from the design, five introduced here. Task 8 writes them up; they are listed now so
no one has to reconstruct them at the end.

1. **Two endpoints for change-email**, where the parent spec's table shows one `POST /change-email`
   row. A token that has to reach the new address and come back cannot be one round trip.
2. **Lockout accounting on `/change-password`**, beyond the parent spec's one-line description.
   `ChangePasswordAsync` does none, so without it a stolen session gets unlimited guesses.
3. **`SetUserNameAsync` alongside `ChangeEmailAsync`** — the plan's correctness requirement, not
   tidiness.
4. **`/confirm-email-change` returns `200 { email }`** — the only endpoint in `/api/auth/*` with a
   body, so the success screen can name the address from the database rather than from the URL.
5. **Task 5 is a real-cookie suite the design's seven-task list does not have.** Added because two
   of the design's own required assertions cannot run under the `Test` scheme at all (see *Open
   items*). Same shape as Plan 5's Task 4.
6. **The change-email success message does not say the link went to the address that was typed.**
   The endpoint answers 204 for both "free" and "held by someone else", so a screen claiming a link
   was sent would leak exactly what the 204 protects. Wording is *"If that address is free, we've
   sent it a confirmation link"* — the mirror of the forgot screen's.
7. **The Account view renders its two forms into two independently-replaced sections** rather than
   through one whole-screen `innerHTML` rebuild. It makes the design's two-form rules structural
   instead of carefully maintained: one form's repaint cannot touch the other's error region,
   focus, or typed input.
8. **`RefreshSignInAsync` is wrapped in `try`/`catch`** rather than checked for a failed result,
   because it returns no result.
9. **A 401 on either Account form is shown in that form's error region rather than bouncing to the
   sign-in screen.** The design does not cover the screen's 401 branch. `/change-password` answers
   401 for a locked-out account as well as a dead session, and a locked-out user's cookie still
   works — so signing them out would be a lie that also loses whatever they had typed. One message
   covers both without naming which.

---

### Task 1: Change-email token provider and the email seam

**Files:**
- Create: `Wend.Api/ChangeEmailTokenProvider.cs`
- Modify: `Wend.Core/IAuthEmailSender.cs`
- Modify: `Wend.Api/FileAuthEmailSender.cs`
- Modify: `Wend.Tests/FakeAuthEmailSender.cs`
- Modify: `Wend.Api/Program.cs:68-83`
- Test: `Wend.Tests/AuthConfigurationTests.cs:83-102`

**Interfaces:**
- Consumes: `PasswordResetTokenProvider<TUser>` and `PasswordResetTokenProviderOptions` as the shape
  to mirror.
- Produces: `ChangeEmailTokenProvider<WendUser>`; `ChangeEmailTokenProviderOptions` (name
  `"WendChangeEmailTokenProvider"`, lifespan 1 hour); the provider-map key `"WendChangeEmail"`;
  `IAuthEmailSender.SendEmailChangeConfirmationAsync(string newEmail, string link)` and
  `IAuthEmailSender.SendEmailChangedNoticeAsync(string oldEmail, string newEmail)`;
  `FakeAuthEmailSender` kinds `"change-email"` and `"changed-notice"`.

**No new tests.** The existing lifespans test grows a third provider instead of gaining a third
test: the failure being guarded is one provider's lifespan silently dragging another's, and that is
one property across three values, not three properties. Total stays **257**.

- [ ] **Step 1: Write the failing test**

Replace `Reset_tokens_last_an_hour_and_confirmation_tokens_still_last_a_day` in
`Wend.Tests/AuthConfigurationTests.cs` with:

```csharp
    [Test]
    public void Change_email_and_reset_tokens_last_an_hour_and_confirmation_still_lasts_a_day()
    {
        var identity = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var confirmation = _factory.Services
            .GetRequiredService<IOptions<EmailConfirmationTokenProviderOptions>>().Value;
        var reset = _factory.Services
            .GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value;
        var changeEmail = _factory.Services
            .GetRequiredService<IOptions<ChangeEmailTokenProviderOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(changeEmail.TokenLifespan, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(reset.TokenLifespan, Is.EqualTo(TimeSpan.FromHours(1)));
            // All three, in one test, on purpose: the failure this grouping exists to prevent is a
            // change to any one lifespan silently dragging the others with it.
            Assert.That(confirmation.TokenLifespan, Is.EqualTo(TimeSpan.FromHours(24)));
            Assert.That(identity.Tokens.ChangeEmailTokenProvider, Is.EqualTo("WendChangeEmail"));
            Assert.That(identity.Tokens.PasswordResetTokenProvider, Is.EqualTo("WendPasswordReset"));
            Assert.That(identity.Tokens.EmailConfirmationTokenProvider,
                Is.EqualTo("WendEmailConfirmation"));
        });
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AuthConfigurationTests"`

Expected: a **compile** error — `ChangeEmailTokenProviderOptions` does not exist. That is the
failing state for this step; nothing else in the suite runs until Step 3 lands.

- [ ] **Step 3: Create the provider**

`Wend.Api/ChangeEmailTokenProvider.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wend.Api;

/// <summary>
/// Change-email tokens with their own lifespan. The third instance of the pattern
/// EmailConfirmationTokenProvider and PasswordResetTokenProvider exist to enforce: without one
/// provider per token type, the global DataProtectionTokenProviderOptions governs all of them, and
/// the hour this one wants would silently become the lifespan of every confirmation link.
/// </summary>
public class ChangeEmailTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<ChangeEmailTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;

public class ChangeEmailTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public ChangeEmailTokenProviderOptions()
    {
        Name = "WendChangeEmailTokenProvider";
        // One hour, matching reset rather than confirmation's 24: the user typed the address
        // seconds ago and has one mailbox to reach. The link also repoints an account's login
        // identity, which is worth as much to an attacker as a reset link.
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
```

- [ ] **Step 4: Register it**

In `Wend.Api/Program.cs`, immediately after the `WendPasswordReset` lines inside
`AddIdentityCore`'s options lambda (currently lines 74-76):

```csharp
        // Third provider, same reason as the second. Identity's default ChangeEmailTokenProvider
        // is the shared "Default" one, so without this line a lifespan set anywhere would govern
        // all of them.
        options.Tokens.ProviderMap.Add("WendChangeEmail",
            new TokenProviderDescriptor(typeof(ChangeEmailTokenProvider<WendUser>)));
        options.Tokens.ChangeEmailTokenProvider = "WendChangeEmail";
```

And beside the other two transients (currently line 83):

```csharp
builder.Services.AddTransient<ChangeEmailTokenProvider<WendUser>>();
```

- [ ] **Step 5: Extend the email seam**

`Wend.Core/IAuthEmailSender.cs` — add to the interface:

```csharp
    Task SendEmailChangeConfirmationAsync(string newEmail, string link);

    /// <summary>
    /// Tells the OLD address that the account's sign-in address was changed. Takes both addresses
    /// because a notice that does not name what the address was changed to tells the owner
    /// something happened without telling them enough to act on it.
    /// </summary>
    Task SendEmailChangedNoticeAsync(string oldEmail, string newEmail);
```

`Wend.Api/FileAuthEmailSender.cs` — add both, in the shape the existing two use:

```csharp
    public async Task SendEmailChangeConfirmationAsync(string newEmail, string link)
    {
        var entry = $"[{DateTime.UtcNow:u}] change-email {newEmail}{Environment.NewLine}  {link}{Environment.NewLine}";
        await File.AppendAllTextAsync(path, entry);
        Console.WriteLine(entry);
    }

    public async Task SendEmailChangedNoticeAsync(string oldEmail, string newEmail)
    {
        var entry = $"[{DateTime.UtcNow:u}] email-changed {oldEmail} -> {newEmail}{Environment.NewLine}";
        await File.AppendAllTextAsync(path, entry);
        Console.WriteLine(entry);
    }
```

`Wend.Tests/FakeAuthEmailSender.cs` — add both:

```csharp
    public Task SendEmailChangeConfirmationAsync(string newEmail, string link)
    {
        Sent.Add((newEmail, link, "change-email"));
        return Task.CompletedTask;
    }

    // The notice has no link, so Link carries the NEW address here — several tests turn on the
    // notice naming what the address was changed to, and this keeps the tuple shape every other
    // test in the suite already reads.
    public Task SendEmailChangedNoticeAsync(string oldEmail, string newEmail)
    {
        Sent.Add((oldEmail, newEmail, "changed-notice"));
        return Task.CompletedTask;
    }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test`

Expected: PASS, **257 total**. The count is unchanged on purpose — if it went up, a test was added
that this task did not ask for.

- [ ] **Step 7: Commit**

```bash
git add Wend.Api/ChangeEmailTokenProvider.cs Wend.Api/Program.cs Wend.Core/IAuthEmailSender.cs Wend.Api/FileAuthEmailSender.cs Wend.Tests/FakeAuthEmailSender.cs Wend.Tests/AuthConfigurationTests.cs
git commit -m "Add a change-email token provider and two email-seam methods"
```

---

### Task 2: `POST /api/auth/change-password`

**Files:**
- Modify: `Wend.Api/AuthEndpoints.cs` (one handler after `/reset-password`, one request record)
- Test: `Wend.Tests/AuthChangePasswordTests.cs` (create)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `POST /api/auth/change-password`, authenticated, body
  `{ currentPassword, newPassword }`; `204`, `400 { error: "password" }`,
  `400 { error: "current" }`, or `401`. Record
  `ChangePasswordRequest(string CurrentPassword, string NewPassword)`.

**Nine new tests.** 257 → **266**.

- [ ] **Step 1: Write the failing tests**

Create `Wend.Tests/AuthChangePasswordTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/change-password. The two 400 codes are asserted by name because the Account screen
/// branches on them: one blames the new password, the other the current one, and swapping them
/// produces a screen that tells the user to fix the field they got right.
///
/// The Test auth scheme is deliberate here — this file is about the handler's logic. What the
/// scheme cannot show (the acting session surviving, other sessions dying, persistence carrying
/// across) is in RealCookieAccountTests, because RefreshSignInAsync is a silent no-op without a
/// real cookie and SecurityStampValidator never runs at all.
/// </summary>
public class AuthChangePasswordTests
{
    private const string GoodPassword = "correct horse battery staple";
    private const string NewPassword = "a different long passphrase";

    private WendApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// Registers and confirms an account, then points the Test scheme at it. Everything after this
    /// call acts as that user. NOTE: never call CreateClient() again afterwards — ConfigureClient
    /// resets CurrentUser to the factory's default user.
    /// </summary>
    private async Task<string> ArrangeSignedIn(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));

        _factory.CurrentUser.UserId = user.Id;
        return user.Id;
    }

    private Task<HttpResponseMessage> Change(string currentPassword, string newPassword) =>
        _client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword, newPassword });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        // The length guard is load-bearing: /change-email's malformed-address branch answers a
        // BARE 400 with no body at all, and ReadFromJsonAsync throws on empty content rather than
        // returning null.
        if (response.Content.Headers.ContentLength is null or 0) return null;
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body?.GetValueOrDefault("error");
    }

    private async Task<bool> PasswordWorks(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        return await users.CheckPasswordAsync(user!, password);
    }

    private async Task<WendUser> Reload(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        return (await users.FindByEmailAsync(email))!;
    }

    [Test]
    public async Task An_anonymous_caller_is_refused()
    {
        _factory.CurrentUser.UserId = null;

        var response = await Change(GoodPassword, NewPassword);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_live_session_whose_account_is_gone_is_401_not_500()
    {
        // GetUserAsync returns null rather than throwing (verified against release/10.0). Plan 7
        // makes this ordinary; today it is reachable by pointing the scheme at an id nobody owns.
        _factory.CurrentUser.UserId = Guid.NewGuid().ToString();

        var response = await Change(GoodPassword, NewPassword);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task The_new_password_replaces_the_old_one()
    {
        await ArrangeSignedIn("changer@example.test");

        var response = await Change(GoodPassword, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await PasswordWorks("changer@example.test", NewPassword), Is.True, "new");
            Assert.That(await PasswordWorks("changer@example.test", GoodPassword), Is.False, "old");
        });
    }

    [Test]
    public async Task A_wrong_current_password_is_refused_with_the_current_code()
    {
        await ArrangeSignedIn("changer@example.test");

        var response = await Change("not the password at all", NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("current"));
            Assert.That(await PasswordWorks("changer@example.test", GoodPassword), Is.True);
        });
    }

    [Test]
    public async Task A_weak_new_password_is_refused_before_the_current_one_is_ever_checked()
    {
        await ArrangeSignedIn("changer@example.test");

        // Deliberately BOTH wrong: a weak new password AND a wrong current one. If the handler
        // called ChangePasswordAsync first, this would spend a lockout attempt and answer
        // "current". The ordering is the assertion.
        var response = await Change("also wrong", "short");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("password"));
            Assert.That((await Reload("changer@example.test")).AccessFailedCount, Is.Zero,
                "a weak new password must not cost an attempt at the current one");
            Assert.That(await PasswordWorks("changer@example.test", GoodPassword), Is.True);
        });
    }

    [Test]
    public async Task Five_wrong_current_passwords_lock_the_account()
    {
        await ArrangeSignedIn("guessed@example.test");

        for (var attempt = 0; attempt < 5; attempt++)
            await Change("wrong wrong wrong wrong", NewPassword);

        var user = await Reload("guessed@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(user.LockoutEnd, Is.Not.Null);
            Assert.That(user.LockoutEnd, Is.GreaterThan(DateTimeOffset.UtcNow));
        });
    }

    [Test]
    public async Task A_locked_out_account_is_refused_without_its_password_being_checked()
    {
        await ArrangeSignedIn("guessed@example.test");
        for (var attempt = 0; attempt < 5; attempt++)
            await Change("wrong wrong wrong wrong", NewPassword);

        // The CORRECT current password, while locked. 401, and nothing changes — otherwise anyone
        // holding a stolen session sidesteps lockout by guessing here instead of at /login.
        var response = await Change(GoodPassword, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(await PasswordWorks("guessed@example.test", GoodPassword), Is.True);
            Assert.That(await PasswordWorks("guessed@example.test", NewPassword), Is.False);
        });
    }

    [Test]
    public async Task A_successful_change_clears_the_failed_count()
    {
        await ArrangeSignedIn("recovered@example.test");
        await Change("wrong wrong wrong wrong", NewPassword);
        await Change("wrong wrong wrong wrong", NewPassword);
        var before = (await Reload("recovered@example.test")).AccessFailedCount;

        await Change(GoodPassword, NewPassword);

        var after = (await Reload("recovered@example.test")).AccessFailedCount;
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(2), "two failures were recorded");
            Assert.That(after, Is.Zero, "a successful change clears them");
        });
    }

    [Test]
    public async Task Change_password_binds_json_only()
    {
        await ArrangeSignedIn("changer@example.test");
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("currentPassword", GoodPassword),
            new KeyValuePair<string, string>("newPassword", NewPassword),
        ]);

        var response = await _client.PostAsync("/api/auth/change-password", form);

        // As in AuthResetTests and AuthForgotTests: 404 is the measured behaviour.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AuthChangePasswordTests"`

Expected: all nine FAIL — the route does not exist, so every call returns 404 and even the two
`Unauthorized` tests fail.

- [ ] **Step 3: Write the handler**

In `Wend.Api/AuthEndpoints.cs`, immediately after the `/reset-password` handler (currently ends at
line 226):

```csharp
        // Authenticated, and the first endpoint in this group that can afford to say what went
        // wrong: the caller has already proved who they are, so there is no account existence left
        // to leak. One code blames the new password, the other the current one.
        group.MapPost("/change-password", async (ChangePasswordRequest req,
            UserManager<WendUser> users, SignInManager<WendUser> signIn, ClaimsPrincipal principal,
            ILoggerFactory loggerFactory) =>
        {
            var newPassword = req.NewPassword ?? "";

            // A live cookie whose account no longer exists — ordinary once Plan 7 ships.
            // GetUserAsync returns null rather than throwing (verified against release/10.0), so
            // this is a 401 and not a 500.
            if (await users.GetUserAsync(principal) is not { } user) return Results.Unauthorized();

            // Policy on the NEW password, before the current one is checked and before any lockout
            // accounting can fire. The ordering is the reverse of /reset-password's on purpose:
            // that endpoint is anonymous and validates before the lookup so an early 400 cannot
            // become an existence oracle. Here the caller is already known, so the user is resolved
            // first and the validators are handed the REAL user — which is what a future policy
            // like "your password may not contain your email address" would need.
            // AuthChangePasswordTests guards this: a weak new password must not cost an attempt at
            // the current one. Do not reorder.
            foreach (var validator in users.PasswordValidators)
            {
                if (!(await validator.ValidateAsync(users, user, newPassword)).Succeeded)
                    return Results.BadRequest(new { error = "password" });
            }

            // A locked account is locked for this too. Without it, lockout is trivially sidestepped
            // by anyone holding a session: they stop guessing at /login and start guessing here.
            if (await users.IsLockedOutAsync(user)) return Results.Unauthorized();

            // ChangePasswordAsync verifies the current password and does NO lockout bookkeeping
            // whatsoever (verified against release/10.0). Without the two calls around it, somebody
            // holding a stolen cookie gets unlimited attempts at the current password — and a
            // correct guess converts a session that dies on its own into a permanent takeover.
            // Five attempts is the same budget login allows, applied to the same secret.
            if (!(await users.ChangePasswordAsync(user, req.CurrentPassword ?? "", newPassword))
                    .Succeeded)
            {
                await users.AccessFailedAsync(user);
                return Results.BadRequest(new { error = "current" });
            }

            await users.ResetAccessFailedCountAsync(user);

            // AFTER the change, so the rotated stamp lands in the reissued cookie. The password
            // write rotates the security stamp, and at ValidationInterval.Zero that refuses every
            // live cookie for this user on its next request — including the browser that just
            // submitted the form. Every OTHER session dying is the point; this one dying is a bug
            // the user experiences as being logged out for changing their password.
            //
            // RefreshSignInAsync returns no result, so the only failure it can report is an
            // exception. Under the Test auth scheme it finds no application cookie and is a silent
            // no-op, which is why the assertion that this works lives in RealCookieAccountTests.
            try
            {
                await signIn.RefreshSignInAsync(user);
            }
            catch (Exception ex)
            {
                // Still 204: the password genuinely changed, and telling the user otherwise would
                // send them round the loop for nothing. The degraded outcome is that their next
                // request 401s and they sign in again with the new password, which works. Same rule
                // /reset-password applies to a failed lockout clear. Type name only, never a
                // message — an exception message can carry data.
                loggerFactory.CreateLogger("Wend.Api.AuthEndpoints")
                    .LogWarning("Password changed but the session was not refreshed: {Error}",
                        ex.GetType().Name);
            }

            return Results.NoContent();
        }).RequireAuthorization();
```

And with the other request records at the bottom of the file:

```csharp
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test`

Expected: PASS, **266 total**.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthChangePasswordTests.cs
git commit -m "Add POST /api/auth/change-password with lockout accounting"
```

---

### Task 3: `POST /api/auth/change-email`

**Files:**
- Modify: `Wend.Api/AuthEndpoints.cs` (one handler after `/change-password`, one link-builder, one
  request record)
- Test: `Wend.Tests/AuthChangeEmailTests.cs` (create)

**Interfaces:**
- Consumes: `IAuthEmailSender.SendEmailChangeConfirmationAsync` and the `WendChangeEmail` provider
  from Task 1.
- Produces: `POST /api/auth/change-email`, authenticated, body `{ newEmail }`; `204`, a bare `400`,
  `400 { error: "same" }`, or `401`. Record `ChangeEmailRequest(string NewEmail)`. Private helper
  `BuildChangeEmailLinkAsync(WendUser, string, UserManager<WendUser>, HttpRequest, string?)`
  returning the `/confirm-email-change?userId=…&newEmail=…&code=…` link.

**Ten new tests.** 266 → **276**.

- [ ] **Step 1: Write the failing tests**

Create `Wend.Tests/AuthChangeEmailTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/change-email — the request half. Four different outcomes fall out of the same 204,
/// and which of them is which is invisible from outside on purpose: this endpoint is authenticated
/// and feels private, which is exactly why a 409 for a taken address looks reasonable here. It
/// would let any account holder walk the user table one address at a time from their own settings
/// screen.
/// </summary>
public class AuthChangeEmailTests
{
    private const string GoodPassword = "correct horse battery staple";

    private WendApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>Registers and confirms an account WITHOUT pointing the Test scheme at it.</summary>
    private async Task<string> Seed(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));
        return user.Id;
    }

    /// <summary>
    /// Seeds an account and acts as it. NOTE: never call CreateClient() after this —
    /// ConfigureClient resets CurrentUser to the factory's default user.
    /// </summary>
    private async Task<string> ArrangeSignedIn(string email)
    {
        var id = await Seed(email);
        _factory.CurrentUser.UserId = id;
        return id;
    }

    private Task<HttpResponseMessage> Request(string newEmail) =>
        _client.PostAsJsonAsync("/api/auth/change-email", new { newEmail });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        // The length guard is load-bearing: /change-email's malformed-address branch answers a
        // BARE 400 with no body at all, and ReadFromJsonAsync throws on empty content rather than
        // returning null.
        if (response.Content.Headers.ContentLength is null or 0) return null;
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body?.GetValueOrDefault("error");
    }

    private static (string UserId, string NewEmail, string Code) ReadLink(string link)
    {
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"]!, query["newEmail"]!, query["code"]!);
    }

    [Test]
    public async Task An_anonymous_caller_is_refused()
    {
        _factory.CurrentUser.UserId = null;

        var response = await Request("elsewhere@example.test");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_live_session_whose_account_is_gone_is_401_not_500()
    {
        // The same guard as /change-password, on a different handler. A reviewer can reject one
        // while approving the other, so both are asserted.
        _factory.CurrentUser.UserId = Guid.NewGuid().ToString();

        var response = await Request("elsewhere@example.test");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task The_address_the_account_already_has_is_refused_as_same()
    {
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        // Cased differently on purpose: the comparison is normalised, not a raw string compare.
        var response = await Request("MALIN@example.test");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("same"));
            Assert.That(_factory.Email.Sent, Is.Empty);
        });
    }

    [Test]
    public async Task A_malformed_empty_or_over_length_address_is_a_bare_400()
    {
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var empty = await Request("   ");
        var malformed = await Request("not-an-address");
        var tooLong = await Request(new string('a', 250) + "@example.test");

        // Bare, with no code: the screen validates format client-side, so reaching this means a
        // caller bypassing the form, and there is no screen state that needs to tell them apart.
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "empty");
            Assert.That(await ErrorCode(empty), Is.Null, "empty carries no code");
            Assert.That(malformed.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "malformed");
            Assert.That(tooLong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "over 254");
            Assert.That(_factory.Email.Sent, Is.Empty);
        });
    }

    [Test]
    public async Task An_address_another_account_holds_gets_a_silent_204_with_no_mail()
    {
        await Seed("taken@example.test");
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("taken@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Email.Sent, Is.Empty,
                "a mail here would answer 'does this address exist' for any account holder");
        });
    }

    [Test]
    public async Task An_address_another_account_holds_only_as_a_user_name_gets_the_same_silent_204()
    {
        var otherId = await Seed("other@example.test");
        await MoveUserNameOnly(otherId, "stranded@example.test");
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("stranded@example.test");

        // FindByEmailAsync alone would call this free and mint a token for an address that then
        // fails DuplicateUserName at confirm time — a link that cannot work, sent to a real inbox.
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Email.Sent, Is.Empty);
        });
    }

    [Test]
    public async Task The_callers_own_stranded_user_name_is_not_treated_as_taken()
    {
        // The 409 half-changed state, arranged directly: Email is the new address, UserName is
        // still the old one. Re-requesting the OLD address is the repair path out of it, and
        // excluding self from the lookup is the only thing that makes it reachable — without it
        // the user gets a silent 204 forever, on the one address they most want back.
        var id = await ArrangeSignedIn("malin@example.test");
        await MoveEmailOnly(id, "moved@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("malin@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Email.Sent.Single().Kind, Is.EqualTo("change-email"));
            Assert.That(_factory.Email.Sent.Single().Email, Is.EqualTo("malin@example.test"));
        });
    }

    [Test]
    public async Task A_free_address_gets_exactly_one_confirmation_to_the_new_address()
    {
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("newer@example.test");

        var sent = _factory.Email.Sent.Single();
        var (userId, newEmail, code) = ReadLink(sent.Link);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(sent.Kind, Is.EqualTo("change-email"));
            Assert.That(sent.Email, Is.EqualTo("newer@example.test"),
                "the confirmation goes to the NEW address, never the old one");
            Assert.That(sent.Link, Does.Contain("/confirm-email-change"));
            Assert.That(userId, Is.Not.Empty);
            Assert.That(newEmail, Is.EqualTo("newer@example.test"));
            Assert.That(code, Is.Not.Empty);
        });
    }

    [Test]
    public async Task An_older_token_still_works_after_a_newer_one_is_issued()
    {
        // Nothing rotates on request, so two requests leave two live tokens and whichever link is
        // clicked first wins. Someone who re-requests BECAUSE they think the first link was seen
        // has revoked nothing. This test writes that down rather than leaving it to be discovered;
        // if it ever starts failing, the revocation model has changed and the design is wrong.
        var id = await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();
        await Request("newer@example.test");
        var first = ReadLink(_factory.Email.Sent.Single().Link);
        await Request("newer@example.test");

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByIdAsync(id))!;
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(first.Code));
        var redeemed = await users.ChangeEmailAsync(user, "newer@example.test", token);

        Assert.That(redeemed.Succeeded, Is.True, "the first link still works");
    }

    [Test]
    public async Task Change_email_binds_json_only()
    {
        await ArrangeSignedIn("malin@example.test");
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("newEmail", "newer@example.test"),
        ]);

        var response = await _client.PostAsync("/api/auth/change-email", form);

        // As in AuthResetTests and AuthForgotTests: 404 is the measured behaviour.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>Leaves an account holding <paramref name="userName"/> as a UserName only.</summary>
    private async Task MoveUserNameOnly(string userId, string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByIdAsync(userId))!;
        var result = await users.SetUserNameAsync(user, userName);
        Assert.That(result.Succeeded, Is.True, "arrangement failed");
    }

    /// <summary>
    /// Writes Email straight through the context, leaving UserName behind — the half-changed state
    /// /confirm-email-change's 409 branch produces. Deliberately corrupt, and deliberately not
    /// built through UserManager, which would keep the two in step.
    /// </summary>
    private async Task MoveEmailOnly(string userId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();
        var row = await db.Users.SingleAsync(u => u.Id == userId);
        row.Email = email;
        row.NormalizedEmail = email.ToUpperInvariant();
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AuthChangeEmailTests"`

Expected: all ten FAIL — the route does not exist, so every call returns 404.

- [ ] **Step 3: Write the handler**

In `Wend.Api/AuthEndpoints.cs`, after the `/change-password` handler:

```csharp
        // Authenticated. One 204 covers four different outcomes — free, held by another account as
        // an email, held by another account as a user name, and held by the caller themselves in
        // the half-changed state — because an authenticated endpoint that names which addresses
        // exist is a user-table oracle with a login in front of it. The one 400 with a code names a
        // property of the caller's OWN account, which they already know.
        group.MapPost("/change-email", async (ChangeEmailRequest req, UserManager<WendUser> users,
            IAuthEmailSender email, HttpRequest http, ClaimsPrincipal principal) =>
        {
            var address = req.NewEmail?.Trim() ?? "";

            // Input-only checks first, per the standing rule: refusing malformed input is safe, but
            // it must happen BEFORE any existence lookup or the 400 becomes an oracle. The bare 400
            // carries no code deliberately — the screen validates format client-side, so reaching
            // it means a caller bypassing the form.
            if (address.Length is 0 or > MaxEmailLength) return Results.BadRequest();
            if (!new EmailAddressAttribute().IsValid(address)) return Results.BadRequest();

            if (await users.GetUserAsync(principal) is not { } user) return Results.Unauthorized();

            // Normalised, not a raw string compare: "MALIN@example.test" is the address they have.
            // Naming this leaks nothing — the caller is authenticated and already knows their own
            // address — and a silent 204 here would promise an inbox that never receives anything.
            if (string.Equals(users.NormalizeEmail(address), user.NormalizedEmail,
                    StringComparison.Ordinal))
                return Results.BadRequest(new { error = "same" });

            // BOTH lookups. An address can be occupied as a UserName while free as an Email — that
            // is exactly the desync SetUserNameAsync exists to prevent — and minting a token for
            // one of those would produce a confirmation link that fails at confirm time, sent to a
            // real inbox.
            //
            // Self is excluded, and self is reachable: in the half-changed state Email is the new
            // address while UserName is still the old one, so FindByNameAsync(old) returns the
            // caller. Excluding self lets them re-request their old address and repair it; not
            // excluding it hands them a silent 204 forever, on the one address they most want back.
            var byEmail = await users.FindByEmailAsync(address);
            var byName = await users.FindByNameAsync(address);
            if ((byEmail is not null && byEmail.Id != user.Id)
                || (byName is not null && byName.Id != user.Id))
            {
                return Results.NoContent();
            }

            // Nothing is written. The account keeps its old address until the link is clicked, so
            // an abandoned request leaves no state to clean up and no pending-change indicator to
            // render — the trade accepted by putting the pending address in the query string.
            var link = await BuildChangeEmailLinkAsync(user, address, users, http, publicBaseUrl);
            await email.SendEmailChangeConfirmationAsync(address, link);

            return Results.NoContent();
        }).RequireAuthorization();
```

The link builder, beside `BuildResetLinkAsync`:

```csharp
    /// <summary>
    /// Mints a change-email token and builds the link to the SPA's /confirm-email-change screen.
    /// The token is bound to (user, new address, security stamp), so the address has to travel with
    /// it — which is why this link carries a third parameter the other two do not.
    ///
    /// Same configured origin as the other two, and it matters more here: a link that repoints an
    /// account's login identity is worth as much to an attacker as a reset link, so building the
    /// origin from http.Host would let anyone who can set that header have Wend email a victim a
    /// genuine-looking link pointing at their own server.
    /// </summary>
    private static async Task<string> BuildChangeEmailLinkAsync(WendUser user, string newEmail,
        UserManager<WendUser> users, HttpRequest http, string? publicBaseUrl)
    {
        var token = await users.GenerateChangeEmailTokenAsync(user, newEmail);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var origin = publicBaseUrl?.TrimEnd('/') ?? $"{http.Scheme}://{http.Host}";
        return $"{origin}/confirm-email-change" +
               $"?userId={Uri.EscapeDataString(user.Id)}" +
               $"&newEmail={Uri.EscapeDataString(newEmail)}" +
               $"&code={Uri.EscapeDataString(code)}";
    }
```

And the request record:

```csharp
public record ChangeEmailRequest(string NewEmail);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test`

Expected: PASS, **276 total**.

- [ ] **Step 5: Commit**

```bash
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthChangeEmailTests.cs
git commit -m "Add POST /api/auth/change-email behind a generic 204"
```

---

### Task 4: `POST /api/auth/confirm-email-change`

**Files:**
- Modify: `Wend.Api/AuthEndpoints.cs` (one handler after `/change-email`, one request record)
- Test: `Wend.Tests/AuthConfirmEmailChangeTests.cs` (create)

**Interfaces:**
- Consumes: the link built by Task 3, and `IAuthEmailSender.SendEmailChangedNoticeAsync` from
  Task 1.
- Produces: `POST /api/auth/confirm-email-change`, **anonymous**, body
  `{ userId, newEmail, code }`; `200 { email }`, `400 { error: "token" }`, or
  `409 { error: "taken" }`. Record `ConfirmEmailChangeRequest(string UserId, string NewEmail,
  string Code)`.

**Eleven new tests.** 276 → **287**.

**This is the task that carries the plan's one genuinely dangerous failure.** Step 4 below is the
correctness requirement, and `Registering_the_old_address_afterwards_still_works` is the only test
in the suite that catches its absence.

- [ ] **Step 1: Write the failing tests**

Create `Wend.Tests/AuthConfirmEmailChangeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/confirm-email-change — the half that applies the change. Anonymous, because the link
/// lands in a mailbox that may be open in a different browser and possession of a token bound to
/// (user, new address, stamp) is the proof.
///
/// The load-bearing test here is Registering_the_old_address_afterwards_still_works. Without
/// SetUserNameAsync it fails and every other test in this repo still passes.
/// </summary>
public class AuthConfirmEmailChangeTests
{
    private const string GoodPassword = "correct horse battery staple";

    private WendApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> Seed(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));
        return user.Id;
    }

    /// <summary>Seeds an account, acts as it, requests a change, and returns the emailed link.</summary>
    private async Task<(string UserId, string NewEmail, string Code)> ArrangeLink(
        string from, string to)
    {
        _factory.CurrentUser.UserId = await Seed(from);
        _factory.Email.Sent.Clear();
        await _client.PostAsJsonAsync("/api/auth/change-email", new { newEmail = to });
        return ReadLink(_factory.Email.Sent.Single().Link);
    }

    private static (string UserId, string NewEmail, string Code) ReadLink(string link)
    {
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"]!, query["newEmail"]!, query["code"]!);
    }

    private Task<HttpResponseMessage> Confirm(string userId, string newEmail, string code) =>
        _client.PostAsJsonAsync("/api/auth/confirm-email-change",
            new { userId, newEmail, code });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        // The length guard is load-bearing: /change-email's malformed-address branch answers a
        // BARE 400 with no body at all, and ReadFromJsonAsync throws on empty content rather than
        // returning null.
        if (response.Content.Headers.ContentLength is null or 0) return null;
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body?.GetValueOrDefault("error");
    }

    private async Task<WendUser> Reload(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        return (await users.FindByIdAsync(userId))!;
    }

    [Test]
    public async Task A_valid_link_changes_both_fields_and_returns_the_stored_address()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");

        var response = await Confirm(userId, newEmail, code);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var user = await Reload(userId);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            // Read back off the row after BOTH writes, so the screen reports what the database
            // holds rather than what the caller asked for.
            Assert.That(body?.GetValueOrDefault("email"), Is.EqualTo("newer@example.test"));
            Assert.That(user.Email, Is.EqualTo("newer@example.test"), "Email");
            Assert.That(user.UserName, Is.EqualTo("newer@example.test"), "UserName");
        });
    }

    [Test]
    public async Task Registering_the_old_address_afterwards_still_works()
    {
        // THE regression test. ChangeEmailAsync does not touch UserName (verified against
        // release/10.0), and RequireUniqueEmail switches on UserValidator's UserName uniqueness
        // check as well as the email one — so without SetUserNameAsync the abandoned address stays
        // occupied. A later registration to it then fails DuplicateUserName, which /register
        // answers with 204 and a code-only log line. The caller sees success, no mail is ever sent,
        // and the failure lands on a stranger months later.
        //
        // Login would keep working either way (it resolves through NormalizedEmail), which is why
        // nothing else in this suite catches it.
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await Confirm(userId, newEmail, code);
        _factory.Email.Sent.Clear();

        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "malin@example.test", password = GoodPassword, displayName = "Someone" });

        Assert.That(_factory.Email.Sent.Where(s => s.Kind == "confirm"), Is.Not.Empty,
            "the old address is squatted as a UserName — SetUserNameAsync is missing");
    }

    [Test]
    public async Task Sign_in_uses_the_new_address_and_the_old_one_is_refused()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await Confirm(userId, newEmail, code);

        var withNew = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "newer@example.test", password = GoodPassword });
        var withOld = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "malin@example.test", password = GoodPassword });

        Assert.Multiple(() =>
        {
            Assert.That(withNew.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "new address");
            Assert.That(withOld.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "old address");
        });
    }

    [Test]
    public async Task A_reused_link_is_a_token_error()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await Confirm(userId, newEmail, code);

        // Single-use comes from stamp rotation, not from a guard: the first confirmation rotated
        // the stamp the token was bound to.
        var second = await Confirm(userId, newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(second), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task A_tampered_new_email_is_a_token_error()
    {
        var (userId, _, code) = await ArrangeLink("malin@example.test", "newer@example.test");

        // The token is bound to the address, so swapping the address in the URL invalidates it.
        var response = await Confirm(userId, "attacker@example.test", code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
            Assert.That((await Reload(userId)).Email, Is.EqualTo("malin@example.test"));
        });
    }

    [Test]
    public async Task A_token_minted_for_one_user_cannot_change_another()
    {
        var (_, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        var victimId = await Seed("victim@example.test");

        var response = await Confirm(victimId, newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
            Assert.That((await Reload(victimId)).Email, Is.EqualTo("victim@example.test"));
        });
    }

    [Test]
    public async Task A_missing_or_unknown_user_id_is_a_token_error()
    {
        var (_, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");

        var missing = await Confirm("", newEmail, code);
        var unknown = await Confirm(Guid.NewGuid().ToString(), newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "missing");
            Assert.That(await ErrorCode(missing), Is.EqualTo("token"));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "unknown");
            Assert.That(await ErrorCode(unknown), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task An_address_taken_since_the_request_is_409_taken()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        // Somebody else claims it in the minutes between the request and the click. This is where
        // the parent spec's "uniqueness re-checked at confirm time" actually happens: UserValidator
        // firing inside UpdateUserAsync. This plan's job is only to tell the two failures apart.
        await Seed("newer@example.test");

        var response = await Confirm(userId, newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            // Not "token". A dead token means "get a new link"; a taken address means "pick a
            // different address". Collapsing them tells someone their link expired when the link
            // was fine.
            Assert.That(await ErrorCode(response), Is.EqualTo("taken"));
            Assert.That((await Reload(userId)).Email, Is.EqualTo("malin@example.test"));
        });
    }

    [Test]
    public async Task One_notice_goes_to_the_old_address_after_success_and_none_on_failure()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        _factory.Email.Sent.Clear();

        await Confirm(userId, newEmail, code);
        var afterSuccess = _factory.Email.Sent.Where(s => s.Kind == "changed-notice").ToList();
        _factory.Email.Sent.Clear();
        await Confirm(userId, newEmail, code);   // the replay: a token failure

        Assert.Multiple(() =>
        {
            Assert.That(afterSuccess, Has.Count.EqualTo(1));
            // Sent to the OLD address, naming the new one — the only mechanism by which an owner
            // learns that somebody with a live session repointed their account.
            Assert.That(afterSuccess[0].Email, Is.EqualTo("malin@example.test"), "recipient");
            Assert.That(afterSuccess[0].Link, Is.EqualTo("newer@example.test"), "names the new one");
            Assert.That(_factory.Email.Sent.Where(s => s.Kind == "changed-notice"), Is.Empty,
                "a notice on failure is an email bomb an attacker aims at the victim");
        });
    }

    [Test]
    public async Task A_password_change_between_the_request_and_the_confirmation_kills_the_link()
    {
        // Correct behaviour, and the kind of coupling that only shows up if something looks for it:
        // both flows rotate the same security stamp, and the change-email token is bound to it.
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await _client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = GoodPassword, newPassword = "a different long passphrase" });

        var response = await Confirm(userId, newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task Confirm_email_change_binds_json_only()
    {
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("userId", "whoever"),
            new KeyValuePair<string, string>("newEmail", "newer@example.test"),
            new KeyValuePair<string, string>("code", "whatever"),
        ]);

        var response = await _client.PostAsync("/api/auth/confirm-email-change", form);

        // As in AuthResetTests and AuthForgotTests: 404 is the measured behaviour.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AuthConfirmEmailChangeTests"`

Expected: all eleven FAIL — the route does not exist.

- [ ] **Step 3: Write the handler**

In `Wend.Api/AuthEndpoints.cs`, after the `/change-email` handler. **Anonymous** — the group has no
`RequireAuthorization()`, so leaving the attribute off is what makes it so; do not add one.

```csharp
        // POST, not GET, even though this arrives from an emailed link — the same reasoning /verify
        // carries, and it bites harder here. Corporate mail scanners and link-preview bots follow
        // GET links automatically, so a GET that applied the change would be fired by a robot
        // before the human ever clicked, silently repointing an account's login identity. The
        // emailed link therefore points at the SPA shell, and the screen POSTs the values back.
        //
        // Anonymous, like /verify and /reset-password: the link lands in a mailbox the user may
        // open in a different browser, possession of a token bound to (user, new address, stamp) is
        // the proof, and the change completes even if the session expired in the meantime.
        group.MapPost("/confirm-email-change", async (ConfirmEmailChangeRequest req,
            UserManager<WendUser> users, IAuthEmailSender email, HttpResponse response,
            ILoggerFactory loggerFactory) =>
        {
            if (req.UserId is not { Length: > 0 } id)
                return Results.BadRequest(new { error = "token" });
            if (await users.FindByIdAsync(id) is not { } user)
                return Results.BadRequest(new { error = "token" });

            var newEmail = req.NewEmail?.Trim() ?? "";
            // Captured BEFORE the write, because the notice has to name the address that is losing
            // the account and `user` is mutated in place two lines further down.
            var oldEmail = user.Email!;

            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Code ?? ""));
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "token" });
            }

            // Verifies the token, writes Email and EmailConfirmed, rotates the security stamp, then
            // runs UserValidator through UpdateUserAsync — which is where the parent spec's
            // "uniqueness re-checked at confirm time" actually happens. All verified against
            // release/10.0. It does NOT touch UserName; that is the next call's whole reason.
            var changed = await users.ChangeEmailAsync(user, newEmail, token);
            if (!changed.Succeeded)
            {
                // Two codes, not one. A dead token means "get a new link"; a taken address means
                // "pick a different address". Collapsing them produces a screen that says the link
                // expired when the link was fine and somebody took the address four minutes ago.
                return changed.Errors.Any(e => e.Code == "DuplicateEmail")
                    ? Results.Conflict(new { error = "taken" })
                    : Results.BadRequest(new { error = "token" });
            }

            // The plan's correctness requirement, not tidiness. Wend sets UserName = Email at
            // registration and ChangeEmailAsync leaves UserName holding the OLD address forever.
            // Login keeps working — it resolves through NormalizedEmail — so the bug passes every
            // obvious test. But RequireUniqueEmail switches on UserValidator's UserName uniqueness
            // check too, so the abandoned address stays occupied, and the next registration to it
            // fails DuplicateUserName, which /register answers with 204 and a log line. Silent,
            // delayed, and it lands on a stranger.
            //
            // The SAME instance, not a reload: ChangeEmailAsync has just refreshed this user's
            // concurrency stamp and reloading here would work from a stale one. Same two-call
            // pattern as /reset-password's lockout clear. This call rotates the security stamp a
            // second time (verified), which is harmless — every session died at the first rotation.
            var renamed = await users.SetUserNameAsync(user, newEmail);
            if (!renamed.Succeeded)
            {
                // The narrowest path in the plan, and it still needs its branch: /change-email
                // checks both lookups precisely so an address free as one is free as the other, so
                // what is left is a genuine race between two confirmations. The account is now
                // half-changed, which is the state this endpoint exists to prevent, so it must not
                // report success. A retry fails the token check because the stamp already rotated,
                // which is what the screen tells the user. Error CODES only.
                loggerFactory.CreateLogger("Wend.Api.AuthEndpoints")
                    .LogWarning("Email changed but the user name was not: {Errors}",
                        string.Join("; ", renamed.Errors.Select(e => e.Code)));
                return Results.Conflict(new { error = "taken" });
            }

            // Off the response path, like login's nudge. Success only: a notice on a failed attempt
            // would be an email bomb an attacker aims at the victim, from the victim's own account.
            response.OnCompleted(async () =>
                await email.SendEmailChangedNoticeAsync(oldEmail, newEmail));

            // The only endpoint in /api/auth/* that returns a body, and it earns it. The success
            // screen has to name the new address, and the only other sources are the query string —
            // a caller-controlled value on an anonymous page, which is the reflected-XSS shape the
            // frontend rule forbids — and nothing at all, which leaves the user trusting that what
            // they typed ten minutes ago is what landed. Read off the user AFTER both writes.
            //
            // No RefreshSignInAsync, and there must not be one: this request is anonymous and may
            // be arriving from a different browser than the one holding the session, so "refresh
            // the acting session" has no meaning here. Every live session is refused on its next
            // request and the user signs in with the new address.
            return Results.Ok(new { email = user.Email });
        });
```

And the request record:

```csharp
public record ConfirmEmailChangeRequest(string UserId, string NewEmail, string Code);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test`

Expected: PASS, **287 total**.

- [ ] **Step 5: Prove the regression test actually bites**

Comment out the `SetUserNameAsync` block (keeping `return Results.Ok(...)`), then run:

`dotnet test --filter "FullyQualifiedName~AuthConfirmEmailChangeTests"`

Expected: **exactly one failure** —
`Registering_the_old_address_afterwards_still_works`. If it passes with the call removed, the test
is not testing what it claims and this task is not done. Restore the block and re-run.

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthConfirmEmailChangeTests.cs
git commit -m "Add POST /api/auth/confirm-email-change, keeping UserName in step with Email"
```

---

### Task 5: The real-cookie walk

**Files:**
- Test: `Wend.Tests/RealCookieAccountTests.cs` (create)

**Interfaces:**
- Consumes: all three endpoints from Tasks 2–4. Adds no production code.

**Four new tests.** 287 → **291**.

**Why this task exists at all.** Three of the design's required assertions cannot run under the
`Test` auth scheme, and a test-scheme version of any of them would pass while testing nothing:

- `RefreshSignInAsync` calls `Context.AuthenticateAsync(IdentityConstants.ApplicationScheme)`,
  finds no cookie under the test scheme, logs an error and returns — so "the acting session
  survives" is vacuously true there.
- `SecurityStampValidator` hangs off the **cookie's** `OnValidatePrincipal`, so "another session
  dies" never fires under the test scheme either.
- The test scheme issues no cookie at all, so there is no `Set-Cookie` to check for `expires=`.

- [ ] **Step 1: Write the failing tests**

Create `Wend.Tests/RealCookieAccountTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Wend.Tests;

/// <summary>
/// Account settings on the genuine cookie scheme, no test auth anywhere: which sessions survive a
/// password change, which die, and whether a remembered cookie stays remembered. Every assertion
/// here is one the Test scheme cannot make — see the note in the plan.
/// </summary>
public class RealCookieAccountTests
{
    private const string GoodPassword = "correct horse battery staple";
    private const string NewPassword = "a different long passphrase";

    private WendApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory(useTestAuth: false);
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task The_test_scheme_really_is_off()
    {
        // The canary, and deliberately the first test in the file. The factory seeds a default user
        // and points CurrentUser at it on every CreateClient(), so a suite that forgot
        // useTestAuth: false is authenticated before it does anything — and every assertion below
        // would pass while testing nothing. This repo has been bitten by that shape twice.
        var response = await _client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>Registers and confirms an account over HTTP, the way a browser would.</summary>
    private async Task Arrange(HttpClient client, string email)
    {
        _factory.Email.Sent.Clear();
        await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });
        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Last().Link).Query);
        await client.PostAsJsonAsync("/api/auth/verify",
            new { userId = query["userId"], code = query["code"] });
    }

    private static Task<HttpResponseMessage> Login(
        HttpClient client, string email, bool rememberMe = false) =>
        client.PostAsJsonAsync("/api/auth/login",
            new { email, password = GoodPassword, rememberMe });

    private static Task<HttpResponseMessage> ChangePassword(HttpClient client, string newPassword) =>
        client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = GoodPassword, newPassword });

    // Last(), not Single(): a change-password response can carry more than one wend.session
    // Set-Cookie — RefreshSignInAsync writes one, and SlidingExpiration's renewal can write another.
    // The last one is the cookie the browser ends up holding.
    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Last(c => c.StartsWith("wend.session="));

    [Test]
    public async Task The_acting_session_survives_a_password_change_while_another_one_dies()
    {
        await Arrange(_client, "walker@example.test");
        await Login(_client, "walker@example.test");

        // A second browser, signed in as the same person. Its cookie is what the password change
        // is supposed to kill.
        using var elsewhere = _factory.CreateClient();
        await Login(elsewhere, "walker@example.test");
        var elsewhereBefore = await elsewhere.GetAsync("/api/boards");

        var changed = await ChangePassword(_client, NewPassword);

        var actingAfter = await _client.GetAsync("/api/boards");
        var elsewhereAfter = await elsewhere.GetAsync("/api/boards");

        // The PAIR is the point. Either assertion alone is satisfiable by a wrong implementation:
        // drop RefreshSignInAsync and the second passes while the first fails; skip the stamp
        // rotation and the reverse.
        Assert.Multiple(() =>
        {
            Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "change");
            Assert.That(elsewhereBefore.StatusCode, Is.EqualTo(HttpStatusCode.OK), "other, before");
            Assert.That(actingAfter.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "the browser that changed the password must NOT be signed out");
            Assert.That(elsewhereAfter.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "every other session must be");
        });
    }

    [Test]
    public async Task A_remembered_session_is_still_remembered_after_a_password_change()
    {
        await Arrange(_client, "remembered@example.test");
        await Login(_client, "remembered@example.test", rememberMe: true);

        using var plain = _factory.CreateClient();
        await Arrange(plain, "forgotten@example.test");
        await Login(plain, "forgotten@example.test");

        var remembered = await ChangePassword(_client, NewPassword);
        var session = await ChangePassword(plain, NewPassword);

        // Both directions, because "always writes expires=" would satisfy the first alone. If the
        // reissued cookie loses its persistence, a remembered login silently becomes a session
        // cookie and the user is signed out a day later with nothing to blame.
        Assert.Multiple(() =>
        {
            Assert.That(SessionCookie(remembered), Does.Contain("expires=").IgnoreCase,
                "remember-me must survive the reissue");
            Assert.That(SessionCookie(session), Does.Not.Contain("expires=").IgnoreCase,
                "and a session cookie must stay one");
        });
    }

    [Test]
    public async Task Every_session_dies_after_an_email_change_including_the_one_that_asked()
    {
        await Arrange(_client, "walker@example.test");
        await Login(_client, "walker@example.test");
        var before = await _client.GetAsync("/api/boards");

        _factory.Email.Sent.Clear();
        await _client.PostAsJsonAsync("/api/auth/change-email",
            new { newEmail = "newer@example.test" });
        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Single().Link).Query);

        // Confirmed from a THIRD client with no session, which is the realistic shape: the link
        // lands in a mailbox that may be open in a different browser entirely.
        using var mailbox = _factory.CreateClient();
        var confirmed = await mailbox.PostAsJsonAsync("/api/auth/confirm-email-change",
            new { userId = query["userId"], newEmail = query["newEmail"], code = query["code"] });

        var after = await _client.GetAsync("/api/boards");

        // No carve-out here, unlike change-password, and that is deliberate: this request is
        // anonymous and may not be coming from the session that asked, so there is no acting
        // session to refresh.
        Assert.Multiple(() =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK), "before");
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.OK), "confirm");
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "after");
        });
    }
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test`

Expected: PASS, **291 total**. These test code that already exists, so they should pass first time —
if any of them fails, the failure is real and belongs to Task 2 or Task 4, not to this test file.

- [ ] **Step 3: Prove `RefreshSignInAsync` is load-bearing**

Comment out the `try { await signIn.RefreshSignInAsync(user); }` call in `/change-password`, then
run: `dotnet test --filter "FullyQualifiedName~RealCookieAccountTests"`

Expected: **exactly one failure** —
`The_acting_session_survives_a_password_change_while_another_one_dies`, on the `actingAfter`
assertion. Restore the call and re-run.

- [ ] **Step 4: Commit**

```bash
git add Wend.Tests/RealCookieAccountTests.cs
git commit -m "Cover account settings on the real cookie scheme"
```

---

### Task 6: The Account screen

**Files:**
- Create: `Wend.Api/wwwroot/js/auth/account/model.js`
- Create: `Wend.Api/wwwroot/js/auth/account/view.js`
- Create: `Wend.Api/wwwroot/js/auth/account/controller.js`
- Modify: `Wend.Api/wwwroot/js/main.js` (three imports, `showAccount`, the `onAccount` callback)
- Modify: `Wend.Api/wwwroot/js/settings/view.js` (the Account button)
- Modify: `Wend.Api/wwwroot/js/settings/controller.js` (forwards it)
- Modify: `Wend.Api/wwwroot/css/app.css` (an `.account-*` block)

**Interfaces:**
- Consumes: `POST /api/auth/change-password` (Task 2), `POST /api/auth/change-email` (Task 3),
  `GET /api/auth/me` for the current address.
- Produces: `createAccountModel()`, `createAccountView(root)`,
  `createAccountController(model, view, announce, { onBack })`; `showAccount()` in `main.js`;
  `createSettingsController(model, view, announce, { onBack, onAccount })`.

**No automated tests** — this repo has no JS test harness. Step 7 is a scripted manual walk, and
every browser check hard-reloads. Total stays **291**.

**The three two-form rules, and how the code makes them structural.** Every auth screen so far has
had exactly one form and one error region, so the announcer and the focus helpers have never had to
disambiguate. Rather than maintain the rules by hand in one whole-screen repaint, the view renders
the shell once and gives each form its own independently-replaced body:

1. **Each form owns its own error region** — its own element, its own alert, its own
   disable-while-in-flight. A submit in one never clears the other's, because a submit in one never
   touches the other's DOM.
2. **Focus after any submit stays inside the submitting form** — its own success alert, its own
   error alert, or its own `<h3>`. Never `<body>`, which is what a whole-screen `innerHTML` repaint
   would produce.
3. **Each form has its own `<h3>` and is associated with it via `aria-labelledby`**, so a screen
   reader lands in a password-shaped form and is told which of the two it is.

- [ ] **Step 1: Write the model**

`Wend.Api/wwwroot/js/auth/account/model.js`:

```js
import { api } from "../../api.js";

// State only. Two sub-states that never touch each other: a failed password change must not
// disturb whatever the email form is showing, and vice versa. `what` tells the controller which
// half changed, so it can repaint and focus that half alone.
export function createAccountModel() {
  let state = {
    email: "",
    password: { status: "editing", errors: [] },
    emailChange: { status: "editing", errors: [], newEmail: "" },
  };
  const subscribers = [];
  const notify = (what) => subscribers.forEach((fn) => fn(state, what));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state, "all");
    },

    // Called once, at mount. The address comes from the server rather than from anything cached,
    // so a screen opened after a change in another tab still shows the truth.
    async load() {
      const me = await api("/api/auth/me");
      state = { ...state, email: me?.email ?? "" };
      notify("all");
    },

    async changePassword({ currentPassword, newPassword }) {
      if (state.password.status === "sending") return;
      state = { ...state, password: { status: "sending", errors: [] } };
      notify("password");
      try {
        await api("/api/auth/change-password", {
          method: "POST",
          body: JSON.stringify({ currentPassword, newPassword }),
        });
        state = { ...state, password: { status: "done", errors: [] } };
      } catch (error) {
        state = { ...state, password: { status: "editing", errors: [passwordError(error)] } };
      }
      notify("password");
    },

    async changeEmail({ newEmail }) {
      if (state.emailChange.status === "sending") return;
      state = { ...state, emailChange: { status: "sending", errors: [], newEmail } };
      notify("email");
      try {
        await api("/api/auth/change-email", {
          method: "POST",
          body: JSON.stringify({ newEmail }),
        });
        // 204 for a free address and for one somebody else holds. The screen must not claim a link
        // went to THIS address, because it has no idea — same discipline as the forgot screen.
        state = { ...state, emailChange: { status: "sent", errors: [], newEmail } };
      } catch (error) {
        state = {
          ...state,
          emailChange: { status: "editing", errors: [emailError(error)], newEmail },
        };
      }
      notify("email");
    },
  };
}

function passwordError(error) {
  const reason = error?.status === 400 ? error?.body?.error : null;
  if (reason === "password") return "That password is too short. Use at least 12 characters.";
  if (reason === "current") return "That isn't your current password.";
  // 401 is either a session that ended or an account locked by five wrong attempts, and the
  // endpoint deliberately does not say which. One message that is true of both, and no bounce to
  // the login screen: a locked-out user's cookie still works, so signing them out would be a lie
  // that also loses whatever they had typed.
  if (error?.status === 401) {
    return "We can't change your password right now. After five wrong attempts an account is "
      + "locked for fifteen minutes — wait, then try again.";
  }
  return "Something went wrong. Please try again.";
}

function emailError(error) {
  const reason = error?.status === 400 ? error?.body?.error : null;
  if (reason === "same") return "That's already your sign-in address.";
  if (error?.status === 400) return "That doesn't look like an email address.";
  if (error?.status === 401) return "We can't change your address right now. Please sign in again.";
  return "Something went wrong. Please try again.";
}
```

- [ ] **Step 2: Write the view**

`Wend.Api/wwwroot/js/auth/account/view.js`:

```js
import { escapeHtml } from "../../escape.js";

// Renders the Account screen. The shell is built once; each form's body is replaced on its own.
// That is what makes the two-form rules structural: a password submit physically cannot clear the
// email form's error, steal its focus, or wipe what was typed into it.
//
// Both forms reuse .auth-form and .field-hint — this is the same form shape the auth screens use,
// and a second copy of that CSS would be a second source of truth for the same spacing.
export function createAccountView(root) {
  let h = {};

  function render(state) {
    root.innerHTML = `
      <div class="account-view">
        <button class="back-link" data-action="back">← Settings</button>
        <h2 class="account-heading" tabindex="-1">Account</h2>
        ${state.email ? `
        <p class="account-address">Signed in as <strong>${escapeHtml(state.email)}</strong></p>` : ""}

        <section class="account-section" aria-labelledby="account-password-heading">
          <h3 class="account-section-heading" id="account-password-heading" tabindex="-1">Change password</h3>
          <div class="account-password-body"></div>
        </section>

        <section class="account-section" aria-labelledby="account-email-heading">
          <h3 class="account-section-heading" id="account-email-heading" tabindex="-1">Change sign-in address</h3>
          <div class="account-email-body"></div>
        </section>
      </div>`;
    renderPassword(state.password);
    renderEmail(state.emailChange);
  }

  function renderPassword(password) {
    const body = root.querySelector(".account-password-body");
    if (!body) return;
    const errors = password.errors ?? [];
    body.innerHTML = `
      ${password.status === "done" ? `
      <div class="account-password-done alert alert-success" tabindex="-1">
        <p>Your password has been changed. Your other devices have been signed out.</p>
      </div>` : ""}
      ${errors.length ? `
      <div class="account-password-errors alert alert-danger" tabindex="-1">
        <p>${escapeHtml(errors[0])}</p>
      </div>` : ""}
      <form class="auth-form" data-action="change-password">
        <label for="account-current-password">Current password</label>
        <input class="input" id="account-current-password" name="currentPassword" type="password"
          autocomplete="current-password" required />

        <label for="account-new-password">New password</label>
        <!-- minlength mirrors the server's policy so the browser gives native, per-field,
             accessible feedback before the request goes out. -->
        <input class="input" id="account-new-password" name="newPassword" type="password"
          autocomplete="new-password" minlength="12" required
          aria-describedby="hint-account-new-password" />
        <p class="field-hint" id="hint-account-new-password">At least 12 characters. A memorable phrase beats a short tangle of symbols.</p>

        <!-- .btn carries the design system's min-height: 2.75rem. A bare <button> is 28px. -->
        <button type="submit" class="btn btn-primary" data-role="change-password">Change password</button>
      </form>`;
  }

  function renderEmail(emailChange) {
    const body = root.querySelector(".account-email-body");
    if (!body) return;
    const errors = emailChange.errors ?? [];
    body.innerHTML = `
      ${emailChange.status === "sent" ? `
      <div class="account-email-sent alert alert-success" tabindex="-1">
        <p>If that address is free, we've sent it a confirmation link. It lasts one hour, and your
          sign-in address doesn't change until you open it.</p>
      </div>` : ""}
      ${errors.length ? `
      <div class="account-email-errors alert alert-danger" tabindex="-1">
        <p>${escapeHtml(errors[0])}</p>
      </div>` : ""}
      <form class="auth-form" data-action="change-email">
        <label for="account-new-email">New email</label>
        <input class="input" id="account-new-email" name="newEmail" type="email"
          autocomplete="email" maxlength="254" required
          value="${escapeHtml(emailChange.newEmail ?? "")}"
          aria-describedby="hint-account-new-email" />
        <p class="field-hint" id="hint-account-new-email">You'll sign in with this address once you've opened the link we send there.</p>

        <button type="submit" class="btn btn-primary" data-role="change-email">Send the link</button>
      </form>`;
  }

  // Written long-hand throughout: `el?.focus() ?? fallback()` looks equivalent and is not —
  // focus() returns undefined, so the fallback would fire every time and drag focus off the thing
  // it had just landed on.
  function focusHeading() { root.querySelector(".account-heading")?.focus(); }

  function focusPasswordOutcome() {
    const done = root.querySelector(".account-password-done");
    if (done) { done.focus(); return; }
    const error = root.querySelector(".account-password-errors");
    if (error) { error.focus(); return; }
    // Still inside the submitting form's section — never <body>, and never the other form.
    root.querySelector("#account-password-heading")?.focus();
  }

  function focusEmailOutcome() {
    const sent = root.querySelector(".account-email-sent");
    if (sent) { sent.focus(); return; }
    const error = root.querySelector(".account-email-errors");
    if (error) { error.focus(); return; }
    root.querySelector("#account-email-heading")?.focus();
  }

  function setPasswordBusy(busy) {
    const button = root.querySelector('[data-role="change-password"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Changing…" : "Change password";
  }

  function setEmailBusy(busy) {
    const button = root.querySelector('[data-role="change-email"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Sending…" : "Send the link";
  }

  // Delegated on root, so it survives either section's body being replaced.
  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-action="back"]')) h.back();
    });
    root.addEventListener("submit", (e) => {
      const form = e.target.closest("form[data-action]");
      if (!form) return;
      e.preventDefault();
      const data = new FormData(form);
      if (form.dataset.action === "change-password") {
        h.changePassword({
          currentPassword: data.get("currentPassword") ?? "",
          newPassword: data.get("newPassword") ?? "",
        });
      } else if (form.dataset.action === "change-email") {
        h.changeEmail({ newEmail: data.get("newEmail") ?? "" });
      }
    });
  }

  return {
    render, renderPassword, renderEmail, focusHeading,
    focusPasswordOutcome, focusEmailOutcome, setPasswordBusy, setEmailBusy, bindActions,
  };
}
```

- [ ] **Step 3: Write the controller**

`Wend.Api/wwwroot/js/auth/account/controller.js`:

```js
// Wires the Account screen. Each half repaints, focuses and announces on its own — the `what` the
// model passes is what keeps one form's outcome out of the other form's business.
export function createAccountController(model, view, announce, { onBack } = {}) {
  view.bindActions({
    back: () => onBack?.(),
    changePassword: (fields) => model.changePassword(fields),
    changeEmail: (fields) => model.changeEmail(fields),
  });

  model.subscribe((state, what) => {
    if (what === "all") {
      view.render(state);
      // Every whole-shell render refocuses the heading, not just the first. There are exactly two
      // (mount, then load resolving), and the second rebuilds the element the first focused — so
      // without this, focus lands on <body> a moment after arriving.
      view.focusHeading();
      return;
    }

    if (what === "password") {
      if (state.password.status === "sending") {
        view.setPasswordBusy(true);
        announce("Changing your password…");
        return;
      }
      // Repaints the password section ONLY. Success clears both password fields, which is why
      // this repaints at all — and why focus has to be placed deliberately afterwards.
      view.renderPassword(state.password);
      view.setPasswordBusy(false);
      view.focusPasswordOutcome();
      if (state.password.status === "done") {
        announce("Your password has been changed. Your other devices have been signed out.");
      } else if (state.password.errors?.length) {
        announce(state.password.errors[0]);
      }
      return;
    }

    if (what === "email") {
      if (state.emailChange.status === "sending") {
        view.setEmailBusy(true);
        announce("Sending the confirmation link…");
        return;
      }
      view.renderEmail(state.emailChange);
      // Re-enabled on success too: somebody who mistyped the new address learns nothing from the
      // response, so retrying must cost nothing but typing.
      view.setEmailBusy(false);
      view.focusEmailOutcome();
      if (state.emailChange.status === "sent") {
        announce("If that address is free, we've sent it a confirmation link. Check that inbox.");
      } else if (state.emailChange.errors?.length) {
        announce(state.emailChange.errors[0]);
      }
    }
  });
}
```

- [ ] **Step 4: Wire it into `main.js`**

Add the imports beside the other auth ones (after the reset trio, currently lines 28-30):

```js
import { createAccountModel } from "./auth/account/model.js";
import { createAccountView } from "./auth/account/view.js";
import { createAccountController } from "./auth/account/controller.js";
```

Add `showAccount` directly after `showSettings` (currently ends at line 146), and give
`createSettingsController` its second callback:

```js
function showSettings() {
  mount((root) => {
    const model = createSettingsModel();
    const view = createSettingsView(root);
    createSettingsController(model, view, announce, {
      onBack: () => showOverview(null, true),
      onAccount: showAccount,
    });
    view.focusHeading(); // house pattern: mounting focuses the screen's heading
  });
}

// No route, on purpose. Settings has none either, so this matches the one precedent that exists —
// and it keeps every route in boot()'s switch anonymous, which is what stops the next person
// adding an authenticated one to it by pattern-match and shipping a screen that renders for a
// signed-out visitor and 401s on first use. The cost is no deep link, and a refresh landing on the
// board overview: the same trade Settings already makes.
function showAccount() {
  mount((root) => {
    const model = createAccountModel();
    const view = createAccountView(root);
    createAccountController(model, view, announce, { onBack: showSettings });
    model.load().catch(reportLoadFailure);
  });
}
```

- [ ] **Step 5: Add the link on Settings**

In `Wend.Api/wwwroot/js/settings/view.js`, inside `.settings-view`, immediately after the
`<h2 class="settings-heading">` line:

```html
        <!-- A button, not a link: the Account screen has no URL, exactly as this screen has none.
             .btn carries the 44px floor a bare <button> does not. -->
        <p class="setting-row">
          <button type="button" class="btn btn-ghost" data-action="account">Account</button>
        </p>
```

And in the same file's `bindActions` click listener, beside the existing `back` branch:

```js
      if (e.target.closest('[data-action="account"]')) h.account();
```

In `Wend.Api/wwwroot/js/settings/controller.js`, widen the signature and forward it:

```js
export function createSettingsController(model, view, announce, { onBack, onAccount } = {}) {
  view.bindActions({
    back: () => onBack?.(),
    account: () => onAccount?.(),
```

- [ ] **Step 6: Add the CSS**

Append to `Wend.Api/wwwroot/css/app.css`, after the auth block. Mobile-first, and every custom
property is a real design-system token:

```css
/* Account screen. Reuses .auth-form for both forms — same shape, and a second copy of that
   spacing would be a second source of truth. Only the outer layout and the section rule are new. */
.account-view {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-4);
}

.account-address {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--text-muted);
}

/* The rule is what tells a sighted user the two forms are separate things — the aria-labelledby
   on each <section> is what tells everyone else. --border, not --control-border: this is a
   decorative divider, and --control-border exists to carry the 3:1 boundary a CONTROL owes
   (SC 1.4.11). Borrowing it here would make the token mean two things. */
.account-section {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  padding-block-start: var(--space-4);
  border-block-start: 1px solid var(--border);
}

.account-section-heading {
  margin: 0;
}

@media (min-width: 768px) {
  .account-view {
    max-width: 32rem;
    margin-inline: auto;
  }
}
```

- [ ] **Step 7: Walk it in a real browser**

Start PostgreSQL, then `dotnet run --project Wend.Api` with
`$env:ASPNETCORE_ENVIRONMENT = "Development"` set first, and open `http://127.0.0.1:5174`.
**Hard-reload every time** (Ctrl+Shift+R) — `UseStaticFiles` sends no `Cache-Control`, so a normal
reload serves stale ES modules.

- [ ] Sign in, open Settings, click **Account**. The heading is focused on arrival, and the screen
  names the address you signed in with.
- [ ] **Both forms' error regions are independent, checked in BOTH directions.** Submit the password
  form with a wrong current password; leave the error showing. Now submit the email form with your
  own address. The password error must still be on screen, unchanged, and focus must land in the
  **email** section. Then repeat the other way round.
- [ ] **Nothing typed in one form is lost when the other submits.** Type a new address, do not
  submit it; submit the password form. The address must still be in the field.
- [ ] **`minlength="12"` with real key events**, never a scripted `.value` assignment — `tooShort`
  only fires on a user-edited value, so a scripted check records a false pass. Type eleven
  characters and submit: the browser's own message, on the field.
- [ ] **A successful change-password leaves you on the Account screen.** This is the one manual
  check that catches a missing `RefreshSignInAsync`; if it is absent you are bounced to sign-in.
  Both password fields are cleared, and focus is on the success message inside the password section.
- [ ] **Focus never lands on `<body>`** after any outcome. Check with the keyboard: after each
  submit, press Tab once and confirm you move to the next control *inside that form's section*.
- [ ] **Each form is identifiable by screen reader** — two `<h3>`s, each `<section>` associated with
  its own via `aria-labelledby`.
- [ ] **Every control is 44×44.** Both submit buttons, both text inputs, the Account button on
  Settings, and the back link.
- [ ] **At ~375px wide**, nothing overflows horizontally and the two sections stack.
- [ ] **In light theme as well as dark** — the section rule uses `--border`, which is defined in
  both blocks of `tokens/colors.css`.

- [ ] **Step 8: Commit**

```bash
git add Wend.Api/wwwroot/js/auth/account Wend.Api/wwwroot/js/main.js Wend.Api/wwwroot/js/settings Wend.Api/wwwroot/css/app.css
git commit -m "Add the Account screen with independent password and email forms"
```

---

### Task 7: The `/confirm-email-change` screen

**Files:**
- Create: `Wend.Api/wwwroot/js/auth/confirm-email/model.js`
- Create: `Wend.Api/wwwroot/js/auth/confirm-email/view.js`
- Create: `Wend.Api/wwwroot/js/auth/confirm-email/controller.js`
- Modify: `Wend.Api/wwwroot/js/main.js` (three imports, `showConfirmEmailChange`, one route)

**Interfaces:**
- Consumes: `POST /api/auth/confirm-email-change` (Task 4).
- Produces: `createConfirmEmailModel()`, `createConfirmEmailView(root)`,
  `createConfirmEmailController(model, view, announce, { userId, newEmail, code })`;
  `showConfirmEmailChange()` and the `/confirm-email-change` case in `boot()`.

**No automated tests.** Total stays **291**.

**The one security rule this screen must not break.** `newEmail` arrives from a query string on an
anonymous page anybody can link to, and every view here renders through a template literal into
`innerHTML`. So it follows Plan 5's rule exactly: **the query-string values live in the controller's
closure, are never handed to the view, and are never rendered.** The success state renders the
`email` field from the **`200` response body** instead, escaped — which is the only reason that
endpoint returns a body at all.

- [ ] **Step 1: Write the model**

`Wend.Api/wwwroot/js/auth/confirm-email/model.js`:

```js
import { api } from "../../api.js";

// Maps the endpoint's three status codes onto the states the screen renders. The token trio is NOT
// held here — the controller owns it and passes it on the one submit, so it can never reach the
// view and never reach the DOM.
export function createConfirmEmailModel() {
  let state = { status: "checking" };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    // Arrived with nothing to confirm — a reload, a bookmark, or a back-navigation after
    // replaceState stripped the query string. Deliberately NOT routed through confirm(), which
    // would post empty values, collect a 400, and tell the user their link expired when they never
    // presented one.
    noLink() {
      state = { status: "nolink" };
      notify();
    },
    async confirm({ userId, newEmail, code }) {
      try {
        const body = await api("/api/auth/confirm-email-change", {
          method: "POST",
          body: JSON.stringify({ userId, newEmail, code }),
        });
        // The address comes from the RESPONSE, never from the query string the controller is
        // holding: the response value is what the database actually stores, and the query-string
        // value is a caller-controlled string on a page anybody can link to.
        state = { status: "done", email: body?.email ?? "" };
      } catch (error) {
        if (error?.status === 409) state = { status: "taken" };
        else if (error?.status === 400) state = { status: "expired" };
        else state = { status: "failed" };
      }
      notify();
    },
  };
}
```

- [ ] **Step 2: Write the view**

`Wend.Api/wwwroot/js/auth/confirm-email/view.js`:

```js
import { escapeHtml } from "../../escape.js";

// Renders the five states. Every one is a real screen with a heading — never a raw error.
//
// This view is never given userId, newEmail or code, and must never be: they come off the query
// string of an anonymous page anybody can link to, and everything here goes through a template
// literal into innerHTML. The success heading interpolates the address the SERVER returned, which
// is a stored value, and escapes it anyway.
export function createConfirmEmailView(root) {
  const BODIES = {
    checking: `
      <h2 class="auth-heading" tabindex="-1">Confirming your new address…</h2>
      <p>One moment.</p>`,
    // A link with no parameters is not a broken one — most often it is a reload after confirming.
    nolink: `
      <h2 class="auth-heading" tabindex="-1">Nothing to confirm</h2>
      <p>Open the link from your email to finish changing your address. Links last one hour.</p>
      <p class="auth-links"><a href="/login">Back to sign in</a>.</p>`,
    expired: `
      <h2 class="auth-heading" tabindex="-1">This link has expired or was already used</h2>
      <p>Links last one hour and each one works once. Sign in and start the change again from
        Settings → Account.</p>
      <p class="auth-links"><a href="/login">Sign in</a>.</p>`,
    taken: `
      <h2 class="auth-heading" tabindex="-1">That address is now in use</h2>
      <p>Somebody claimed it after you asked for this link. Sign in and try a different address from
        Settings → Account.</p>
      <p class="auth-links"><a href="/login">Sign in</a>.</p>`,
    failed: `
      <h2 class="auth-heading" tabindex="-1">Something went wrong</h2>
      <p>We couldn't change your address just now. Sign in and try again from Settings → Account.</p>
      <p class="auth-links"><a href="/login">Sign in</a>.</p>`,
  };

  function render(state) {
    const body = state.status === "done"
      ? `
        <h2 class="auth-heading" tabindex="-1">Your sign-in address is now ${escapeHtml(state.email ?? "")}</h2>
        <p>Use it the next time you sign in. Every session you had open has been signed out,
          including this one.</p>
        <p class="auth-links"><a href="/login">Sign in</a>.</p>`
      : BODIES[state.status] ?? BODIES.failed;

    root.innerHTML = `<div class="auth-view">${body}</div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  return { render, focusHeading };
}
```

Note there is no `bindActions`: this screen has no controls of its own. Every exit is an ordinary
`<a href="/login">`, which the shell's routing handles on the next page load.

**This screen loads no third-party resources** — no image, no font, no script from another origin.
The same rule `/verify` and `/reset-password` carry, restated rather than inherited silently,
because this screen's URL carries **an email address as well as a token**: a `Referer` leaking to
another origin would disclose personal data on top of a credential. Nothing above adds one; the
rule is here so nobody adds one later.

- [ ] **Step 3: Write the controller**

`Wend.Api/wwwroot/js/auth/confirm-email/controller.js`:

```js
const ANNOUNCEMENTS = {
  checking: "Confirming your new address.",
  nolink: "Nothing to confirm. Open the link from your email.",
  expired: "This link has expired or was already used. Start the change again from Account settings.",
  taken: "That address is now in use. Try a different one from Account settings.",
  failed: "We couldn't change your address. Try again from Account settings.",
};

// Wires the confirm-email-change screen. Owns userId, newEmail and code for the lifetime of the
// screen and passes them to the one request — the view never sees them.
export function createConfirmEmailController(model, view, announce,
  { userId, newEmail, code } = {}) {
  // Settle the no-link case BEFORE subscribing, so arrival renders and announces once instead of
  // flashing "Confirming…" at somebody who presented nothing. Mirrors the verify screen.
  if (!userId || !newEmail || !code) model.noLink();

  model.subscribe((state) => {
    view.render(state);
    // EVERY state moves focus to its heading and says what happened — including "checking". This
    // screen is reached by clicking a link in an email specifically to receive an async result, so
    // the house "first paint does not force focus" rule is wrong here: without this a
    // screen-reader user gets silence, with focus nowhere, until the request settles.
    view.focusHeading();
    announce(state.status === "done"
      ? `Your sign-in address is now ${state.email}. Please sign in.`
      : ANNOUNCEMENTS[state.status] ?? ANNOUNCEMENTS.failed);
  });

  // POSTs on mount, with no button to press. The endpoint is a POST precisely so a mail scanner
  // following the emailed link cannot complete the change, and this shell-plus-JS shape is what
  // makes that true. Same as /verify.
  if (userId && newEmail && code) model.confirm({ userId, newEmail, code });
}
```

- [ ] **Step 4: Wire it into `main.js`**

Imports, beside the account trio:

```js
import { createConfirmEmailModel } from "./auth/confirm-email/model.js";
import { createConfirmEmailView } from "./auth/confirm-email/view.js";
import { createConfirmEmailController } from "./auth/confirm-email/controller.js";
```

The mount function, beside `showVerify`:

```js
function showConfirmEmailChange() {
  hideAppChrome();
  const params = new URLSearchParams(location.search);
  const userId = params.get("userId") ?? "";
  const newEmail = params.get("newEmail") ?? "";
  const code = params.get("code") ?? "";

  // Drop the live token AND the address out of the address bar and the history entry as soon as
  // they are read. They still reach the server in the POST body, but they no longer sit in a URL a
  // user might screenshot, bookmark, or paste into a support chat — and this URL carries personal
  // data on top of a credential, which is one more reason than /verify has. A reload after this
  // point has nothing, which is what the screen's no-link state is for.
  history.replaceState(null, "", "/confirm-email-change");

  mount((root) => {
    const model = createConfirmEmailModel();
    const view = createConfirmEmailView(root);
    createConfirmEmailController(model, view, announce, { userId, newEmail, code });
  });
}
```

And **one** new case in `boot()`'s switch — the screen is anonymous, so it belongs ahead of the
gate alongside `/verify`, because an emailed link has to land somewhere regardless of session state:

```js
    case "/confirm-email-change": showConfirmEmailChange(); return;
```

**Do not add a case for the Account screen.** Every route in this switch is anonymous, and that is
the property that stops the next person adding an authenticated one by pattern-match.

- [ ] **Step 5: Walk it in a real browser**

With the app running and PostgreSQL up, request a change from the Account screen, then take the
newest link out of `%LOCALAPPDATA%\Wend\auth-emails.log`. **Hard-reload each time.**

- [ ] **Open the link.** It confirms with no button pressed, the heading names the new address, and
  the heading is focused and announced.
- [ ] **Neither `code` nor `newEmail` appears anywhere in the DOM.** Check with
  `document.body.innerHTML.includes("<the code>")` and the same for the address, in the console,
  after the screen settles. The address in the heading came from the response body — to prove that,
  tamper with the `newEmail` parameter in the URL before opening it: it must render the *expired*
  state, not a heading containing the tampered string.
- [ ] **The address bar reads `/confirm-email-change` with no query string** the moment the screen
  mounts, and Back does not restore it.
- [ ] **Reloading the page** renders the no-link state and sends **no request** (check the Network
  panel).
- [ ] **The old link, opened a second time**, renders the expired state — never a raw error.
- [ ] **You are signed out.** Navigate to `/` afterwards: the gate must land you on sign-in, and
  signing in with the **new** address must work while the old one is refused.
- [ ] **The header chrome is hidden** on this screen — no Settings, no Sign out, and neither is in
  the tab order. Tab from the top of the page and confirm the skip link comes first.
- [ ] **~375px and light theme**, as on every other auth screen.

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/wwwroot/js/auth/confirm-email Wend.Api/wwwroot/js/main.js
git commit -m "Add the confirm-email-change screen"
```

---

### Task 8: Docs and the PR

**Files:**
- Modify: none in the repo — this task produces the PR body.

**The backlog work is already done.** The timing, rate-limiting and log-exclusion entries were
extended for Plan 6 on 2026-08-13, along with the stolen-session entry from the stress test.
Verified against `docs/backlog.md` on 2026-08-31: the entries are at *Register leaks account
existence through timing*, *`/api/auth/*` is not rate limited*, *Verify and reset tokens travel in a
query string*, and *A stolen session is a full account takeover, because change-email needs no
password*. **Read them rather than re-deriving; nothing in `backlog.md` is waiting on this plan, and
nothing in this plan may quietly close the stolen-session item** — it is Malin's deferral and a
Plan 8 launch gate.

- [ ] **Step 1: Confirm the suite and the tree**

```bash
dotnet test
git status --short
```

Expected: **291 passed**, and a clean tree apart from what the branch intends.

- [ ] **Step 2: Open the PR**

Body must contain, in this order:

1. **What it adds** — the three endpoints, the token provider, the two screens.
2. **The nine deviations**, copied from *Deviations to record in the PR body* at the top of this
   plan, each with its one-line reason.
3. **The four load-bearing findings and the test that catches each**, so the reviewer can check them
   without re-deriving the design:
   - the `UserName` desync → `Registering_the_old_address_afterwards_still_works`;
   - lockout accounting on change-password →
     `Five_wrong_current_passwords_lock_the_account` and
     `A_locked_out_account_is_refused_without_its_password_being_checked`;
   - the acting session surviving while others die →
     `The_acting_session_survives_a_password_change_while_another_one_dies`;
   - remember-me surviving the cookie reissue →
     `A_remembered_session_is_still_remembered_after_a_password_change`.
4. **The manual walk**, stating which of Task 6 Step 7 and Task 7 Step 5 were actually done and
   which were not. Say it plainly either way — an unwalked check reported as walked is worse than
   an unwalked check.
5. **Test count: 257 → 291.**
6. A pointer to
   [`docs/2026-08-13-wend-review-guide.md`](../2026-08-13-wend-review-guide.md) § *Plan 6 review
   checklist*.

**No AI attribution.** No `Co-Authored-By`, no "Generated with" trailer.

- [ ] **Step 3: Get CI green before asking for review**

`ci.yml` only fires on PRs into `main`. If the check has not appeared, `gh pr update-branch` both
fixes a `BEHIND` branch and fires it; `gh workflow run ci.yml --ref <branch>` is the pre-retarget
fallback.

- [ ] **Step 4: Hand it to Henry**

Squash-merge is the default — this branch is single-author. Merge-not-squash only if Henry pushes
commits onto it, where squashing would attach a co-author trailer. After the merge, confirm the
remote branch actually deleted (the auto-delete has silently no-op'd once), and delete the local
one with `git branch -D` — after a squash-merge git cannot see the branch as merged, so `-d`
refuses.

---

## Done when

- [ ] `dotnet test` reports **291 passed**, 0 failed.
- [ ] Commenting out `SetUserNameAsync` in `/confirm-email-change` fails
      `Registering_the_old_address_afterwards_still_works` **and nothing else** (Task 4, Step 5).
- [ ] Commenting out `RefreshSignInAsync` in `/change-password` fails
      `The_acting_session_survives_a_password_change_while_another_one_dies` **and nothing else**
      (Task 5, Step 3).
- [ ] A signed-in user can change their password from Settings → Account, stays on the screen
      afterwards, and their other devices are signed out.
- [ ] A signed-in user can change their sign-in address, confirms it from the emailed link, and
      afterwards signs in with the new address and not the old one.
- [ ] Registering the abandoned address afterwards sends a confirmation mail.
- [ ] The old address receives exactly one notice, naming the new address, on success only.
- [ ] Neither `code` nor `newEmail` is in the DOM or the address bar after
      `/confirm-email-change` mounts, and reloading it sends no request.
- [ ] Both Account forms' error regions are independent in both directions, and focus lands inside
      the submitting form on every outcome — never on `<body>`.
- [ ] Every new control measures at least 44×44, at ~375px and at desktop width, in both themes.
- [ ] `docs/backlog.md` is unchanged, and the stolen-session item is still open.
- [ ] The PR body carries all nine deviations, the four findings with their tests, and an honest
      account of which manual checks were walked.

---

*Written 2026-08-31 against the tree at `e6bda61`, with all eight of the design's open items
verified against `dotnet/aspnetcore` `release/10.0` first. Three of those answers changed the code:
`ChangeEmailAsync` does distinguish `DuplicateEmail` from `InvalidToken`, so the 409/400 split works
off the error code; `SetUserNameAsync` rotates the security stamp a second time, so the same-instance
requirement is real; and `RefreshSignInAsync` returns no result and is a silent no-op under the Test
auth scheme, which is why Task 5 exists as its own real-cookie suite rather than folding into Task 2.*
