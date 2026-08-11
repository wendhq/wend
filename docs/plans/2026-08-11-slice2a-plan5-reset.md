# Slice 2a Plan 5 — Forgot & reset password: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** A user who has forgotten their password can request an emailed link and set a new one,
which evicts every live session for that account and clears any lockout.

**Architecture:** Two anonymous minimal-API endpoints (`/api/auth/forgot-password`,
`/api/auth/reset-password`) added to the existing `AuthEndpoints` group, a password-reset token
provider with its own one-hour lifespan, a second method on the `IAuthEmailSender` seam, and two
hand-authored vanilla-JS MVC screens mounted by `main.js` on their own routes. Nothing is
restructured — every piece mirrors a shape Plan 3 or Plan 4 already established.

**Tech stack:** .NET 10, ASP.NET Core minimal APIs, ASP.NET Core Identity (`AddIdentityCore` +
cookie auth), EF Core 10 on PostgreSQL 17, NUnit 4 with `WebApplicationFactory`, vanilla ES modules
with no build step.

**Design spec:** [`docs/2026-08-11-wend-slice2a-plan5-reset-design.md`](../2026-08-11-wend-slice2a-plan5-reset-design.md)
— stress-tested, nine fixes folded in. Read it before Task 1.

## Global Constraints

- **Start PostgreSQL before anything:** `Start-Service postgresql-x64-17`. The service is Manual
  start, so connection refused and EF timeouts mean it is stopped, not a code bug.
- **Kill the app before any build or test run:** the process is `Wend.Api`, **not** `Wend`. A
  running instance holds the DLL and produces MSB3021/3027, which is a copy lock, not a test failure.
- **Test baseline is 231 green.** Check the running total against each task's stated total. A
  paste-driven edit once silently dropped three tests behind a green suite.
- **No AI attribution** in commit messages — no `Co-Authored-By`, no "Generated with" trailer.
- **`design-system/` is read-only.** No task here needs a change in it; every class used already
  exists (`.btn`, `.btn-primary`, `.alert`, `.alert-danger`, `.alert-success`).
- **`escapeHtml` at every user-content interpolation**, `#status` stays outside `#app`, and focus
  never drops to `<body>`.
- **Password policy is 12 characters, no composition rules** (`Program.cs:47-51`). Test passwords
  use `"correct horse battery staple"`, as every existing auth test does.
- **Emailed links are built from `Wend:PublicBaseUrl`**, never from `http.Host`.
- **Logging carries error codes only** — never an address, never a token, never a password.
- **Every browser check hard-reloads** (Ctrl+Shift+R or `cache: 'reload'`): `UseStaticFiles` sends
  no `Cache-Control`, so ES-module imports are served stale otherwise.

---

## File structure

**Created**

| File | Responsibility |
|---|---|
| `Wend.Api/PasswordResetTokenProvider.cs` | A `DataProtectorTokenProvider` subclass plus its options, so reset tokens carry a one-hour lifespan independent of every other Identity token. |
| `Wend.Tests/AuthForgotTests.cs` | `/api/auth/forgot-password` — which mail goes out, and that every outcome looks identical. |
| `Wend.Tests/AuthResetTests.cs` | `/api/auth/reset-password` — token validity, password policy, lockout clearing. |
| `Wend.Tests/RealCookieResetTests.cs` | The browser-shaped walk on the genuine cookie scheme, opening with the test-scheme canary. |
| `Wend.Api/wwwroot/js/auth/forgot/{model,view,controller}.js` | The request-a-link screen. |
| `Wend.Api/wwwroot/js/auth/reset/{model,view,controller}.js` | The set-a-new-password screen. |

**Modified**

| File | Change |
|---|---|
| `Wend.Core/IAuthEmailSender.cs` | `SendPasswordResetAsync` on the seam. |
| `Wend.Api/FileAuthEmailSender.cs` | Implements it. |
| `Wend.Tests/FakeAuthEmailSender.cs` | Implements it and records message *kind*. |
| `Wend.Api/Program.cs` | Registers the reset provider and points `options.Tokens` at it. |
| `Wend.Api/AuthEndpoints.cs` | Two handlers, one link-builder, two request records. |
| `Wend.Tests/AuthConfigurationTests.cs` | Asserts both token lifespans and both provider names. |
| `Wend.Api/wwwroot/js/api.js` | Attaches the parsed error body, so a caller can tell two 400s apart. |
| `Wend.Api/wwwroot/js/main.js` | Two imports, two mount functions, two routes. |
| `Wend.Api/wwwroot/js/auth/login/view.js` | A permanent forgot-password link, and the help block's dead line becomes a real one. |
| `docs/backlog.md` | Three entries: the new email-bomb vector, reset tokens in query strings, the forgot-password timing side channel. |

**Test totals:** 231 baseline → 232 (T1) → 239 (T2) → 249 (T3) → **253** (T4). Tasks 5–7 add no
automated tests. The spec estimated 25–30; the pinned total is **22**, because the spec counted the
lockout-clear and older-token properties twice — once as state and once as behaviour — and the plan
keeps both but as one test each per level.

---

### Task 1: Reset token provider and the email seam

**Files:**
- Create: `Wend.Api/PasswordResetTokenProvider.cs`
- Modify: `Wend.Core/IAuthEmailSender.cs`
- Modify: `Wend.Api/FileAuthEmailSender.cs`
- Modify: `Wend.Tests/FakeAuthEmailSender.cs`
- Modify: `Wend.Api/Program.cs:68-76`
- Test: `Wend.Tests/AuthConfigurationTests.cs`

**Interfaces:**
- Consumes: `EmailConfirmationTokenProvider<TUser>` and its options as the shape to mirror.
- Produces: `PasswordResetTokenProvider<WendUser>`, `PasswordResetTokenProviderOptions` (name
  `"WendPasswordReset"`, lifespan 1 hour); `IAuthEmailSender.SendPasswordResetAsync(string email,
  string link)`; `FakeAuthEmailSender.Sent` becomes
  `List<(string Email, string Link, string Kind)>` with `Kind` of `"confirm"` or `"reset"`.

- [ ] **Step 1: Write the failing test**

Add to `Wend.Tests/AuthConfigurationTests.cs`, after `The_security_stamp_is_revalidated_on_every_request`:

```csharp
    [Test]
    public void Reset_tokens_last_an_hour_and_confirmation_tokens_still_last_a_day()
    {
        var identity = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var confirmation = _factory.Services
            .GetRequiredService<IOptions<EmailConfirmationTokenProviderOptions>>().Value;
        var reset = _factory.Services
            .GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(reset.TokenLifespan, Is.EqualTo(TimeSpan.FromHours(1)));
            // Both, in one test, on purpose: the failure this pairing exists to prevent is a
            // change to either lifespan silently dragging the other with it.
            Assert.That(confirmation.TokenLifespan, Is.EqualTo(TimeSpan.FromHours(24)));
            Assert.That(identity.Tokens.PasswordResetTokenProvider, Is.EqualTo("WendPasswordReset"));
            Assert.That(identity.Tokens.EmailConfirmationTokenProvider,
                Is.EqualTo("WendEmailConfirmation"));
        });
    }
```

The file does **not** currently have `using Wend.Api;` — add it alongside the existing usings, since
`EmailConfirmationTokenProviderOptions` and the new options class both live in that namespace.

Note the assertion blocks in the new tests below use `Assert.MultipleAsync` wherever the lambda is
`async`. `Assert.Multiple` takes a sync delegate, so an `async` lambda binds as `async void` and its
awaited assertions can land after the block has already closed.

- [ ] **Step 2: Run the test to verify it fails**

```bash
Start-Service postgresql-x64-17; Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test --filter "FullyQualifiedName~AuthConfigurationTests"
```

Expected: FAIL to compile — `PasswordResetTokenProviderOptions` does not exist.

- [ ] **Step 3: Create the provider**

`Wend.Api/PasswordResetTokenProvider.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wend.Api;

/// <summary>
/// Password-reset tokens with their own lifespan. The mirror image of
/// EmailConfirmationTokenProvider, and the reason that class exists: without one provider per token
/// type, the global DataProtectionTokenProviderOptions governs both, and the hour a reset wants
/// would silently become the lifespan of every confirmation link too.
/// </summary>
public class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;

public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        Name = "WendPasswordResetTokenProvider";
        // One hour. A reset link is the most powerful string Wend sends by email, and the screen
        // that greets an expired one offers a replacement in a click.
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
```

- [ ] **Step 4: Register it**

In `Wend.Api/Program.cs`, inside the `AddIdentityCore` options lambda, directly after the two
existing `options.Tokens` lines (currently `Program.cs:68-70`):

```csharp
        options.Tokens.ProviderMap.Add("WendPasswordReset",
            new TokenProviderDescriptor(typeof(PasswordResetTokenProvider<WendUser>)));
        options.Tokens.PasswordResetTokenProvider = "WendPasswordReset";
```

And directly after the existing `builder.Services.AddTransient<EmailConfirmationTokenProvider<WendUser>>();`
(currently `Program.cs:76`):

```csharp
builder.Services.AddTransient<PasswordResetTokenProvider<WendUser>>();
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test --filter "FullyQualifiedName~AuthConfigurationTests"
```

Expected: PASS, 6 tests.

- [ ] **Step 6: Add the seam method**

`Wend.Core/IAuthEmailSender.cs` — add inside the interface, below the existing method:

```csharp
    Task SendPasswordResetAsync(string email, string link);
```

`Wend.Api/FileAuthEmailSender.cs` — add below `SendEmailConfirmationAsync`:

```csharp
    public async Task SendPasswordResetAsync(string email, string link)
    {
        var entry = $"[{DateTime.UtcNow:u}] reset {email}{Environment.NewLine}  {link}{Environment.NewLine}";
        await File.AppendAllTextAsync(path, entry);
        Console.WriteLine(entry);
    }
```

`Wend.Tests/FakeAuthEmailSender.cs` — replace the whole class body:

```csharp
/// <summary>Captures what would have been emailed, so tests can assert on links and on silence.</summary>
public sealed class FakeAuthEmailSender : IAuthEmailSender
{
    // Kind, not just the address: several Plan 5 tests turn on WHICH mail went out — a reset
    // request against an unconfirmed account must produce a confirmation link and no reset link.
    public List<(string Email, string Link, string Kind)> Sent { get; } = [];

    public Task SendEmailConfirmationAsync(string email, string link)
    {
        Sent.Add((email, link, "confirm"));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string link)
    {
        Sent.Add((email, link, "reset"));
        return Task.CompletedTask;
    }
}
```

Adding a third *named* tuple element is source-compatible: every existing assertion reads
`.Email`, `.Link`, `.Single()`, `.Last()`, `.Clear()` or `Is.Empty`, and none destructures
positionally. Verify with `grep -rn "Email.Sent" Wend.Tests` before moving on.

- [ ] **Step 7: Run the full suite**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test
```

Expected: PASS, **232** tests. If the count is not 232, stop and find the missing test before
committing.

- [ ] **Step 8: Commit**

```bash
git add Wend.Api/PasswordResetTokenProvider.cs Wend.Api/Program.cs Wend.Api/FileAuthEmailSender.cs Wend.Core/IAuthEmailSender.cs Wend.Tests/FakeAuthEmailSender.cs Wend.Tests/AuthConfigurationTests.cs
git commit -m "Give password-reset tokens their own one-hour lifespan"
```

---

### Task 2: `POST /api/auth/forgot-password`

**Files:**
- Modify: `Wend.Api/AuthEndpoints.cs`
- Test: `Wend.Tests/AuthForgotTests.cs` (create)

**Interfaces:**
- Consumes: `IAuthEmailSender.SendPasswordResetAsync` (Task 1); the existing private helpers
  `SendConfirmationAsync` and `BuildConfirmationLinkAsync` in `AuthEndpoints`.
- Produces: `POST /api/auth/forgot-password` taking `ForgotPasswordRequest(string Email)` and always
  answering `204`; a private `BuildResetLinkAsync` returning a link of the form
  `{origin}/reset-password?userId=…&code=…`, which Task 6's screen parses.

- [ ] **Step 1: Write the failing tests**

Create `Wend.Tests/AuthForgotTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/forgot-password answers 204 to everything, so nearly every test here asserts on which
/// mail went out rather than on the response.
/// </summary>
public class AuthForgotTests
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

    private Task Register(string email) =>
        _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });

    private Task<HttpResponseMessage> Forgot(string email) =>
        _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

    private async Task ConfirmDirectly(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        await users.ConfirmEmailAsync(user!, token);
    }

    [Test]
    public async Task A_confirmed_account_is_emailed_exactly_one_reset_link()
    {
        await Register("member@example.test");
        await ConfirmDirectly("member@example.test");
        _factory.Email.Sent.Clear();

        await Forgot("member@example.test");

        var sent = _factory.Email.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Email, Is.EqualTo("member@example.test"));
            Assert.That(sent.Kind, Is.EqualTo("reset"));
            Assert.That(sent.Link, Does.Contain("/reset-password?userId="));
        });
    }

    [Test]
    public async Task An_unconfirmed_account_is_emailed_a_confirmation_link_and_no_reset_link()
    {
        await Register("waiting@example.test");
        _factory.Email.Sent.Clear();

        await Forgot("waiting@example.test");

        // ResetPasswordAsync does not confirm an address and RequireConfirmedAccount still blocks
        // the login afterwards, so a reset link here would succeed and leave the user at the same
        // wall. The confirmation link is the mail that actually unblocks them.
        var sent = _factory.Email.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Kind, Is.EqualTo("confirm"));
            Assert.That(_factory.Email.Sent.Any(m => m.Kind == "reset"), Is.False);
        });
    }

    [Test]
    public async Task An_unknown_address_is_emailed_nothing()
    {
        await Forgot("stranger@example.test");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task A_malformed_address_is_emailed_nothing()
    {
        await Forgot("not-an-email");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task A_locked_out_account_still_gets_its_link()
    {
        await Register("locked@example.test");
        await ConfirmDirectly("locked@example.test");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await _client.PostAsJsonAsync("/api/auth/login",
                new { email = "locked@example.test", password = "wrong wrong wrong wrong" });
        }
        _factory.Email.Sent.Clear();

        await Forgot("locked@example.test");

        // A reset is precisely how a locked-out owner gets back in. Refusing here would only
        // punish the victim of somebody else's guessing.
        Assert.That(_factory.Email.Sent.Single().Kind, Is.EqualTo("reset"));
    }

    [Test]
    public async Task Every_forgot_outcome_returns_the_same_response()
    {
        await Register("waiting@example.test");
        await Register("member@example.test");
        await ConfirmDirectly("member@example.test");

        var confirmed = await Forgot("member@example.test");
        var unconfirmed = await Forgot("waiting@example.test");
        var unknown = await Forgot("stranger@example.test");
        var rubbish = await Forgot("not-an-email");

        Assert.Multiple(() =>
        {
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(unconfirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(rubbish.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task Forgot_password_binds_json_only()
    {
        // The guard on the reasoning that lets antiforgery wait for Plan 8: an HTML form cannot
        // send application/json, and a cross-site fetch that does triggers a preflight there is no
        // CORS policy to satisfy. If someone later adds form binding, this test says so.
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("email", "member@example.test"),
        ]);

        var response = await _client.PostAsync("/api/auth/forgot-password", form);

        // 404 is the measured behaviour for the equivalent login test (AuthSessionTests). If this
        // fails with 415 instead, record the difference in the PR — do not just change the number.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test --filter "FullyQualifiedName~AuthForgotTests"
```

Expected: 7 tests, all failing — the route does not exist, so the `/api/{**path}` catch-all answers
404 where 204 is expected and no mail is ever sent.

- [ ] **Step 3: Add the link builder**

In `Wend.Api/AuthEndpoints.cs`, below the existing `BuildConfirmationLinkAsync`:

```csharp
    /// <summary>
    /// Mints a password-reset token and builds the link to the SPA's /reset-password screen. Same
    /// Base64Url encoding and the same configured origin as the confirmation link: building it from
    /// http.Host would let an attacker who can set that header get Wend to email a victim a
    /// genuine-looking link pointing at the attacker's server, carrying a live reset token.
    /// </summary>
    private static async Task<string> BuildResetLinkAsync(WendUser user,
        UserManager<WendUser> users, HttpRequest http, string? publicBaseUrl)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var origin = publicBaseUrl?.TrimEnd('/') ?? $"{http.Scheme}://{http.Host}";
        return $"{origin}/reset-password" +
               $"?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(code)}";
    }
```

- [ ] **Step 4: Add the handler**

In `Wend.Api/AuthEndpoints.cs`, after the `/resend-verification` mapping and before `/login`:

```csharp
        // Anonymous, and one response for every input — unknown, malformed, unconfirmed and
        // confirmed alike. No 400 branch, for the same reason resend-verification has none: telling
        // a caller their address is malformed is harmless, but a second response shape on an
        // endpoint whose whole job is looking identical from outside is a liability nobody needs.
        group.MapPost("/forgot-password", async (ForgotPasswordRequest req,
            UserManager<WendUser> users, IAuthEmailSender email, HttpRequest http) =>
        {
            var address = req.Email?.Trim() ?? "";
            if (address.Length is 0 or > MaxEmailLength) return Results.NoContent();

            if (await users.FindByEmailAsync(address) is { } user)
            {
                if (!user.EmailConfirmed)
                {
                    // A reset would succeed and leave them unable to log in anyway, with the
                    // enumeration rules forbidding any explanation. Send the mail that unblocks
                    // them instead. No reset token is minted for an unconfirmed account, ever.
                    await SendConfirmationAsync(user, users, email, http, publicBaseUrl);
                }
                else
                {
                    // Locked out falls through here on purpose: a reset is how a locked-out owner
                    // gets back in.
                    var link = await BuildResetLinkAsync(user, users, http, publicBaseUrl);
                    await email.SendPasswordResetAsync(user.Email!, link);
                }
            }

            return Results.NoContent();
        });
```

And at the bottom of the file, beside the other request records:

```csharp
public record ForgotPasswordRequest(string Email);
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test --filter "FullyQualifiedName~AuthForgotTests"
```

Expected: PASS, 7 tests.

- [ ] **Step 6: Run the full suite**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test
```

Expected: PASS, **239** tests.

- [ ] **Step 7: Commit**

```bash
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthForgotTests.cs
git commit -m "Add the forgot-password endpoint"
```

---

### Task 3: `POST /api/auth/reset-password`

**Files:**
- Modify: `Wend.Api/AuthEndpoints.cs`
- Test: `Wend.Tests/AuthResetTests.cs` (create)

**Interfaces:**
- Consumes: `BuildResetLinkAsync` and the reset provider (Tasks 1–2).
- Produces: `POST /api/auth/reset-password` taking
  `ResetPasswordRequest(string UserId, string Code, string Password)`, answering `204`, or `400`
  with a body of `{ "error": "password" }` or `{ "error": "token" }`. Task 6's screen branches on
  exactly those two strings.

- [ ] **Step 1: Write the failing tests**

Create `Wend.Tests/AuthResetTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/reset-password. The two 400 codes are asserted by name because the reset screen
/// branches on them: one tells the user their password is too short, the other tells them their
/// link is dead, and swapping them produces a screen that lies.
/// </summary>
public class AuthResetTests
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

    /// <summary>Registers, confirms, requests a reset, and returns the emailed userId + code.</summary>
    private async Task<(string UserId, string Code)> ArrangeResetLink(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });
        await ConfirmDirectly(email);
        _factory.Email.Sent.Clear();
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        return ReadLink(_factory.Email.Sent.Last().Link);
    }

    private static (string UserId, string Code) ReadLink(string link)
    {
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"]!, query["code"]!);
    }

    private Task<HttpResponseMessage> Reset(string userId, string code, string password) =>
        _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, code, password });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body?.GetValueOrDefault("error");
    }

    private async Task ConfirmDirectly(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        await users.ConfirmEmailAsync(user!, token);
    }

    private async Task<WendUser> Reload(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        return (await users.FindByEmailAsync(email))!;
    }

    private async Task<bool> PasswordWorks(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        return await users.CheckPasswordAsync(user!, password);
    }

    [Test]
    public async Task A_valid_token_sets_the_new_password()
    {
        var (userId, code) = await ArrangeResetLink("member@example.test");

        var response = await Reset(userId, code, NewPassword);

        Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await PasswordWorks("member@example.test", NewPassword), Is.True);
            Assert.That(await PasswordWorks("member@example.test", GoodPassword), Is.False);
        });
    }

    [Test]
    public async Task A_reused_token_is_refused()
    {
        var (userId, code) = await ArrangeResetLink("member@example.test");
        await Reset(userId, code, NewPassword);

        var second = await Reset(userId, code, "yet another long passphrase");

        // Identity's tokens are stamp-bound, and a completed reset rotates the stamp. That is
        // where the single-use guarantee comes from — there is no explicit guard to read.
        Assert.MultipleAsync(async () =>
        {
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(second), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task An_older_token_still_works_after_a_newer_one_is_issued()
    {
        var (userId, older) = await ArrangeResetLink("member@example.test");
        await _client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = "member@example.test" });

        var response = await Reset(userId, older, NewPassword);

        // Requesting a new link revokes nothing: only a COMPLETED reset rotates the stamp. Asking
        // for a fresh link because you think the old one was seen does not kill the old one.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task A_token_minted_for_one_account_cannot_reset_another()
    {
        var (mine, code) = await ArrangeResetLink("mine@example.test");
        var (theirs, _) = await ArrangeResetLink("theirs@example.test");
        Assert.That(mine, Is.Not.EqualTo(theirs));

        var response = await Reset(theirs, code, NewPassword);

        Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
            Assert.That(await PasswordWorks("theirs@example.test", NewPassword), Is.False);
        });
    }

    [Test]
    public async Task A_garbage_code_is_refused()
    {
        var (userId, _) = await ArrangeResetLink("member@example.test");

        var notBase64Url = await Reset(userId, "!!!not base64!!!", NewPassword);
        var wellFormedRubbish = await Reset(userId,
            WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("not a token")), NewPassword);

        Assert.MultipleAsync(async () =>
        {
            Assert.That(notBase64Url.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(notBase64Url), Is.EqualTo("token"));
            Assert.That(wellFormedRubbish.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(wellFormedRubbish), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task An_unknown_user_id_is_refused()
    {
        var (_, code) = await ArrangeResetLink("member@example.test");

        var response = await Reset(Guid.NewGuid().ToString(), code, NewPassword);

        Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task A_weak_password_is_refused_by_code_and_leaves_the_token_usable()
    {
        var (userId, code) = await ArrangeResetLink("member@example.test");

        var weak = await Reset(userId, code, "short");
        var retry = await Reset(userId, code, NewPassword);

        // The whole point of validating policy BEFORE redeeming the token: the user is told which
        // of the two things went wrong, and a rejected attempt does not cost them their link.
        Assert.MultipleAsync(async () =>
        {
            Assert.That(weak.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(weak), Is.EqualTo("password"));
            Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task A_successful_reset_clears_the_lockout()
    {
        var (userId, code) = await ArrangeResetLink("locked@example.test");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await _client.PostAsJsonAsync("/api/auth/login",
                new { email = "locked@example.test", password = "wrong wrong wrong wrong" });
        }
        Assert.That((await Reload("locked@example.test")).LockoutEnd, Is.Not.Null, "arrange failed");

        await Reset(userId, code, NewPassword);

        // Two columns, two calls. Resetting the count alone leaves a live LockoutEnd and the user
        // holds a working password they cannot use for fifteen minutes.
        var user = await Reload("locked@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(user.LockoutEnd, Is.Null);
            Assert.That(user.AccessFailedCount, Is.Zero);
        });
    }

    [Test]
    public async Task Resetting_does_not_confirm_an_unconfirmed_address()
    {
        // Unreachable through forgot-password, which never mints a reset token for an unconfirmed
        // account — so the token is minted directly. This pins the reason that branch exists: a
        // reset is not a second, quieter way through the verification gate.
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "waiting@example.test", password = GoodPassword, displayName = "Malin" });
        string userId, code;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
            var user = (await users.FindByEmailAsync("waiting@example.test"))!;
            userId = user.Id;
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
                await users.GeneratePasswordResetTokenAsync(user)));
        }

        var response = await Reset(userId, code, NewPassword);

        Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That((await Reload("waiting@example.test")).EmailConfirmed, Is.False);
        });
    }

    [Test]
    public async Task Reset_password_binds_json_only()
    {
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("userId", "whoever"),
            new KeyValuePair<string, string>("code", "whatever"),
            new KeyValuePair<string, string>("password", NewPassword),
        ]);

        var response = await _client.PostAsync("/api/auth/reset-password", form);

        // As in AuthForgotTests: 404 is the measured behaviour. A 415 here is a finding, not a
        // number to edit.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test --filter "FullyQualifiedName~AuthResetTests"
```

Expected: 10 tests, all failing — the route does not exist.

- [ ] **Step 3: Add the handler**

In `Wend.Api/AuthEndpoints.cs`, directly after the `/forgot-password` mapping:

```csharp
        // Anonymous. 204, or 400 saying which of the two things went wrong. Neither code mentions
        // an account, so this is not an enumeration surface — it exists because a single 400 for
        // both cases produces a screen that says "this link has expired" when the link is fine and
        // the password was eleven characters.
        group.MapPost("/reset-password", async (ResetPasswordRequest req,
            UserManager<WendUser> users, ILoggerFactory loggerFactory) =>
        {
            var password = req.Password ?? "";

            // Policy first, before any lookup or token work. It depends only on the caller's own
            // input, and checking it here is what lets the screen name the real problem. (A future
            // custom validator that reads the user would have to move below the lookup; none does.)
            var candidate = new WendUser
            {
                UserName = "policy@example.invalid",
                Email = "policy@example.invalid",
            };
            foreach (var validator in users.PasswordValidators)
            {
                if (!(await validator.ValidateAsync(users, candidate, password)).Succeeded)
                    return Results.BadRequest(new { error = "password" });
            }

            if (req.UserId is not { Length: > 0 } id) return Results.BadRequest(new { error = "token" });
            if (await users.FindByIdAsync(id) is not { } user) return Results.BadRequest(new { error = "token" });

            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Code ?? ""));
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "token" });
            }

            // Rotates the security stamp, which is what evicts every live cookie for this user on
            // its next request — SecurityStampValidator runs at TimeSpan.Zero — and kills every
            // outstanding reset token bound to the old stamp. Nothing here says so out loud; the
            // tests are the documentation.
            if (!(await users.ResetPasswordAsync(user, token, password)).Succeeded)
                return Results.BadRequest(new { error = "token" });

            // Both, not one: LockoutEnd and AccessFailedCount are separate columns. The same `user`
            // instance throughout — ResetPasswordAsync refreshed its concurrency stamp, and
            // reloading here would work from a stale one.
            var lockoutCleared = await users.SetLockoutEndDateAsync(user, null);
            var countCleared = await users.ResetAccessFailedCountAsync(user);
            if (!lockoutCleared.Succeeded || !countCleared.Succeeded)
            {
                // Still 204: the password genuinely changed, and sending the user round the loop
                // again would not fix a lockout row. Error CODES only, never the address.
                loggerFactory.CreateLogger("Wend.Api.AuthEndpoints")
                    .LogWarning("Password reset succeeded but the lockout was not cleared: {Errors}",
                        string.Join("; ", lockoutCleared.Errors.Concat(countCleared.Errors)
                            .Select(e => e.Code)));
            }

            return Results.NoContent();
        });
```

And at the bottom of the file:

```csharp
public record ResetPasswordRequest(string UserId, string Code, string Password);
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test --filter "FullyQualifiedName~AuthResetTests"
```

Expected: PASS, 10 tests. If `A_successful_reset_clears_the_lockout` fails on `LockoutEnd`, check
that both calls are present and that neither was replaced by the other.

- [ ] **Step 5: Run the full suite**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test
```

Expected: PASS, **249** tests.

- [ ] **Step 6: Commit**

```bash
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthResetTests.cs
git commit -m "Add the reset-password endpoint"
```

---

### Task 4: The real-cookie walk

**Files:**
- Test: `Wend.Tests/RealCookieResetTests.cs` (create)

**Interfaces:**
- Consumes: everything from Tasks 1–3, plus `new WendApiFactory(useTestAuth: false)`.
- Produces: nothing consumed by later tasks. This is the suite that proves the session-eviction
  promise Plan 4 paid for.

- [ ] **Step 1: Write the failing tests**

Create `Wend.Tests/RealCookieResetTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Wend.Tests;

/// <summary>
/// Password reset on the genuine cookie scheme, no test auth anywhere: the emailed link, the new
/// password, and what happens to sessions that were already live. WebApplicationFactory's client
/// keeps cookies, so the session flows exactly as it does in a browser.
/// </summary>
public class RealCookieResetTests
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
        // useTestAuth: false is authenticated before it does anything, and every assertion below
        // would pass while testing nothing.
        var response = await _client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>Registers and confirms an account over HTTP, the way a browser would.</summary>
    private async Task Arrange(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });
        var link = new Uri(_factory.Email.Sent.Last().Link);
        var query = HttpUtility.ParseQueryString(link.Query);
        await _client.PostAsJsonAsync("/api/auth/verify",
            new { userId = query["userId"], code = query["code"] });
    }

    private async Task<(string UserId, string Code)> RequestReset(string email)
    {
        _factory.Email.Sent.Clear();
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Single().Link).Query);
        return (query["userId"]!, query["code"]!);
    }

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    [Test]
    public async Task Forgot_then_reset_replaces_the_password()
    {
        await Arrange("walker@example.test");
        var (userId, code) = await RequestReset("walker@example.test");

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { userId, code, password = NewPassword });
        var withOld = await Login("walker@example.test", GoodPassword);
        var withNew = await Login("walker@example.test", NewPassword);

        Assert.Multiple(() =>
        {
            Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "reset");
            Assert.That(withOld.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "old password");
            Assert.That(withNew.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "new password");
        });
    }

    [Test]
    public async Task A_live_session_is_refused_on_its_next_request_after_a_reset()
    {
        await Arrange("walker@example.test");
        await Login("walker@example.test", GoodPassword);
        var before = await _client.GetAsync("/api/boards");

        // A second client stands in for the other device — or the attacker holding a stolen
        // session. The reset arrives from somewhere else entirely; this client never re-logs-in.
        using var elsewhere = _factory.CreateClient();
        var (userId, code) = await RequestReset("walker@example.test");
        await elsewhere.PostAsJsonAsync("/api/auth/reset-password",
            new { userId, code, password = NewPassword });

        var after = await _client.GetAsync("/api/boards");

        // This is what Plan 4 bought TimeSpan.Zero for, and the first thing to check it.
        Assert.Multiple(() =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK), "session before the reset");
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "same cookie after");
        });
    }

    [Test]
    public async Task A_locked_out_account_can_sign_in_immediately_after_a_reset()
    {
        await Arrange("locked@example.test");
        for (var attempt = 0; attempt < 5; attempt++)
            await Login("locked@example.test", "wrong wrong wrong wrong");
        var whileLocked = await Login("locked@example.test", GoodPassword);

        var (userId, code) = await RequestReset("locked@example.test");
        await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { userId, code, password = NewPassword });
        var afterReset = await Login("locked@example.test", NewPassword);

        Assert.Multiple(() =>
        {
            Assert.That(whileLocked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "the correct password is refused while locked out");
            Assert.That(afterReset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
                "no fifteen-minute wait after a reset");
        });
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test --filter "FullyQualifiedName~RealCookieResetTests"
```

Expected: PASS, 4 tests — the endpoints already exist, so this suite verifies rather than drives.
If `A_live_session_is_refused_on_its_next_request_after_a_reset` fails, the stamp validation
interval is the suspect: check `Program.cs:130` still sets `TimeSpan.Zero`.

- [ ] **Step 3: Run the full suite**

```bash
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet test
```

Expected: PASS, **253** tests. This is the final automated total for the plan.

- [ ] **Step 4: Commit**

```bash
git add Wend.Tests/RealCookieResetTests.cs
git commit -m "Cover password reset on the real cookie path"
```

---

### Task 5: The forgot-password screen

**Files:**
- Create: `Wend.Api/wwwroot/js/auth/forgot/model.js`, `view.js`, `controller.js`
- Modify: `Wend.Api/wwwroot/js/main.js`
- Modify: `Wend.Api/wwwroot/js/auth/login/view.js`

**Interfaces:**
- Consumes: `POST /api/auth/forgot-password` (Task 2); `api` from `js/api.js`; `escapeHtml` from
  `js/escape.js`.
- Produces: `createForgotModel()`, `createForgotView(root)`, `createForgotController(model, view,
  announce)`; the route `/forgot-password`, which Task 6's expired state links to.

- [ ] **Step 1: Write the model**

`Wend.Api/wwwroot/js/auth/forgot/model.js`:

```js
import { api } from "../../api.js";

// State only: what was submitted, what came back. No DOM, no timers.
export function createForgotModel() {
  let state = { status: "editing", errors: [], email: "" };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    async submit({ email }) {
      state = { status: "sending", errors: [], email };
      notify();
      try {
        await api("/api/auth/forgot-password", {
          method: "POST",
          body: JSON.stringify({ email }),
        });
        // 204 for an address the server knows and one it has never seen. The screen must not claim
        // a link went to THIS address, because it has no idea.
        state = { status: "sent", errors: [], email };
      } catch {
        // The endpoint has no 400 branch, so anything that lands here is a transport failure.
        state = { status: "editing", errors: ["Something went wrong. Please try again."], email };
      }
      notify();
    },
  };
}
```

- [ ] **Step 2: Write the view**

`Wend.Api/wwwroot/js/auth/forgot/view.js`:

```js
import { escapeHtml } from "../../escape.js";

// Renders the forgot-password form and its one success state. No logic; events via data-action.
// The success message does NOT echo the address back as confirmation — the server will not say
// whether it has one, so neither can this screen.
export function createForgotView(root) {
  let h = {};

  function render(state) {
    const errors = state.errors ?? [];
    root.innerHTML = `
      <div class="auth-view">
        <h2 class="auth-heading" tabindex="-1">Reset your password</h2>
        ${state.status === "sent" ? `
        <div class="auth-sent alert alert-success" tabindex="-1">
          <p>If that address has an account, we've sent it a link. The link lasts one hour.</p>
        </div>` : ""}
        ${errors.length ? `
        <div class="auth-errors alert alert-danger" tabindex="-1">
          <p>${escapeHtml(errors[0])}</p>
        </div>` : ""}
        <form class="auth-form" data-action="submit">
          <label for="forgot-email">Email</label>
          <input id="forgot-email" name="email" type="email" autocomplete="email"
            maxlength="254" required value="${escapeHtml(state.email ?? "")}"
            aria-describedby="hint-forgot-email" />
          <p class="field-hint" id="hint-forgot-email">The address you signed up with.</p>

          <!-- .btn carries the design system's min-height: 2.75rem. A bare <button> is 28px. -->
          <button type="submit" class="btn btn-primary" data-role="submit">Send the link</button>
        </form>
        <p class="auth-links">Remembered it? <a href="/login">Sign in</a>.</p>
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  // Written long-hand on purpose: `el?.focus() ?? focusHeading()` looks equivalent and is not —
  // focus() returns undefined, so the fallback would fire every time and drag focus off the
  // message it had just landed on.
  function focusSent() {
    const sent = root.querySelector(".auth-sent");
    if (sent) sent.focus();
    else focusHeading();
  }

  function focusFirstError() {
    const summary = root.querySelector(".auth-errors");
    if (summary) summary.focus();
    else focusHeading();
  }

  function setBusy(busy) {
    const button = root.querySelector('[data-role="submit"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Sending…" : "Send the link";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="submit"]')) return;
      e.preventDefault();
      const data = new FormData(e.target);
      h.submit({ email: data.get("email") ?? "" });
    });
  }

  return { render, focusHeading, focusSent, focusFirstError, setBusy, bindActions };
}
```

The success message renders **above a form that stays usable**, with the submitted address still in
the field. Every response is identical, so a typo is invisible until the inbox stays empty — the fix
has to cost nothing but editing one character.

- [ ] **Step 3: Write the controller**

`Wend.Api/wwwroot/js/auth/forgot/controller.js`:

```js
// Wires the forgot-password screen: submits, announces every outcome, moves focus deliberately.
export function createForgotController(model, view, announce) {
  let seenFirstRender = false;

  view.bindActions({
    submit: (fields) => model.submit(fields),
  });

  model.subscribe((state) => {
    if (state.status === "sending") {
      view.setBusy(true);
      announce("Sending…");
      return;
    }

    view.render(state);
    view.setBusy(false);

    // The first render is the empty form on page load: announce nothing, leave focus for the skip
    // link. Every later render is a submit result and gets both.
    if (!seenFirstRender) {
      seenFirstRender = true;
      return;
    }

    if (state.status === "sent") {
      view.focusSent();
      announce("If that address has an account, we've sent it a link. Check your inbox.");
    } else if (state.errors?.length) {
      view.focusFirstError();
      announce(state.errors[0]);
    }
  });
}
```

- [ ] **Step 4: Wire the route**

In `Wend.Api/wwwroot/js/main.js`, add to the import block:

```js
import { createForgotModel } from "./auth/forgot/model.js";
import { createForgotView } from "./auth/forgot/view.js";
import { createForgotController } from "./auth/forgot/controller.js";
```

Add a mount function beside `showRegister`:

```js
function showForgot() {
  hideAppChrome();
  mount((root) => {
    const model = createForgotModel();
    const view = createForgotView(root);
    createForgotController(model, view, announce);
  });
}
```

And add to the `switch` in `boot()`, beside the other auth routes:

```js
    case "/forgot-password": showForgot(); return;
```

- [ ] **Step 5: Link to it from the login screen**

In `Wend.Api/wwwroot/js/auth/login/view.js`, replace this line inside `HELP`:

```js
        <li>Forgotten the password? Password reset arrives in the next release.</li>
```

with:

```js
        <li>Forgotten the password? <a href="/forgot-password">Reset it</a>.</li>
```

and replace the links paragraph:

```js
        <p class="auth-links">No account yet? <a href="/register">Create one</a>.</p>
```

with:

```js
        <p class="auth-links"><a href="/forgot-password">Forgotten your password?</a></p>
        <p class="auth-links">No account yet? <a href="/register">Create one</a>.</p>
```

Permanent, not only inside the help block: somebody who knows they have forgotten their password
should not have to fail three times to be offered the way out.

- [ ] **Step 6: Run the app and check the screen**

```bash
Start-Service postgresql-x64-17; Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet run --project Wend.Api
```

Open `http://127.0.0.1:5174/forgot-password` and **hard-reload** (Ctrl+Shift+R). Then, in the
browser console — synthetic clicks do not dispatch in the automation pane, so drive the form through
`requestSubmit()` and wrap in an async IIFE because bare top-level `await` is a SyntaxError:

```js
(async () => {
  const input = document.querySelector("#forgot-email");
  input.value = "member@example.test";
  document.querySelector(".auth-form").requestSubmit();
  await new Promise((r) => setTimeout(r, 500));
  const btn = document.querySelector('[data-role="submit"]');
  console.log({
    successShown: !!document.querySelector(".auth-sent"),
    formSurvived: !!document.querySelector(".auth-form"),
    valueKept: document.querySelector("#forgot-email")?.value,
    submitReEnabled: btn?.disabled === false,
    focused: document.activeElement?.className,
    button: btn?.getBoundingClientRect().height,
    input: document.querySelector("#forgot-email")?.getBoundingClientRect().height,
  });
})();
```

Expected: `successShown: true`, `formSurvived: true`, `valueKept: "member@example.test"`,
`submitReEnabled: true`, `focused` contains `auth-sent`, `button` ≥ 44. The **input height will be
32** — that is the known, backlogged, pre-existing `.auth-form` shortfall, not a new failure. Record
the number, do not fix it here.

- [ ] **Step 7: Check the keyboard path and the announcement**

With the keyboard only: Tab from the address bar into the page, confirm the skip link, the email
field, the submit button, the "Forgotten your password?" link and the "Sign in" link are all
reachable, in that visual order, with a visible focus ring. Confirm `#status` received the sent
message:

```js
document.getElementById("status").textContent;
```

Expected: the "If that address has an account…" sentence.

- [ ] **Step 8: Commit**

```bash
git add Wend.Api/wwwroot/js/auth/forgot Wend.Api/wwwroot/js/main.js Wend.Api/wwwroot/js/auth/login/view.js
git commit -m "Add the forgot-password screen"
```

---

### Task 6: The reset-password screen

**Files:**
- Create: `Wend.Api/wwwroot/js/auth/reset/model.js`, `view.js`, `controller.js`
- Modify: `Wend.Api/wwwroot/js/api.js`
- Modify: `Wend.Api/wwwroot/js/main.js`

**Interfaces:**
- Consumes: `POST /api/auth/reset-password` and its two error codes (Task 3); the `/forgot-password`
  route (Task 5); `showLogin(reason)`, already in `main.js`.
- Produces: `createResetModel()` with `noLink()` and `submit({ userId, code, password })`;
  `createResetView(root)`; `createResetController(model, view, announce, { userId, code, onDone })`;
  the route `/reset-password`.

- [ ] **Step 1: Let `api()` carry the error body**

`Wend.Api/wwwroot/js/api.js` — add one line inside the `!res.ok` branch, after `error.status`:

```js
    // Some failures carry a machine-readable reason. The reset screen has to tell "your password
    // is too short" apart from "your link is dead", and both are 400. Every other caller reads only
    // .status; an empty or non-JSON body leaves this null instead of throwing.
    error.body = await res.json().catch(() => null);
```

- [ ] **Step 2: Write the model**

`Wend.Api/wwwroot/js/auth/reset/model.js`:

```js
import { api } from "../../api.js";

// State only. The token pair is NOT held here — the controller owns it and passes it on each
// submit, so it can never reach the view and never reach the DOM.
export function createResetModel() {
  let state = { status: "editing", errors: [] };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    // Arrived with nothing to redeem — a reload, a bookmark, or a back-navigation after
    // replaceState stripped the query string.
    noLink() {
      state = { status: "nolink", errors: [] };
      notify();
    },
    async submit({ userId, code, password }) {
      // A second submit after a successful one would send a token the first submit has already
      // killed, and the screen would announce "this link has expired" to somebody whose password
      // had just been changed. The view's in-flight disable loses this race when both clicks land
      // before the first response.
      if (state.status === "sending" || state.status === "done") return;

      state = { status: "sending", errors: [] };
      notify();
      try {
        await api("/api/auth/reset-password", {
          method: "POST",
          body: JSON.stringify({ userId, code, password }),
        });
        state = { status: "done", errors: [] };
      } catch (error) {
        const code400 = error?.status === 400 ? error?.body?.error : null;
        if (code400 === "token") {
          state = { status: "expired", errors: [] };
        } else if (code400 === "password") {
          state = {
            status: "editing",
            errors: ["That password is too short. Use at least 12 characters."],
          };
        } else {
          state = { status: "editing", errors: ["Something went wrong. Please try again."] };
        }
      }
      notify();
    },
  };
}
```

- [ ] **Step 3: Write the view**

`Wend.Api/wwwroot/js/auth/reset/view.js`:

```js
import { escapeHtml } from "../../escape.js";

// Renders the new-password form and the two dead-end states. No logic; events via data-action.
//
// userId and code are NEVER passed to this view and never rendered — no hidden inputs, nothing.
// They come off the query string of an anonymous page anybody can link to, and every view here
// renders through a template literal into innerHTML, so `value="${code}"` would be reflected XSS.
// The controller holds them and merges them on submit.
export function createResetView(root) {
  let h = {};

  function render(state) {
    if (state.status === "nolink") {
      root.innerHTML = `
        <div class="auth-view">
          <h2 class="auth-heading" tabindex="-1">Nothing to reset</h2>
          <p>Open the link from your email to set a new password. Links last one hour.</p>
          <p class="auth-links"><a href="/forgot-password">Request a new link</a>.</p>
        </div>`;
      return;
    }

    if (state.status === "expired") {
      root.innerHTML = `
        <div class="auth-view">
          <h2 class="auth-heading" tabindex="-1">This link has expired or was already used</h2>
          <p>Reset links last one hour, and each one works once.</p>
          <p class="auth-links"><a href="/forgot-password">Request a new link</a>.</p>
        </div>`;
      return;
    }

    const errors = state.errors ?? [];
    root.innerHTML = `
      <div class="auth-view">
        <h2 class="auth-heading" tabindex="-1">Set a new password</h2>
        ${errors.length ? `
        <div class="auth-errors alert alert-danger" tabindex="-1">
          <p>${escapeHtml(errors[0])}</p>
        </div>` : ""}
        <form class="auth-form" data-action="submit">
          <!-- minlength mirrors the server's policy so the browser gives native, per-field,
               accessible feedback before the request goes out. -->
          <label for="reset-password">New password</label>
          <input id="reset-password" name="password" type="password" autocomplete="new-password"
            minlength="12" required aria-describedby="hint-reset-password" />
          <p class="field-hint" id="hint-reset-password">At least 12 characters. A memorable phrase beats a short tangle of symbols.</p>

          <button type="submit" class="btn btn-primary" data-role="submit">Set the password</button>
        </form>
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  // A server-side error belongs to no field, so focus goes to the summary. Per-field errors are
  // left to native validation, which focuses the offending input itself.
  function focusFirstError() {
    const summary = root.querySelector(".auth-errors");
    if (summary) summary.focus();
    else focusHeading();
  }

  function setBusy(busy) {
    const button = root.querySelector('[data-role="submit"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Setting…" : "Set the password";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="submit"]')) return;
      e.preventDefault();
      const data = new FormData(e.target);
      h.submit({ password: data.get("password") ?? "" });
    });
  }

  return { render, focusHeading, focusFirstError, setBusy, bindActions };
}
```

- [ ] **Step 4: Write the controller**

`Wend.Api/wwwroot/js/auth/reset/controller.js`:

```js
// Wires the reset screen. Owns userId and code for the lifetime of the screen and merges them into
// each submit — the view never sees them.
export function createResetController(model, view, announce, { userId, code, onDone } = {}) {
  let seenFirstRender = false;

  view.bindActions({
    submit: ({ password }) => model.submit({ userId, code, password }),
  });

  // Settle the no-link case BEFORE subscribing, so that arrival renders and announces once instead
  // of showing a form to somebody with nothing to submit. Mirrors the verify screen.
  if (!userId || !code) model.noLink();

  model.subscribe((state) => {
    if (state.status === "sending") {
      view.setBusy(true);
      announce("Setting your new password…");
      return;
    }

    if (state.status === "done") {
      // The API deliberately does not sign the user in: a link that arrived by email should not
      // become a session. Hand off to login, which moves focus and announces the reason.
      onDone?.();
      return;
    }

    view.render(state);
    view.setBusy(false);

    // Unlike register and login, the FIRST render here is already a result — this screen is reached
    // by clicking a link in an email, and "nothing to reset" is an outcome the user must hear.
    if (state.status === "nolink") {
      view.focusHeading();
      announce("Nothing to reset. Open the link from your email, or request a new one.");
      seenFirstRender = true;
      return;
    }

    if (!seenFirstRender) {
      seenFirstRender = true;
      return;
    }

    if (state.status === "expired") {
      view.focusHeading();
      announce("This link has expired or was already used. Request a new one.");
    } else if (state.errors?.length) {
      view.focusFirstError();
      announce(state.errors[0]);
    }
  });
}
```

- [ ] **Step 5: Wire the route**

In `Wend.Api/wwwroot/js/main.js`, add to the import block:

```js
import { createResetModel } from "./auth/reset/model.js";
import { createResetView } from "./auth/reset/view.js";
import { createResetController } from "./auth/reset/controller.js";
```

Add a mount function beside `showVerify`:

```js
function showReset() {
  hideAppChrome();
  const params = new URLSearchParams(location.search);
  const userId = params.get("userId") ?? "";
  const code = params.get("code") ?? "";

  // Drop the live token out of the address bar and the history entry as soon as it is read — it
  // still reached the server in the POST body, but it no longer sits in a URL a user might
  // screenshot, bookmark, or paste into a support chat. A reload after this point has no token,
  // which is what the screen's no-link state is for.
  history.replaceState(null, "", "/reset-password");

  mount((root) => {
    const model = createResetModel();
    const view = createResetView(root);
    createResetController(model, view, announce, {
      userId,
      code,
      onDone: () => showLogin("Your password has been changed — please sign in."),
    });
  });
}
```

And add to the `switch` in `boot()`:

```js
    case "/reset-password": showReset(); return;
```

- [ ] **Step 6: Walk the real flow in a browser**

```bash
Start-Service postgresql-x64-17; Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet run --project Wend.Api
```

Register and confirm an account through the UI if you do not have one, then request a reset at
`/forgot-password`. Read the link out of the dev mail log:

```bash
Get-Content "$env:LOCALAPPDATA\Wend\auth-emails.log" -Tail 4
```

Open the `reset` link, **hard-reload**, and check the screen:

```js
(async () => {
  console.log({
    urlStripped: location.search === "",
    tokenInDom: document.getElementById("app").innerHTML.includes("code="),
    heading: document.querySelector(".auth-heading")?.textContent,
    button: document.querySelector('[data-role="submit"]')?.getBoundingClientRect().height,
  });
})();
```

Expected: `urlStripped: true`, `tokenInDom: false`, heading "Set a new password", `button` ≥ 44.

- [ ] **Step 7: Check `minlength` with real keystrokes**

Focus the field and **type** — do not assign `.value`. `tooShort` only fires on a user-edited value,
so a scripted assignment leaves the field non-dirty, native validation passes, the request goes out,
and the check records a false pass on the one constraint this field has.

```js
document.querySelector("#reset-password").focus();
```

Type `short` on the keyboard, press Enter, and confirm the browser blocks the submit with its own
"lengthen this text" bubble and no network request appears.

- [ ] **Step 8: Check the three outcomes**

Type a full-length password and submit. Expected: the login screen appears, focus is on its heading,
and `#status` reads "Your password has been changed — please sign in."

Then reload `/reset-password` (no query string now). Expected: the "Nothing to reset" heading,
focus on it, the announcement, and **no network request** — confirm in the network panel.

Then open the same emailed link a second time. Expected: "This link has expired or was already
used", focus on the heading, and the link to `/forgot-password` working.

- [ ] **Step 9: Check the double submit**

Open a fresh reset link, focus the field and type a valid password by hand:

```js
document.querySelector("#reset-password").focus();
```

Then fire two submits back to back, before the first can settle:

```js
const form = document.querySelector(".auth-form");
form.requestSubmit();
form.requestSubmit();
```

Expected: the login screen with the changed-password announcement. **Not** the expired state — if
the expired heading appears, the model's `status === "sending" || "done"` guard is missing or the
statuses do not match.

- [ ] **Step 10: Commit**

```bash
git add Wend.Api/wwwroot/js/auth/reset Wend.Api/wwwroot/js/api.js Wend.Api/wwwroot/js/main.js
git commit -m "Add the reset-password screen"
```

---

### Task 7: Backlog and docs

**Files:**
- Modify: `docs/backlog.md`

**Interfaces:**
- Consumes: nothing. Produces: nothing. This task exists so three deliberate deferrals cannot be
  discovered at deploy time.

- [ ] **Step 1: Widen the rate-limiting entry**

In `docs/backlog.md`, in the section `### /api/auth/* is not rate limited`, replace the **Now** line:

```markdown
- **Now:** none of register, resend-verification, login or logout is rate limited. Register, resend and the unconfirmed-account nudge on login all trigger outbound email, so all three are email-bombing vectors, and login is the credential-stuffing surface.
```

with:

```markdown
- **Now:** none of register, resend-verification, login, logout, forgot-password or reset-password is rate limited. Register, resend, the unconfirmed-account nudge on login and **forgot-password** all trigger outbound email, and login is the credential-stuffing surface. **`/api/auth/forgot-password` (Plan 5) is the cheapest of them to abuse: one anonymous request, no password needed, and the mail goes to a third party whose address is the only thing the caller has to know.**
```

- [ ] **Step 2: Widen the query-string entry**

Replace the heading and the first two bullets of `### Verify tokens travel in a query string`:

```markdown
### Verify tokens travel in a query string

- **Now:** the emailed link carries `userId` and `code` as query parameters. The SPA strips them from the address bar with `history.replaceState`, and Kestrel logs nothing at Information.
- **Later:** exclude `/verify` query strings from access logging.
```

with:

```markdown
### Verify and reset tokens travel in a query string

- **Now:** both emailed links — `/verify` (Plan 3) and `/reset-password` (Plan 5) — carry `userId` and `code` as query parameters. Both screens strip them from the address bar with `history.replaceState`, and Kestrel logs nothing at Information.
- **Later:** exclude `/verify` **and `/reset-password`** query strings from access logging.
```

- [ ] **Step 3: Widen the register-timing entry**

Replace the **Now** and **Later** lines of `### Register leaks account existence through timing`:

```markdown
- **Now:** `POST /api/auth/register` returns the same `204` whether or not the address is taken, but the taken path skips password hashing and so returns measurably faster.
- **Later:** dummy-hash the skipped path, as login will.
```

with:

```markdown
- **Now:** `POST /api/auth/register` returns the same `204` whether or not the address is taken, but the taken path skips password hashing and so returns measurably faster. **`POST /api/auth/forgot-password` (Plan 5) has the same shape: the unknown-address branch generates no token and sends no mail, so it returns faster than a confirmed one.**
- **Later:** dummy-hash the skipped path on register, as login does, and equalise the forgot-password branches.
```

- [ ] **Step 4: Commit**

```bash
git add docs/backlog.md
git commit -m "Record what Plan 5 deferred"
```

---

## Done when

- `dotnet test` reports **253** green.
- A person can walk register → verify → login → forgot → the emailed link → new password → sign in,
  in a browser, with the keyboard only.
- The three backlog entries name forgot-password.
- The PR body records: the pinned test total and its delta from 231, any deviation from this plan,
  the measured auth-input height (expected 32px, pre-existing and backlogged), and the fact that
  Task 5–6 checks were driven through `requestSubmit()` where a real pointer was not used.
