# Wend Slice 2a — Plan 3: Register & verify

> **For agentic workers:** use `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` to implement this task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let a stranger create a Wend account and confirm their email address — registration,
email-confirmation tokens, the outbound-email seam with a dev sender, resend, and the purge that
stops an unconfirmed account squatting an address forever.

**Architecture:** Identity's services are wired headlessly (`AddIdentityCore` — no cookie scheme, no
`SignInManager`), so this plan owns `UserManager<WendUser>` and the token providers and nothing
else. A new `AuthEndpoints` group serves `/api/auth/register`, `/verify` and `/resend-verification`,
all anonymous, all answering with the *same* generic response whether or not the address exists.
Outbound mail goes through an `IAuthEmailSender` seam whose only implementation writes the link to a
local file. The emailed link lands on the SPA shell, which POSTs the code back — so an email
scanner following the link cannot burn the token.

**Tech stack:** `net10.0`, EF Core 10, Npgsql `10.0.3`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` `10.0.9`, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`, native PostgreSQL 17, vanilla-JS MVC (no build step).

**Reference:** parent spec [`2026-07-08-wend-slice2a-accounts-design.md`](../2026-07-08-wend-slice2a-accounts-design.md) · previous plan [`2026-08-06-slice2a-identity-ownership.md`](./2026-08-06-slice2a-identity-ownership.md).

---

## Scope

The spec's sequencing (§ *Sequencing*, items 1–8) numbers the remaining plans. Plans 1 and 2
together delivered item 1 (*Foundation*). **This plan is item 2, "Register + verify"** — and stops
there.

| In | Out (and which plan owns it) |
|---|---|
| `AddIdentityCore`, password policy, email uniqueness | Cookie auth, `SignInManager`, lockout, `/login`, `/logout`, `/me`, the frontend auth gate — **Plan 4** |
| `POST /api/auth/register` | Forgot / reset password — **Plan 5** |
| `POST /api/auth/verify` + the verify landing screen | Change email / password, remember-me — **Plan 6** |
| `POST /api/auth/resend-verification` | Account deletion — **Plan 7** |
| `IAuthEmailSender` + the dev file sender | Rate limiting, antiforgery, HTTPS/HSTS, headers — **Plan 8** |
| Unverified-account purge | Host, real email provider, privacy policy / terms / DPA — **Plan 9** |
| The register screen | |

**The app still cannot show you a board when this plan lands.** `ICurrentUser` remains
`NullCurrentUser`, so every `/api/boards` call answers 401 exactly as it has since Plan 2. That is
the accepted state until Plan 4, not a regression.

---

## Global constraints

- Target `net10.0`. EF Core 10 throughout; **no Docker, no Testcontainers, no hypervisor.**
- **New configuration key:** `Wend:PublicBaseUrl` — the origin emailed links are built from.
  Unset in Development (the request host is used); required in every other environment, where its
  absence fails startup.
- **No new NuGet packages.** Everything this plan needs is already referenced:
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore` `10.0.9` (Wend.Core) supplies `UserManager`
  and the token providers; `WebEncoders` ships in the ASP.NET Core shared framework.
- `dotnet build` must end at **0 warnings**; `dotnet test` green at the stated count for every task.
- **No authentication scheme in this plan.** No `AddAuthentication`, no `AddIdentityCookies`, no
  `SignInManager`, no `RequireAuthorization()`. `ICurrentUser` stays `NullCurrentUser`.
- **No third-party resources on any page.** The verify screen loads only same-origin CSS/JS, so no
  `Referer` header can carry a live token off-origin. Do not add a font, an icon CDN, or an
  analytics tag.
- Mobile-first CSS: baseline styles target the smallest screen, `min-width` queries layer up at
  768px / 1024px. No `max-width` queries.
- `escapeHtml` at **every** user-content interpolation in a view. No exceptions.
- Commits: one per task, authored under your own account, **no co-author trailer and no AI
  attribution** (house rule). Run every command from the repo root.
- Branch: `feature/slice2a-plan3-register-verify`, opened as a PR for the other owner to review and
  merge.

---

## Decisions locked at plan time

The spec's *Open items* list several values to fix "at plan time". These are those decisions — they
are the ones to argue with at review, before any code exists.

| Decision | Value | Why |
|---|---|---|
| **Password policy** | Minimum **12** characters; **no** forced digit / upper / lower / symbol | Current NIST guidance favours length over composition rules, which mostly produce `Password1!`. Identity's default is 6 with all four classes on, so every switch is set explicitly. |
| **Verify-token lifespan** | **24 hours**, via a dedicated provider | The spec says "verify: hours, reset: ~1 hour". A dedicated provider is what keeps the two independent — otherwise Plan 5 setting the global lifespan to 1 hour would silently shorten verification too. |
| **Unverified-account purge window** | **7 days** | Long enough to survive a holiday weekend, short enough that a squatted address frees itself. Resend covers anyone who misses it. |
| **"Single use"** | Enforced by the `EmailConfirmed` check, not by invalidating the token | Identity's data-protector tokens are time-limited and stamp-bound, **not** single-use. A replayed link finds an already-confirmed account and gets the accessible "already verified" state. The user-visible guarantee in the spec holds; the mechanism is this, and the plan says so rather than implying crypto it doesn't have. |
| **Verify is a `POST`, not the spec's `GET /verify`** | Email link → `/verify?userId=…&code=…` (SPA shell) → SPA `POST /api/auth/verify` | The spec's endpoint table is marked *illustrative*. Confirming an email is a state change, and corporate mail scanners and link-preview bots follow `GET` links automatically — a `GET` that confirms would be triggered by a robot before the human clicks, and worse, would let a prefetch confirm an address the human never opened. With `POST`, a scanner fetching the link receives only the static shell. |
| **Email seam is named `IAuthEmailSender`** | not `IEmailSender` | `Microsoft.AspNetCore.Identity.IEmailSender` exists, and `AuthEndpoints.cs` imports both that namespace and `Wend.Core` — an unqualified `IEmailSender` there is a `CS0104` ambiguous reference. The seam the spec asked for is unchanged; only the identifier avoids the collision. |
| **Register's response timing is not equalised** | Documented limitation | The spec requires constant-time behaviour for **login** (Plan 4). Register creating-vs-skipping a password hash is a timing side channel, but the app is still localhost-only and unreachable when this plan lands. **Plan 8 must close it.** Recorded in `docs/backlog.md` in Task 9. |
| **No rate limiting yet** | Deferred to Plan 8, per the spec's own sequencing | Register and resend are email-sending endpoints and therefore email-bombing vectors. They are not reachable from another machine until Plan 9 deploys — **this is a launch gate, and Plan 9 must not deploy before Plan 8 lands.** |
| **Lockout thresholds** | Not set here | Lockout is consumed by `SignInManager`, which arrives in Plan 4. Setting it now would be config with no reader. |
| **Inactive-account retention** | **Explicitly deferred to Plan 9** | The spec permits setting it here *or* deferring, and it is a legal-posture decision that belongs with the privacy policy rather than with registration code. Deferred out loud, not skipped — backlogged in Task 9. |
| **Emailed links come from `Wend:PublicBaseUrl`** | not the request's `Host` header | Building them from `http.Host` is Host-header injection: an attacker sets the header, and Wend emails the victim a genuine link pointing at the attacker's server. Development falls back to the request host, and startup fails elsewhere if the setting is absent. |
| **Outside Development, the app refuses to start** | until a real email sender exists | The only `IAuthEmailSender` writes to a file. Registered unconditionally it would make a deployed Wend look healthy while sending nothing and accumulating addresses beside live tokens. |

---

## Notes for the implementer

- **Start PostgreSQL first.** The service is `Manual` start: `Start-Service postgresql-x64-17`.
  Connection refused or an EF timeout means the service is stopped, not a code bug.
- **Stop the app before building.** The process is `Wend.Api`, *not* `Wend`:
  `Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force`. Skipping this gives
  MSB3021/3027 copy-lock errors that look like test failures.
- **Run `dotnet ef` with `ASPNETCORE_ENVIRONMENT=Development`** so user-secrets supply the
  connection string.
- **Test counts are load-bearing.** A paste-driven edit once silently dropped three tests behind a
  green suite. Every task states its expected total; check the number `dotnet test` prints against
  it before committing. **Baseline: 174.** This plan ends at **205**.
- **`CreatedAt` must default to `DateTime.UtcNow` in the property initializer, not just in the
  handler.** Npgsql maps `DateTime` to `timestamp with time zone` and **throws** on a
  `Kind=Unspecified` value — which is what `default(DateTime)` is. `TestUsers.SeedAsync` and
  `WendApiFactory.ConfigureClient` both construct `WendUser` by object initializer and never set
  `CreatedAt`; the property initializer is the only thing keeping those 174 existing tests green.
- **This migration does not destroy data**, unlike Plan 2's. It adds one column with a
  `now()` default.
- **Identity's `UserValidator` does the email-format check for you** — but only when
  `RequireUniqueEmail` is `true`, which Task 2 sets. It also enforces
  `User.AllowedUserNameCharacters`; the default set covers ordinary addresses, so an exotic-but-legal
  address could be refused. Accepted, and out of scope to widen here.
- **The register handler must never reveal that an address is taken.** Every path that finds an
  existing account returns the same `204` as a fresh registration. When reviewing, read the handler
  for *response shape*, not for correctness of the happy path — a stray `Results.Conflict()` there
  is the bug this plan most needs to not ship.
- **Validation order in that handler is load-bearing, not stylistic.** Everything that depends only
  on the caller's own input — email length, email format, display name, password policy — runs
  *before* the existence lookup. Move the password check back inside `CreateAsync` and a weak
  password answers `400` for a free address and `204` for a taken one, which enumerates the user
  table one request at a time. `A_weak_password_answers_the_same_for_a_taken_address_as_a_free_one`
  is the guard; if it ever fails, the fix is the ordering, never the test.
- **The purge `BackgroundService` must not fire during tests.** `PeriodicTimer` waits a full
  interval *before* its first tick, and the interval is 6 hours, so an app booted by
  `WendApiFactory` and disposed seconds later never reaches the database. Do not "helpfully" add an
  immediate first run.
- **Frontend tasks have no automated tests.** This repo has no JS test harness (no `package.json`,
  no Biome — `.editorconfig` only), exactly as through all of Slice 1. Tasks 7 and 8 are verified by
  the scripted manual checks written into them. Run those checks; do not skip them because the C#
  suite is green.
- **Hard-reload every browser check.** `UseStaticFiles` sends no `Cache-Control`, so a normal reload
  serves stale ES modules from an earlier `:5174` session.

---

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Core/IAuthEmailSender.cs` | **new** | The outbound-auth-email seam |
| `Wend.Core/WendPaths.cs` | modify | Path of the dev email log |
| `Wend.Core/WendUser.cs` | modify | `CreatedAt` |
| `Wend.Core/DisplayNames.cs` | **new** | Display-name cap + control-character stripping |
| `Wend.Core/UnverifiedAccounts.cs` | **new** | The purge query |
| `Wend.Core/Migrations/` | **new** | `AddUserCreatedAt` |
| `Wend.Api/FileAuthEmailSender.cs` | **new** | Dev sender — writes the link to a file and the console |
| `Wend.Api/EmailConfirmationTokenProvider.cs` | **new** | 24-hour token provider + its options |
| `Wend.Api/AuthEndpoints.cs` | **new** | `/api/auth/register` · `/verify` · `/resend-verification` |
| `Wend.Api/UnverifiedAccountPurgeService.cs` | **new** | 6-hourly `BackgroundService` wrapper |
| `Wend.Api/Program.cs` | modify | Wire Identity, the sender, the endpoints, the purge |
| `Wend.Api/wwwroot/js/main.js` | modify | Route `/register` and `/verify` before the board app |
| `Wend.Api/wwwroot/js/auth/register/{model,view,controller}.js` | **new** | Registration screen |
| `Wend.Api/wwwroot/js/auth/verify/{model,view,controller}.js` | **new** | Verify landing screen + expired state |
| `Wend.Api/wwwroot/css/app.css` | modify | Auth-screen layout (mobile-first) |
| `Wend.Tests/FakeAuthEmailSender.cs` | **new** | Captures sent links for assertions |
| `Wend.Tests/AuthEmailSenderTests.cs` | **new** | Dev sender writes and appends |
| `Wend.Tests/IdentityConfigurationTests.cs` | **new** | Password policy, unique email, provider, `CreatedAt` |
| `Wend.Tests/AuthRegisterTests.cs` | **new** | Registration + enumeration resistance |
| `Wend.Tests/AuthVerifyTests.cs` | **new** | Confirmation, reuse, expiry |
| `Wend.Tests/AuthResendTests.cs` | **new** | Resend + generic responses |
| `Wend.Tests/UnverifiedAccountPurgeTests.cs` | **new** | Window, confirmed accounts, cascade |
| `Wend.Tests/WendApiFactory.cs` | modify | Expose the fake sender to tests |
| `docs/backlog.md` | modify | Register timing side channel |

---

## Task 1 — The email seam and the dev sender

No Identity yet. This is the piece every later task sends through, so it lands first and alone.

**Interfaces produced:** `Wend.Core.IAuthEmailSender.SendEmailConfirmationAsync(string email, string link)`; `Wend.Api.FileAuthEmailSender(string path)`; `Wend.Core.WendPaths.AuthEmailLogPath()`.

- [ ] **Step 1 — write the failing test**

Create `Wend.Tests/AuthEmailSenderTests.cs`:

```csharp
using Wend.Api;

namespace Wend.Tests;

public class AuthEmailSenderTests
{
    private string _path = null!;

    [SetUp]
    public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"wend_email_{Guid.NewGuid():N}.log");

    [TearDown]
    public void TearDown() => File.Delete(_path);

    [Test]
    public async Task Sending_writes_the_link_to_the_log_file()
    {
        var sender = new FileAuthEmailSender(_path);

        await sender.SendEmailConfirmationAsync("someone@example.test", "https://wend.test/verify?code=abc");

        var written = await File.ReadAllTextAsync(_path);
        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("someone@example.test"));
            Assert.That(written, Does.Contain("https://wend.test/verify?code=abc"));
        });
    }

    [Test]
    public async Task Sending_twice_appends_rather_than_overwrites()
    {
        var sender = new FileAuthEmailSender(_path);

        await sender.SendEmailConfirmationAsync("first@example.test", "https://wend.test/verify?code=1");
        await sender.SendEmailConfirmationAsync("second@example.test", "https://wend.test/verify?code=2");

        var written = await File.ReadAllTextAsync(_path);
        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("first@example.test"));
            Assert.That(written, Does.Contain("second@example.test"));
        });
    }
}
```

- [ ] **Step 2 — run it and watch it fail**

```powershell
dotnet test --filter FullyQualifiedName~AuthEmailSenderTests
```

Expected: build error — `FileAuthEmailSender` does not exist.

- [ ] **Step 3 — create `Wend.Core/IAuthEmailSender.cs`**

```csharp
namespace Wend.Core;

/// <summary>
/// Outbound authentication email. Named IAuthEmailSender, not IEmailSender, because
/// Microsoft.AspNetCore.Identity already defines an IEmailSender and AuthEndpoints imports both
/// that namespace and this one — an unqualified name there would not compile.
///
/// The only implementation writes to a local file (dev). A transactional provider arrives with
/// deployment, where the provider is a GDPR data processor and needs a DPA before it sees a real
/// address.
/// </summary>
public interface IAuthEmailSender
{
    Task SendEmailConfirmationAsync(string email, string link);
}
```

- [ ] **Step 4 — add the log path to `Wend.Core/WendPaths.cs`**

Add this method inside the existing `WendPaths` class, after `DefaultDbPath()`:

```csharp
    /// <summary>
    /// Where the dev email sender writes confirmation links: <c>%LOCALAPPDATA%\Wend\auth-emails.log</c>.
    /// Not a mailbox — a developer's click-through log. It contains live tokens, so it stays out of
    /// the repo (AppData, like the database) and is never shipped to a real environment.
    /// </summary>
    public static string AuthEmailLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wend");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "auth-emails.log");
    }
```

- [ ] **Step 5 — create `Wend.Api/FileAuthEmailSender.cs`**

```csharp
using Wend.Core;

namespace Wend.Api;

/// <summary>
/// Development email sender: appends the link to a log file and echoes it to the console, so the
/// whole register-and-verify flow can be built and walked with no provider account and no real
/// send. Deliberately the only implementation in this plan.
/// </summary>
public sealed class FileAuthEmailSender(string path) : IAuthEmailSender
{
    public async Task SendEmailConfirmationAsync(string email, string link)
    {
        var entry = $"[{DateTime.UtcNow:u}] confirm {email}{Environment.NewLine}  {link}{Environment.NewLine}";
        await File.AppendAllTextAsync(path, entry);
        Console.WriteLine(entry);
    }
}
```

- [ ] **Step 6 — run the tests**

```powershell
dotnet test
```

Expected: **176 passed, 0 failed** (174 baseline + 2).

- [ ] **Step 7 — commit**

```powershell
git add Wend.Core/IAuthEmailSender.cs Wend.Core/WendPaths.cs Wend.Api/FileAuthEmailSender.cs Wend.Tests/AuthEmailSenderTests.cs
git commit -m "Add the outbound auth-email seam and a dev file sender"
```

---

## Task 2 — Wire Identity headlessly

`UserManager<WendUser>`, the password policy, unique emails, the 24-hour confirmation-token
provider, and the `CreatedAt` the purge will need. Still no endpoints.

**Interfaces produced:** `UserManager<WendUser>` in DI; `WendUser.CreatedAt`; the token provider named `"WendEmailConfirmation"`.

- [ ] **Step 1 — write the failing test**

Create `Wend.Tests/IdentityConfigurationTests.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wend.Core;

namespace Wend.Tests;

public class IdentityConfigurationTests
{
    private WendApiFactory _factory = null!;
    private IServiceScope _scope = null!;
    private UserManager<WendUser> _users = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _factory.CreateClient().Dispose();   // boots the app
        _scope = _factory.Services.CreateScope();
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _factory.Dispose();
    }

    private static WendUser NewUser(string email) =>
        new() { UserName = email, Email = email, DisplayName = "Test User" };

    [Test]
    public async Task A_password_shorter_than_the_minimum_is_rejected()
    {
        var result = await _users.CreateAsync(NewUser("short@example.test"), "Abc1!def");

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task A_long_passphrase_without_symbols_is_accepted()
    {
        var result = await _users.CreateAsync(NewUser("phrase@example.test"), "correct horse battery staple");

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    [Test]
    public async Task A_second_user_cannot_reuse_an_email()
    {
        await _users.CreateAsync(NewUser("taken@example.test"), "correct horse battery staple");

        var result = await _users.CreateAsync(NewUser("taken@example.test"), "another entirely fine passphrase");

        Assert.That(result.Succeeded, Is.False);
    }

    // Not async: there is nothing to await here, and an async method without an await is a
    // CS1998 warning — which this plan's "0 warnings" rule would fail on.
    [Test]
    public void Email_confirmation_uses_wends_own_token_provider()
    {
        var options = _scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.That(options.Tokens.EmailConfirmationTokenProvider, Is.EqualTo("WendEmailConfirmation"));
    }

    [Test]
    public async Task A_new_account_records_when_it_was_created()
    {
        await _users.CreateAsync(NewUser("stamped@example.test"), "correct horse battery staple");

        var user = await _users.FindByEmailAsync("stamped@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(user!.CreatedAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
            Assert.That(user.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }
}
```

- [ ] **Step 2 — run it and watch it fail**

```powershell
dotnet test --filter FullyQualifiedName~IdentityConfigurationTests
```

Expected: FAIL — `UserManager<WendUser>` is not registered (`InvalidOperationException: No service for type ... UserManager`), and `CreatedAt` does not compile.

- [ ] **Step 3 — add `CreatedAt` to `Wend.Core/WendUser.cs`**

Add this property to the existing `WendUser` class, below `DisplayName`:

```csharp
    /// <summary>
    /// When the account was created (UTC). IdentityUser has no such field, and the unverified-account
    /// purge needs one to know what is stale.
    ///
    /// The initializer is load-bearing: Npgsql maps DateTime to 'timestamp with time zone' and throws
    /// on a Kind=Unspecified value, which is exactly what default(DateTime) is. Test helpers build
    /// WendUser by object initializer and never set this, so without the default they would all fail.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
```

- [ ] **Step 4 — create the migration**

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet ef migrations add AddUserCreatedAt --project Wend.Core --startup-project Wend.Api
```

Open the generated `Wend.Core/Migrations/*_AddUserCreatedAt.cs` and give the column a server-side
default so the rows already in the table are valid:

```csharp
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");
```

- [ ] **Step 5 — create `Wend.Api/EmailConfirmationTokenProvider.cs`**

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wend.Api;

/// <summary>
/// Confirmation tokens with their own lifespan, independent of every other Identity token.
/// Without this the global DataProtectionTokenProviderOptions governs all of them, so Plan 5
/// shortening the default to the ~1 hour a password reset wants would silently shorten email
/// confirmation to an hour too.
/// </summary>
public class EmailConfirmationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;

public class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailConfirmationTokenProviderOptions()
    {
        Name = "WendEmailConfirmationTokenProvider";
        // Long enough to survive "I'll do it in the morning", short enough that a leaked link in a
        // forwarded mailbox goes stale quickly.
        TokenLifespan = TimeSpan.FromHours(24);
    }
}
```

- [ ] **Step 6 — wire Identity in `Wend.Api/Program.cs`**

Add `using Microsoft.AspNetCore.Identity;` to the top of the file. Then, immediately after the
`AddScoped<ICurrentUser, NullCurrentUser>()` line, insert:

```csharp
// Identity, headless: AddIdentityCore gives UserManager and the token providers with no cookie
// scheme and no SignInManager. Plan 4 adds AddSignInManager() + AddIdentityCookies() on top.
builder.Services.AddIdentityCore<WendUser>(options =>
    {
        // Length over composition, per current NIST guidance — every switch set explicitly
        // because Identity's defaults are 6 characters with all four character classes required.
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        // Email is the login credential, so it has to be unique. This also switches on
        // UserValidator's email-format check.
        options.User.RequireUniqueEmail = true;

        options.Tokens.ProviderMap.Add("WendEmailConfirmation",
            new TokenProviderDescriptor(typeof(EmailConfirmationTokenProvider<WendUser>)));
        options.Tokens.EmailConfirmationTokenProvider = "WendEmailConfirmation";
    })
    .AddEntityFrameworkStores<WendDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddTransient<EmailConfirmationTokenProvider<WendUser>>();

// The only sender that exists writes to a local file. Registering it unconditionally would mean a
// deployed Wend "works" — registrations succeed, nobody ever receives a link, and the server quietly
// accumulates a file of email addresses paired with live tokens. Refusing to boot is the correct
// behaviour for an auth system with no way to send mail; Plan 9 wires a transactional provider here.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IAuthEmailSender>(_ =>
        new FileAuthEmailSender(WendPaths.AuthEmailLogPath()));
}
else
{
    throw new InvalidOperationException(
        "No production IAuthEmailSender is configured. Wend will not start outside Development "
        + "until the deployment plan wires a transactional email provider.");
}
```

- [ ] **Step 7 — run the tests**

```powershell
dotnet test
```

Expected: **181 passed, 0 failed** (176 + 5).

- [ ] **Step 8 — commit**

```powershell
git add Wend.Core/WendUser.cs Wend.Core/Migrations Wend.Api/EmailConfirmationTokenProvider.cs Wend.Api/Program.cs Wend.Tests/IdentityConfigurationTests.cs
git commit -m "Wire ASP.NET Identity headlessly with a 24-hour confirmation token"
```

---

## Task 3 — `POST /api/auth/register`

The enumeration-resistant registration endpoint. **The response shape is the security property
here** — every outcome that involves an existing address returns exactly what a fresh registration
returns.

**Interfaces produced:** `POST /api/auth/register` taking `{ email, password, displayName }`, answering `204` on success-or-existing and `400` on the caller's own bad input; `Wend.Core.DisplayNames.Clean(string?)` and `DisplayNames.MaxLength`; `Wend.Tests.FakeAuthEmailSender` with `Sent` (a list of `(Email, Link)`); `WendApiFactory.Email`.

- [ ] **Step 1 — create the test double `Wend.Tests/FakeAuthEmailSender.cs`**

```csharp
using Wend.Core;

namespace Wend.Tests;

/// <summary>Captures what would have been emailed, so tests can assert on links and on silence.</summary>
public sealed class FakeAuthEmailSender : IAuthEmailSender
{
    public List<(string Email, string Link)> Sent { get; } = [];

    public Task SendEmailConfirmationAsync(string email, string link)
    {
        Sent.Add((email, link));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2 — expose it from `Wend.Tests/WendApiFactory.cs`**

Two startup guards now branch on the environment — the email sender (Task 2) and `Wend:PublicBaseUrl`
— so pin it rather than inheriting whatever the test host defaults to. Add as the **first** line of
`ConfigureWebHost`:

```csharp
        // Both of Program.cs's environment-guarded branches must take their Development path here.
        builder.UseEnvironment("Development");
```

Add the property beside the existing `CurrentUser` property:

```csharp
    /// <summary>Captured outbound auth email. Assert on Email.Sent instead of reading a log file.</summary>
    public FakeAuthEmailSender Email { get; } = new();
```

and extend the existing `ConfigureTestServices` call so it also replaces the sender:

```csharp
        // Tests supply their own current user; the app's NullCurrentUser would make everything 401.
        // They also swap the file-writing dev sender for one that records in memory.
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ICurrentUser>(_ => CurrentUser);
            services.AddSingleton<IAuthEmailSender>(Email);
        });
```

Add `using Wend.Core;` if it is not already present (it is — `WendUser` is used below).

- [ ] **Step 3 — write the failing tests**

Create `Wend.Tests/AuthRegisterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class AuthRegisterTests
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

    private Task<HttpResponseMessage> Register(
        string email = "new@example.test",
        string password = GoodPassword,
        string displayName = "Malin") =>
        _client.PostAsJsonAsync("/api/auth/register", new { email, password, displayName });

    private async Task<WendUser?> Find(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        return await users.FindByEmailAsync(email);
    }

    [Test]
    public async Task Registering_creates_an_unconfirmed_account()
    {
        var response = await Register();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var user = await Find("new@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(user, Is.Not.Null);
            Assert.That(user!.EmailConfirmed, Is.False);
            Assert.That(user.DisplayName, Is.EqualTo("Malin"));
        });
    }

    [Test]
    public async Task Registering_emails_a_confirmation_link()
    {
        await Register();

        var sent = _factory.Email.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Email, Is.EqualTo("new@example.test"));
            Assert.That(sent.Link, Does.Contain("/verify?userId="));
            Assert.That(sent.Link, Does.Contain("&code="));
        });
    }

    [Test]
    public async Task Registering_a_taken_address_reports_the_same_generic_success()
    {
        await Register(email: "taken@example.test");
        var user = await Find("taken@example.test");
        await Confirm(user!.Id);
        _factory.Email.Sent.Clear();

        var response = await Register(email: "taken@example.test", displayName: "Impostor");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Registering_a_taken_address_neither_creates_nor_emails_anything()
    {
        await Register(email: "taken@example.test");
        var user = await Find("taken@example.test");
        await Confirm(user!.Id);
        _factory.Email.Sent.Clear();

        await Register(email: "taken@example.test", displayName: "Impostor");

        var stored = await Find("taken@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(stored!.DisplayName, Is.EqualTo("Malin"), "the existing account must be untouched");
            Assert.That(_factory.Email.Sent, Is.Empty, "a confirmed account must not be emailed");
        });
    }

    [Test]
    public async Task Registering_an_unconfirmed_address_resends_the_link()
    {
        await Register(email: "squatted@example.test");
        _factory.Email.Sent.Clear();

        var response = await Register(email: "squatted@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Email.Sent.Single().Email, Is.EqualTo("squatted@example.test"));
        });
    }

    [Test]
    public async Task A_blank_display_name_is_rejected()
    {
        var response = await Register(displayName: "   ");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task An_over_long_display_name_is_rejected()
    {
        var response = await Register(displayName: new string('x', 101));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Control_characters_are_stripped_from_the_display_name()
    {
        // A null and an escape character inside the name, written as C# escape sequences so they
        // survive copy-paste. Both must be stripped; the ordinary letters must not be.
        await Register(displayName: "Ma\u0000lin\u001B");

        var user = await Find("new@example.test");
        Assert.That(user!.DisplayName, Is.EqualTo("Malin"));
    }

    [Test]
    public async Task A_weak_password_is_rejected()
    {
        var response = await Register(password: "short1!");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // The oracle this endpoint is most likely to grow by accident: if the password policy is only
    // checked inside CreateAsync — which runs for a free address and not a taken one — then a
    // deliberately weak password answers 400 for free and 204 for taken, and one request per
    // address enumerates the whole user table. Validation order is the fix; this is the guard.
    [Test]
    public async Task A_weak_password_answers_the_same_for_a_taken_address_as_a_free_one()
    {
        await Register(email: "taken@example.test");

        var free = await Register(email: "free@example.test", password: "short1!");
        var taken = await Register(email: "taken@example.test", password: "short1!");

        Assert.That(taken.StatusCode, Is.EqualTo(free.StatusCode));
    }

    [Test]
    public async Task An_address_that_is_not_an_email_is_rejected()
    {
        var response = await Register(email: "not-an-email");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>Marks an account confirmed without going through the endpoint (Task 4 tests that).</summary>
    private async Task Confirm(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByIdAsync(userId);
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        await users.ConfirmEmailAsync(user!, token);
    }
}
```

- [ ] **Step 4 — run them and watch them fail**

```powershell
dotnet test --filter FullyQualifiedName~AuthRegisterTests
```

Expected: all 11 FAIL — `/api/auth/register` is not mapped, so the `/api/{**path}` catch-all
answers `404`.

- [ ] **Step 5 — create `Wend.Core/DisplayNames.cs`**

```csharp
namespace Wend.Core;

/// <summary>
/// Display names are user-controlled content that Slice 2b will render on *other users'* boards,
/// so a bad value is a stored-XSS vector across a trust boundary. Cleaning happens once, at write
/// time; escaping still happens at every interpolation. Both, not either.
/// </summary>
public static class DisplayNames
{
    /// <summary>Matches the column cap configured in WendDbContext.</summary>
    public const int MaxLength = 100;

    /// <summary>Strips control characters (including newlines, which break log lines) and trims.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var kept = value.Where(c => !char.IsControl(c)).ToArray();
        return new string(kept).Trim();
    }
}
```

- [ ] **Step 6 — create `Wend.Api/AuthEndpoints.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Wend.Core;

namespace Wend.Api;

public static class AuthEndpoints
{
    // RFC 5321's maximum path length — an outer bound before anything else looks at the value.
    private const int MaxEmailLength = 254;

    /// <param name="publicBaseUrl">
    /// Origin to build emailed links from, e.g. "https://wend.example". Null only in Development,
    /// where the request's host is used instead. See the Host-header note on SendConfirmationAsync.
    /// </param>
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group, string? publicBaseUrl)
    {
        // Anonymous by design — nobody is signed in while registering. These routes carry no
        // ICurrentUser guard, unlike every board route.
        group.MapPost("/register", async (RegisterRequest req, UserManager<WendUser> users,
            IAuthEmailSender email, HttpRequest http, ILoggerFactory loggerFactory) =>
        {
            var address = req.Email?.Trim() ?? "";
            var displayName = DisplayNames.Clean(req.DisplayName);
            var password = req.Password ?? "";

            // ─── Everything that depends ONLY on the caller's own input is checked first. ───
            // Refusing malformed input is safe: it says nothing about who is registered. But it
            // must happen BEFORE the existence lookup, or the 400 becomes an oracle — a caller
            // sending a deliberately weak password would get 400 for a free address (the policy
            // ran) and 204 for a taken one (it did not). One request per address, whole user
            // table enumerated. AuthRegisterTests guards this ordering; do not reorder.
            if (address.Length is 0 or > MaxEmailLength) return Results.BadRequest();
            if (!new EmailAddressAttribute().IsValid(address)) return Results.BadRequest();
            if (displayName.Length is 0 or > DisplayNames.MaxLength) return Results.BadRequest();

            var candidate = new WendUser { UserName = address, Email = address, DisplayName = displayName };
            foreach (var validator in users.PasswordValidators)
            {
                if (!(await validator.ValidateAsync(users, candidate, password)).Succeeded)
                    return Results.BadRequest();
            }

            // ─── From here on, every outcome answers 204. ───
            if (await users.FindByEmailAsync(address) is { } existing)
            {
                // An unconfirmed account gets a fresh link — that is also how a real owner
                // reclaims an address a bot registered first. A confirmed one is left alone.
                // Both answer exactly as a brand-new registration does.
                if (!existing.EmailConfirmed)
                    await SendConfirmationAsync(existing, users, email, http, publicBaseUrl);
                return Results.NoContent();
            }

            var result = await users.CreateAsync(candidate, password);
            if (!result.Succeeded)
            {
                // Past the validation above, the only failures left are a uniqueness race and
                // Identity's AllowedUserNameCharacters rule — and answering either with a 400
                // would leak existence. So: same 204, and a log line (error CODES only, never the
                // address) so a legitimately-unusable address is still diagnosable from the server.
                loggerFactory.CreateLogger("Wend.Api.AuthEndpoints")
                    .LogWarning("Registration rejected after validation: {Errors}",
                        string.Join("; ", result.Errors.Select(e => e.Code)));
                return Results.NoContent();
            }

            await SendConfirmationAsync(candidate, users, email, http, publicBaseUrl);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// Mints a confirmation token and emails a link to the SPA's /verify screen. The token is
    /// Base64Url-encoded because Identity's raw token is not URL-safe.
    ///
    /// The origin comes from configuration, NOT from the request. Building it from http.Host would
    /// mean an attacker who can set the Host header gets Wend to email a victim a genuine-looking
    /// link pointing at the attacker's server — handing over a live confirmation token. Development
    /// falls back to the request host because there is no configured origin on localhost.
    /// </summary>
    private static async Task SendConfirmationAsync(WendUser user, UserManager<WendUser> users,
        IAuthEmailSender email, HttpRequest http, string? publicBaseUrl)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var origin = publicBaseUrl?.TrimEnd('/') ?? $"{http.Scheme}://{http.Host}";
        var link = $"{origin}/verify" +
                   $"?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(code)}";
        await email.SendEmailConfirmationAsync(user.Email!, link);
    }
}

public record RegisterRequest(string Email, string Password, string DisplayName);
```

- [ ] **Step 7 — map the group in `Wend.Api/Program.cs`**

Beside the existing `Wend:Port` line near the top of the file, read the public origin:

```csharp
// Emailed links are built from this, never from the request's Host header — see
// AuthEndpoints.SendConfirmationAsync. Development has no configured origin and falls back to the
// request host, which on localhost is the only thing it can be.
var publicBaseUrl = builder.Configuration["Wend:PublicBaseUrl"];
if (publicBaseUrl is null && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "Wend:PublicBaseUrl is not configured. Set it via environment variables so confirmation "
        + "links cannot be forged through the Host header.");
```

Then add this line directly below `app.MapGroup("/api/boards").MapBoardEndpoints();`:

```csharp
app.MapGroup("/api/auth").MapAuthEndpoints(publicBaseUrl);
```

- [ ] **Step 8 — run the tests**

```powershell
dotnet test
```

Expected: **192 passed, 0 failed** (181 + 11).

- [ ] **Step 9 — commit**

```powershell
git add Wend.Core/DisplayNames.cs Wend.Api/AuthEndpoints.cs Wend.Api/Program.cs Wend.Tests/FakeAuthEmailSender.cs Wend.Tests/WendApiFactory.cs Wend.Tests/AuthRegisterTests.cs
git commit -m "Add enumeration-resistant registration"
```

---

## Task 4 — `POST /api/auth/verify`

Three outcomes, three status codes, because the screen in Task 8 has three accessible states.

**Interfaces produced:** `POST /api/auth/verify` taking `{ userId, code }` → `204` confirmed · `409` already confirmed · `400` expired/invalid.

- [ ] **Step 1 — write the failing tests**

Create `Wend.Tests/AuthVerifyTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class AuthVerifyTests
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

    /// <summary>Registers, then reads userId + code straight out of the emailed link.</summary>
    private async Task<(string UserId, string Code)> RegisterAndCaptureLink(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });
        var link = _factory.Email.Sent.Last().Link;
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"]!, query["code"]!);
    }

    private Task<HttpResponseMessage> Verify(string userId, string code) =>
        _client.PostAsJsonAsync("/api/auth/verify", new { userId, code });

    [Test]
    public async Task A_valid_link_confirms_the_account()
    {
        var (userId, code) = await RegisterAndCaptureLink("new@example.test");

        var response = await Verify(userId, code);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByIdAsync(userId);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(user!.EmailConfirmed, Is.True);
        });
    }

    [Test]
    public async Task A_reused_link_reports_already_confirmed()
    {
        var (userId, code) = await RegisterAndCaptureLink("new@example.test");
        await Verify(userId, code);

        var response = await Verify(userId, code);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task A_garbled_code_reports_an_expired_link()
    {
        var (userId, _) = await RegisterAndCaptureLink("new@example.test");

        var response = await Verify(userId, "!!!not-base64url!!!");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task An_unknown_user_id_reports_an_expired_link()
    {
        var (_, code) = await RegisterAndCaptureLink("new@example.test");

        var response = await Verify(Guid.NewGuid().ToString(), code);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task A_code_minted_for_another_account_is_refused()
    {
        var (_, victimCode) = await RegisterAndCaptureLink("victim@example.test");
        var (attackerId, _) = await RegisterAndCaptureLink("attacker@example.test");

        var response = await Verify(attackerId, victimCode);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var attacker = await users.FindByIdAsync(attackerId);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(attacker!.EmailConfirmed, Is.False);
        });
    }
}
```

- [ ] **Step 2 — run them and watch them fail**

```powershell
dotnet test --filter FullyQualifiedName~AuthVerifyTests
```

Expected: all 5 FAIL with `404` — `/api/auth/verify` is not mapped.

- [ ] **Step 3 — add the endpoint to `Wend.Api/AuthEndpoints.cs`**

Insert this inside `MapAuthEndpoints`, after the `/register` mapping and before `return group;`:

```csharp
        // POST, not GET, even though this arrives from an emailed link. Corporate mail scanners and
        // link-preview bots follow GET links automatically, so a GET that confirms would be fired by
        // a robot before the human ever clicked — and could confirm an address nobody opened. The
        // link therefore points at the SPA shell, which posts the code back from the browser.
        group.MapPost("/verify", async (VerifyRequest req, UserManager<WendUser> users) =>
        {
            if (req.UserId is not { Length: > 0 } id) return Results.BadRequest();
            if (await users.FindByIdAsync(id) is not { } user) return Results.BadRequest();

            // Identity's data-protector tokens are time-limited and stamp-bound, NOT single-use.
            // This check is what makes a replayed link resolve to "already verified" rather than
            // silently re-confirming — the user-visible single-use guarantee.
            if (user.EmailConfirmed) return Results.Conflict();

            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Code ?? ""));
            }
            catch (FormatException)
            {
                return Results.BadRequest();
            }

            var result = await users.ConfirmEmailAsync(user, token);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest();
        });
```

Add the request record beside `RegisterRequest` at the bottom of the file:

```csharp
public record VerifyRequest(string UserId, string Code);
```

- [ ] **Step 4 — run the tests**

```powershell
dotnet test
```

Expected: **197 passed, 0 failed** (192 + 5).

- [ ] **Step 5 — commit**

```powershell
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthVerifyTests.cs
git commit -m "Confirm email addresses from the emailed link"
```

---

## Task 5 — `POST /api/auth/resend-verification`

The escape hatch for an expired link, and the way a real owner reclaims a squatted address. Every
outcome answers identically.

**Interfaces produced:** `POST /api/auth/resend-verification` taking `{ email }` → always `204`.

- [ ] **Step 1 — write the failing tests**

Create `Wend.Tests/AuthResendTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class AuthResendTests
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

    private Task<HttpResponseMessage> Resend(string email) =>
        _client.PostAsJsonAsync("/api/auth/resend-verification", new { email });

    [Test]
    public async Task Resending_emails_a_fresh_link_to_an_unconfirmed_account()
    {
        await Register("waiting@example.test");
        _factory.Email.Sent.Clear();

        await Resend("waiting@example.test");

        Assert.That(_factory.Email.Sent.Single().Email, Is.EqualTo("waiting@example.test"));
    }

    [Test]
    public async Task Resending_to_a_confirmed_account_sends_nothing()
    {
        await Register("done@example.test");
        await ConfirmDirectly("done@example.test");
        _factory.Email.Sent.Clear();

        await Resend("done@example.test");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task Resending_to_an_unknown_address_sends_nothing()
    {
        await Resend("stranger@example.test");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task Every_resend_outcome_returns_the_same_response()
    {
        await Register("waiting@example.test");
        await Register("done@example.test");
        await ConfirmDirectly("done@example.test");

        var unconfirmed = await Resend("waiting@example.test");
        var confirmed = await Resend("done@example.test");
        var unknown = await Resend("stranger@example.test");
        var rubbish = await Resend("not-an-email");

        Assert.Multiple(() =>
        {
            Assert.That(unconfirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(rubbish.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    private async Task ConfirmDirectly(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        await users.ConfirmEmailAsync(user!, token);
    }
}
```

- [ ] **Step 2 — run them and watch them fail**

```powershell
dotnet test --filter FullyQualifiedName~AuthResendTests
```

Expected: all 4 FAIL — `/api/auth/resend-verification` is not mapped.

- [ ] **Step 3 — add the endpoint to `Wend.Api/AuthEndpoints.cs`**

Insert inside `MapAuthEndpoints`, after the `/verify` mapping and before `return group;`:

```csharp
        // Every branch — unconfirmed, confirmed, unknown, malformed — falls out of the same 204.
        // Note there is no `return BadRequest` for a bad address: telling the caller their input
        // was malformed is fine, but it is not worth a second response shape on an endpoint whose
        // whole job is to look identical from outside.
        group.MapPost("/resend-verification", async (ResendRequest req, UserManager<WendUser> users,
            IAuthEmailSender email, HttpRequest http) =>
        {
            var address = req.Email?.Trim() ?? "";
            if (address.Length is > 0 and <= MaxEmailLength &&
                await users.FindByEmailAsync(address) is { EmailConfirmed: false } user)
            {
                await SendConfirmationAsync(user, users, email, http);
            }

            return Results.NoContent();
        });
```

Add the request record at the bottom of the file:

```csharp
public record ResendRequest(string Email);
```

- [ ] **Step 4 — run the tests**

```powershell
dotnet test
```

Expected: **201 passed, 0 failed** (197 + 4).

- [ ] **Step 5 — commit**

```powershell
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthResendTests.cs
git commit -m "Let an unconfirmed account request a fresh verification link"
```

---

## Task 6 — Purge unverified accounts

Without this, one bot registration holds an address forever and `resend-verification` is the only
way out. Seven days, then the row goes.

**Interfaces produced:** `Wend.Core.UnverifiedAccounts.PurgeAsync(WendDbContext, DateTime cutoffUtc, CancellationToken)` → count deleted; `Wend.Core.UnverifiedAccounts.Window`.

- [ ] **Step 1 — write the failing tests**

Create `Wend.Tests/UnverifiedAccountPurgeTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class UnverifiedAccountPurgeTests
{
    private WendApiFactory _factory = null!;
    private IServiceScope _scope = null!;
    private WendDbContext _db = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _factory.CreateClient().Dispose();   // boots the app and applies migrations
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<WendDbContext>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _factory.Dispose();
    }

    private async Task<string> SeedAsync(bool confirmed, TimeSpan age)
    {
        var id = Guid.NewGuid().ToString();
        var email = $"{id}@example.test";
        _db.Users.Add(new WendUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = confirmed,
            DisplayName = "Test User",
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow - age,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    // Derived from the constant, never a hard-coded 7. A test that recomputes the window
    // independently still passes when the window changes — while testing the wrong boundary.
    private static TimeSpan Window => UnverifiedAccounts.Window;
    private DateTime Cutoff => DateTime.UtcNow - Window;

    [Test]
    public async Task An_unconfirmed_account_past_the_window_is_purged()
    {
        var id = await SeedAsync(confirmed: false, age: Window + TimeSpan.FromDays(1));

        await UnverifiedAccounts.PurgeAsync(_db, Cutoff);

        Assert.That(await _db.Users.AnyAsync(u => u.Id == id), Is.False);
    }

    [Test]
    public async Task An_unconfirmed_account_inside_the_window_survives()
    {
        var id = await SeedAsync(confirmed: false, age: Window - TimeSpan.FromDays(1));

        await UnverifiedAccounts.PurgeAsync(_db, Cutoff);

        Assert.That(await _db.Users.AnyAsync(u => u.Id == id), Is.True);
    }

    [Test]
    public async Task A_confirmed_account_is_never_purged()
    {
        var id = await SeedAsync(confirmed: true, age: Window + TimeSpan.FromDays(400));

        await UnverifiedAccounts.PurgeAsync(_db, Cutoff);

        Assert.That(await _db.Users.AnyAsync(u => u.Id == id), Is.True);
    }

    [Test]
    public async Task Purging_an_account_erases_its_boards()
    {
        var id = await SeedAsync(confirmed: false, age: Window + TimeSpan.FromDays(1));
        _db.Boards.Add(new Board { Title = "Doomed", OwnerId = id });
        await _db.SaveChangesAsync();

        await UnverifiedAccounts.PurgeAsync(_db, Cutoff);

        Assert.That(await _db.Boards.AnyAsync(b => b.OwnerId == id), Is.False);
    }
}
```

- [ ] **Step 2 — run them and watch them fail**

```powershell
dotnet test --filter FullyQualifiedName~UnverifiedAccountPurgeTests
```

Expected: build error — `UnverifiedAccounts` does not exist.

- [ ] **Step 3 — create `Wend.Core/UnverifiedAccounts.cs`**

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>
/// Registration creates an account that cannot log in. Left alone, one bot registration would hold
/// an address forever; purging returns it to whoever actually owns the mailbox.
///
/// The query is kept separate from the hosted service that calls it so it can be tested directly,
/// against real PostgreSQL, without waiting on a timer.
/// </summary>
public static class UnverifiedAccounts
{
    /// <summary>
    /// How long an account may sit unconfirmed before its address is released. Lives here rather
    /// than on the hosted service so the tests can pin the boundary to the same constant the
    /// production sweep uses — otherwise changing the window leaves the tests green while they
    /// measure a boundary nothing else has.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public static Task<int> PurgeAsync(WendDbContext db, DateTime cutoffUtc, CancellationToken ct = default) =>
        db.Users
            .Where(u => !u.EmailConfirmed && u.CreatedAt < cutoffUtc)
            .ExecuteDeleteAsync(ct);
}
```

- [ ] **Step 4 — run the tests**

```powershell
dotnet test
```

Expected: **205 passed, 0 failed** (201 + 4).

- [ ] **Step 5 — create `Wend.Api/UnverifiedAccountPurgeService.cs`**

```csharp
using Wend.Core;

namespace Wend.Api;

/// <summary>
/// Runs the unverified-account purge on a slow timer. Thin by design: the query it calls is in
/// Wend.Core and is what the tests exercise.
/// </summary>
public sealed class UnverifiedAccountPurgeService(
    IServiceScopeFactory scopes,
    ILogger<UnverifiedAccountPurgeService> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        // PeriodicTimer waits a full interval BEFORE its first tick. That is deliberate: every API
        // test boots this app and disposes it seconds later, so with a 6-hour interval the purge
        // never touches a test's throwaway database. Do not add an immediate first run.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();
                var cutoff = DateTime.UtcNow - UnverifiedAccounts.Window;
                var removed = await UnverifiedAccounts.PurgeAsync(db, cutoff, stoppingToken);
                if (removed > 0) log.LogInformation("Purged {Count} unverified accounts.", removed);
            }
            catch (Exception ex)
            {
                // A failed sweep must not take the web app down; the next tick tries again.
                log.LogError(ex, "Unverified-account purge failed.");
            }
        }
    }
}
```

- [ ] **Step 6 — register it in `Wend.Api/Program.cs`**

Add directly below the `AddSingleton<IAuthEmailSender>` line from Task 2:

```csharp
builder.Services.AddHostedService<UnverifiedAccountPurgeService>();
```

- [ ] **Step 7 — run the tests again**

```powershell
dotnet test
```

Expected: still **205 passed, 0 failed**. If this drops, the hosted service is firing during tests —
check the interval.

- [ ] **Step 8 — commit**

```powershell
git add Wend.Core/UnverifiedAccounts.cs Wend.Api/UnverifiedAccountPurgeService.cs Wend.Api/Program.cs Wend.Tests/UnverifiedAccountPurgeTests.cs
git commit -m "Purge unverified accounts after seven days"
```

---

## Task 7 — The registration screen

First frontend task. `main.js` learns to route on the path, and `js/auth/register/` follows the same
model/view/controller trio as `boards/`, `board/`, `card/` and `settings/`.

**No automated tests** — this repo has no JS harness. The manual script in Step 6 is the gate.

**Interfaces produced:** `createRegisterModel()` with `subscribe(fn)` and `submit({ email, password, displayName })`; `createRegisterView(root)` with `render(state)`, `focusHeading()`, `focusFirstError()`, `setBusy(busy)`, `bindActions(handlers)`; `createRegisterController(model, view, announce)`; `main.js` routing on `location.pathname`. Model state is `{ status: "editing" | "sending" | "sent", errors: string[], email? }`.

- [ ] **Step 1 — create `Wend.Api/wwwroot/js/auth/register/model.js`**

```js
import { api } from "../../api.js";

// State only: what was submitted, what came back. No DOM, no timers.
export function createRegisterModel() {
  let state = { status: "editing", errors: [] };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    async submit({ email, password, displayName }) {
      state = { status: "sending", errors: [] };
      notify();
      try {
        await api("/api/auth/register", {
          method: "POST",
          body: JSON.stringify({ email, password, displayName }),
        });
        // 204 for a new account AND for one that already exists — the server refuses to say
        // which, so the screen must not claim "account created" either.
        state = { status: "sent", errors: [], email };
      } catch (error) {
        state = {
          status: "editing",
          errors: error?.status === 400
            ? ["Check the form: we need a valid email address, a display name, and a password of at least 12 characters."]
            : ["Something went wrong. Please try again."],
        };
      }
      notify();
    },
  };
}
```

- [ ] **Step 2 — create `Wend.Api/wwwroot/js/auth/register/view.js`**

```js
import { escapeHtml } from "../../escape.js";

// Renders the registration form and its confirmation state. No logic; events via data-action.
export function createRegisterView(root) {
  let h = {};

  function render(state) {
    if (state.status === "sent") {
      root.innerHTML = `
        <div class="auth-view">
          <h2 class="auth-heading" tabindex="-1">Check your email</h2>
          <p>If <strong>${escapeHtml(state.email ?? "")}</strong> can be registered, we've sent it a link to confirm the address. The link lasts 24 hours.</p>
          <p>Nothing arrived? Check spam, then <button type="button" class="btn btn-ghost" data-action="resend">send it again</button>.</p>
        </div>`;
      return;
    }

    const errors = state.errors ?? [];
    root.innerHTML = `
      <div class="auth-view">
        <h2 class="auth-heading" tabindex="-1">Create your Wend account</h2>
        ${errors.length ? `
        <div class="auth-errors alert alert-danger" tabindex="-1">
          <p>We couldn't create your account:</p>
          <ul>${errors.map((e) => `<li>${escapeHtml(e)}</li>`).join("")}</ul>
        </div>` : ""}
        <form class="auth-form" data-action="submit">
          <label for="reg-name">Display name</label>
          <input id="reg-name" name="displayName" type="text" autocomplete="nickname"
            maxlength="100" required aria-describedby="hint-reg-name" />
          <p class="field-hint" id="hint-reg-name">What other people will see. You can change it later.</p>

          <label for="reg-email">Email</label>
          <input id="reg-email" name="email" type="email" autocomplete="email"
            maxlength="254" required aria-describedby="hint-reg-email" />
          <p class="field-hint" id="hint-reg-email">You'll sign in with this, and we'll send a confirmation link to it.</p>

          <!-- minlength mirrors the server's policy so the browser gives native, per-field,
               accessible feedback. The server's 400 is a lumped message with no field attribution,
               which is the one accessibility commitment this screen would otherwise miss. -->
          <label for="reg-password">Password</label>
          <input id="reg-password" name="password" type="password" autocomplete="new-password"
            minlength="12" required aria-describedby="hint-reg-password" />
          <p class="field-hint" id="hint-reg-password">At least 12 characters. A memorable phrase beats a short tangle of symbols.</p>

          <button type="submit" data-role="submit">Create account</button>
        </form>
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  // After a failed submit, focus lands on the error summary — not back at the top of the form,
  // and never on <body>. Note the summary is NOT role="alert": focus moves to it and the
  // controller announces it through #status, and doing all three makes most screen readers read
  // the same message twice.
  function focusFirstError() {
    const summary = root.querySelector(".auth-errors");
    if (summary) summary.focus();
    else focusHeading();
  }

  // Disabled while a request is in flight, so a double-clicked button can't send two
  // confirmation emails.
  function setBusy(busy) {
    const button = root.querySelector('[data-role="submit"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Creating account…" : "Create account";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="submit"]')) return;
      e.preventDefault();
      const data = new FormData(e.target);
      h.submit({
        displayName: data.get("displayName") ?? "",
        email: data.get("email") ?? "",
        password: data.get("password") ?? "",
      });
    });
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-action="resend"]')) h.resend();
    });
  }

  return { render, focusHeading, focusFirstError, setBusy, bindActions };
}
```

- [ ] **Step 3 — create `Wend.Api/wwwroot/js/auth/register/controller.js`**

```js
import { api } from "../../api.js";

// Wires the registration view: submits, announces every outcome, moves focus deliberately.
export function createRegisterController(model, view, announce) {
  let lastEmail = "";
  let seenFirstRender = false;

  view.bindActions({
    submit: (fields) => {
      lastEmail = fields.email;
      model.submit(fields);
    },
    resend: async () => {
      try {
        await api("/api/auth/resend-verification", {
          method: "POST",
          body: JSON.stringify({ email: lastEmail }),
        });
        announce("If that address needs confirming, we've sent another link.");
      } catch {
        announce("Couldn't send another link — please try again.");
      }
    },
  });

  model.subscribe((state) => {
    if (state.status === "sending") {
      view.setBusy(true);
      announce("Creating your account…");
      return;
    }

    view.render(state);
    view.setBusy(false);

    // The first render is the empty form on page load: announce nothing, and leave focus for the
    // skip link. Every later render is a submit result and gets both.
    if (!seenFirstRender) {
      seenFirstRender = true;
      return;
    }

    if (state.status === "sent") {
      view.focusHeading();
      announce("Check your email for a link to confirm your address.");
    } else if (state.errors?.length) {
      view.focusFirstError();
      announce(state.errors[0]);
    }
  });
}
```

- [ ] **Step 4 — route to it in `Wend.Api/wwwroot/js/main.js`**

Add these imports below the existing `settings` imports:

```js
import { createRegisterModel } from "./auth/register/model.js";
import { createRegisterView } from "./auth/register/view.js";
import { createRegisterController } from "./auth/register/controller.js";
```

Add this function directly above the final `showOverview();` line:

```js
// index.html's header belongs to the signed-in app. Left visible on an auth screen, Settings is a
// trap: it mounts the boards settings over the auth screen, and its Back goes to the board
// overview, which 401s. It is also the first thing after the skip link in the tab order, so a
// keyboard user meets it before the form they came for.
function hideAppChrome() {
  document.getElementById("settings-link").hidden = true;
}

function showRegister() {
  hideAppChrome();
  mount((root) => {
    const model = createRegisterModel();
    const view = createRegisterView(root);
    createRegisterController(model, view, announce);
  });
}
```

Then replace the final line `showOverview();` with:

```js
// The server renders the SPA shell for every non-API path, so the client owns routing. Auth
// screens are reached by URL because an emailed link has to land somewhere. Plan 4 replaces this
// with the real auth gate, which decides between the app and the login screen on boot.
switch (location.pathname) {
  case "/register":
    showRegister();
    break;
  default:
    showOverview(); // first paint: no forced focus, skip link is available
}
```

- [ ] **Step 5 — style the auth screens in `Wend.Api/wwwroot/css/app.css`**

Append to the end of the file:

```css
/* Auth screens — mobile-first: a single column that simply gets a max width on larger screens.
   Every custom property below is a real design-system token (checked against
   design-system/tokens/): the scale is --space-1..9, radii are --radius-sm/md/lg/xl/pill,
   --text-muted and --danger are colours, and --text-sm is a font size. */
.auth-view {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-4);
}

.auth-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.auth-form label {
  margin-block-start: var(--space-3);
}

.auth-form button[type="submit"] {
  margin-block-start: var(--space-4);
}

.field-hint {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--text-muted);
}

/* Looks come from the design system's .alert .alert-danger; this only trims the list spacing
   and takes the focus ring the controller's focusFirstError() depends on. */
.auth-errors ul {
  margin-block-end: 0;
}

@media (min-width: 768px) {
  .auth-view {
    max-width: 32rem;
    margin-inline: auto;
  }
}
```

> Note the two families sharing a `--text-` prefix: `--text`, `--text-muted` and `--text-faint` are
> **colours**, while `--text-sm` … `--text-4xl` are **font sizes**. The views also lean on the
> design system's own component classes — `.alert .alert-danger` for the error summary, `.btn
> .btn-ghost` for the inline resend control — rather than re-inventing them here. Do not edit
> anything under `design-system/`; it is vendored and read-only.

- [ ] **Step 6 — walk it manually**

```powershell
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet run --project Wend.Api
```

Open `http://127.0.0.1:5174/register` with a **hard reload** (DevTools open, "Disable cache" on),
then check every line:

- [ ] Keyboard only, from page load: `Tab` goes skip link → Display name → Email → Password → Create account. **The Settings button is not in the tab order** — `hideAppChrome()` removed it. Every stop has a visible focus ring.
- [ ] Submit the empty form — the browser's own `required` validation stops it, naming the field. No JS error in the console.
- [ ] Submit with a 6-character password. The **browser** blocks it with a per-field message (that is `minlength`), and no request appears in the Network tab.
- [ ] Now exercise the server-error path, which is what the error summary exists for: set the Network tab to Offline, submit a valid form. The summary appears, **focus moves to it**, and `#status` announces it (watch the element in the Elements panel). Go back Online afterwards.
- [ ] Submit a valid form. The heading becomes "Check your email", focus is on the heading.
- [ ] `%LOCALAPPDATA%\Wend\auth-emails.log` has a new entry with a `/verify?userId=…&code=…` link.
- [ ] Register the **same** address again from a fresh `/register` load. The screen says exactly the same thing — it never says the address is taken.
- [ ] Register that same taken address once more with a 6-character password, this time via DevTools console so `minlength` can't intervene:
      `await fetch('/api/auth/register',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email:'<the taken one>',password:'short1!',displayName:'x'})})`
      then repeat with an address you have never used. **Both must return the same status.** A 400 for one and a 204 for the other is the enumeration oracle from the stress test, live.
- [ ] Double-click "Create account" fast. Only **one** entry appears in the log.
- [ ] Narrow the viewport to ~375px (device toolbar). No horizontal scrolling, the form stays one column, and the submit button measures **≥44×44 px** (`$0.getBoundingClientRect()` with it selected).
- [ ] Check the `.field-hint` contrast: with a hint selected, confirm `--text-muted` against the page background clears **4.5:1** (small text — 3:1 is not enough here).
- [ ] The Network tab shows requests to **no** host other than `127.0.0.1:5174`.
- [ ] Delete `auth-emails.log` when you're done — it is developer scratch holding real addresses and live tokens, and nothing rotates it.

- [ ] **Step 7 — commit**

```powershell
git add Wend.Api/wwwroot/js/auth Wend.Api/wwwroot/js/main.js Wend.Api/wwwroot/css/app.css
git commit -m "Add the registration screen"
```

---

## Task 8 — The verify landing screen

Where the emailed link lands. Three states, all of them first-class accessible screens.

**Interfaces produced:** `createVerifyModel()` with `subscribe(fn)`, `confirm({ userId, code })` and `noLink()`; `createVerifyView(root)` with `render(state)`, `focusHeading()`, `setBusy(busy)`, `bindActions(handlers)`; `createVerifyController(model, view, announce, { userId, code })`. Model state is `{ status: "checking" | "confirmed" | "already" | "nothing" | "expired" | "failed" }`; the view also renders `"sent"`, which only the controller sets after a successful resend.

- [ ] **Step 1 — create `Wend.Api/wwwroot/js/auth/verify/model.js`**

```js
import { api } from "../../api.js";

// Maps the endpoint's three status codes onto the three states the screen renders.
export function createVerifyModel() {
  let state = { status: "checking" };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    // Arrived at /verify with no link parameters — a reload after confirming, or a hand-typed
    // URL. Deliberately NOT routed through confirm(), which would post empty values, collect a
    // 400 and tell the user their link expired when they never presented one.
    noLink() {
      state = { status: "nothing" };
      notify();
    },
    async confirm({ userId, code }) {
      try {
        await api("/api/auth/verify", {
          method: "POST",
          body: JSON.stringify({ userId, code }),
        });
        state = { status: "confirmed" };
      } catch (error) {
        if (error?.status === 409) state = { status: "already" };
        else if (error?.status === 400) state = { status: "expired" };
        else state = { status: "failed" };
      }
      notify();
    },
  };
}
```

- [ ] **Step 2 — create `Wend.Api/wwwroot/js/auth/verify/view.js`**

```js
import { escapeHtml } from "../../escape.js";

// Renders the four verify states. Every one is a real screen with a heading — never a raw error.
export function createVerifyView(root) {
  let h = {};

  const BODIES = {
    checking: `
      <h2 class="auth-heading" tabindex="-1">Confirming your address…</h2>
      <p>One moment.</p>`,
    confirmed: `
      <h2 class="auth-heading" tabindex="-1">Address confirmed</h2>
      <p>Your email address is confirmed. Signing in arrives in the next release.</p>`,
    already: `
      <h2 class="auth-heading" tabindex="-1">Already confirmed</h2>
      <p>This address was confirmed already, so this link has done its job. You don't need to do anything.</p>`,
    // A link with no parameters is not the same as a broken one — most often it is someone
    // reloading this page after confirming, and telling them their link expired would be a lie.
    nothing: `
      <h2 class="auth-heading" tabindex="-1">Nothing to confirm here</h2>
      <p>Open the confirmation link from your email. If you no longer have it, request a new one below.</p>`,
    expired: `
      <h2 class="auth-heading" tabindex="-1">This link has expired</h2>
      <p>Confirmation links last 24 hours and can only be used once. Request a new one below.</p>`,
    failed: `
      <h2 class="auth-heading" tabindex="-1">Something went wrong</h2>
      <p>We couldn't confirm your address just now. Request a new link below.</p>`,
    sent: `
      <h2 class="auth-heading" tabindex="-1">Check your email</h2>
      <p>If that address needs confirming, we've sent a new link. It lasts 24 hours.</p>`,
  };

  // The register link is not decoration. An account left unconfirmed for a week is deleted, so the
  // person most likely to be holding a stale link is the one whose account no longer exists — and
  // for them the resend form silently does nothing, forever. They need the other door.
  const RESEND_FORM = `
    <form class="auth-form" data-action="resend">
      <label for="verify-email">Email</label>
      <input id="verify-email" name="email" type="email" autocomplete="email"
        maxlength="254" required />
      <button type="submit" data-role="resend">Send a new link</button>
    </form>
    <p>Link more than a week old? The account may have been removed —
      <a href="/register">create it again</a>.</p>`;

  function render(state) {
    const needsResend = state.status === "expired" || state.status === "failed"
      || state.status === "nothing";
    root.innerHTML = `
      <div class="auth-view">
        ${BODIES[state.status] ?? BODIES.failed}
        ${needsResend ? RESEND_FORM : ""}
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  function setBusy(busy) {
    const button = root.querySelector('[data-role="resend"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Sending…" : "Send a new link";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="resend"]')) return;
      e.preventDefault();
      h.resend(new FormData(e.target).get("email") ?? "");
    });
  }

  return { render, focusHeading, setBusy, bindActions };
}
```

- [ ] **Step 3 — create `Wend.Api/wwwroot/js/auth/verify/controller.js`**

```js
import { api } from "../../api.js";

const ANNOUNCEMENTS = {
  checking: "Confirming your address.",
  confirmed: "Your email address is confirmed.",
  already: "This address was already confirmed. There's nothing to do.",
  nothing: "Nothing to confirm. Open the link from your email, or request a new one below.",
  expired: "This link has expired. Request a new one below.",
  failed: "We couldn't confirm your address. Request a new link below.",
};

// Wires the verify screen: reads the link, confirms once, announces the outcome, offers a resend.
export function createVerifyController(model, view, announce, { userId, code } = {}) {
  view.bindActions({
    resend: async (email) => {
      view.setBusy(true);
      try {
        await api("/api/auth/resend-verification", {
          method: "POST",
          body: JSON.stringify({ email }),
        });
        // Not the expired screen with a note bolted on — a resend that worked deserves its own
        // heading, and "This link has expired" above a success message reads as a failure.
        view.render({ status: "sent" });
        view.focusHeading();
        announce("If that address needs confirming, we've sent a new link.");
      } catch {
        announce("Couldn't send a new link — please try again.");
        view.setBusy(false);
      }
    },
  });

  // Settle the no-link case BEFORE subscribing, so that arrival renders and announces once
  // instead of flashing "Confirming…" at a user who presented nothing to confirm.
  if (!userId || !code) model.noLink();

  model.subscribe((state) => {
    view.render(state);
    // EVERY state moves focus to its heading and says what happened — including "checking".
    // This screen is reached by clicking a link in an email specifically to receive an async
    // result, so the house "first paint does not force focus" rule is wrong here: without this
    // a screen-reader user gets silence, with focus nowhere, until the request settles.
    view.focusHeading();
    announce(ANNOUNCEMENTS[state.status] ?? ANNOUNCEMENTS.failed);
  });

  if (userId && code) model.confirm({ userId, code });
}
```

- [ ] **Step 4 — route to it in `Wend.Api/wwwroot/js/main.js`**

Add these imports beside the register imports:

```js
import { createVerifyModel } from "./auth/verify/model.js";
import { createVerifyView } from "./auth/verify/view.js";
import { createVerifyController } from "./auth/verify/controller.js";
```

Add this function beside `showRegister()`:

```js
function showVerify() {
  hideAppChrome();
  const params = new URLSearchParams(location.search);
  const userId = params.get("userId") ?? "";
  const code = params.get("code") ?? "";

  // Drop the live token out of the address bar and the history entry as soon as it is read. It
  // still reached the server in the POST body, but it no longer sits in the URL a user might
  // screenshot, bookmark, or paste into a support chat.
  history.replaceState(null, "", "/verify");

  mount((root) => {
    const model = createVerifyModel();
    const view = createVerifyView(root);
    createVerifyController(model, view, announce, { userId, code });
  });
}
```

Add the case to the switch:

```js
  case "/verify":
    showVerify();
    break;
```

- [ ] **Step 5 — walk it manually**

```powershell
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet run --project Wend.Api
```

Register a fresh address at `http://127.0.0.1:5174/register`, then copy the link from
`%LOCALAPPDATA%\Wend\auth-emails.log` and check every line:

- [ ] Pasting the link confirms the address: heading reads "Address confirmed", **focus is on the heading**, `#status` announced it.
- [ ] Before it settles, the "Confirming your address…" heading is focused and announced — throttle the network to Slow 3G if it is too fast to see. Silence with focus nowhere is the bug this check exists for.
- [ ] The address bar now reads `/verify` with **no** `userId` or `code`.
- [ ] Pressing Back does not restore a URL containing the code.
- [ ] **Reload that confirmed page (F5).** It must read "Nothing to confirm here" — *not* "This link has expired", which would tell a user who just succeeded that they failed.
- [ ] Reloading the original link (paste it again) gives "Already confirmed" — not a raw error, not a second confirmation.
- [ ] Visiting `/verify?userId=x&code=y` gives "This link has expired" **with** a working resend form and a visible "create it again" link to `/register`; focus is on the heading.
- [ ] Follow that `/register` link — it lands on the registration screen, which is the only route out for someone whose unconfirmed account was purged.
- [ ] Visiting `/verify` bare gives "Nothing to confirm here" once — it must not flash "Confirming…" first, and must not announce twice.
- [ ] Submitting the resend form for an unconfirmed address adds a new entry to the log and the screen changes to "Check your email" — not "This link has expired" with a success note under it. The screen says the same thing whatever address you type.
- [ ] Keyboard only, all states: every control reachable, visible focus ring, no focus dropping to `<body>`, and **no Settings button in the tab order**.
- [ ] At ~375px: no horizontal scrolling.
- [ ] Network tab: requests to `127.0.0.1:5174` only — **no third-party host**, so no `Referer` can carry the token off-origin.

- [ ] **Step 6 — commit**

```powershell
git add Wend.Api/wwwroot/js/auth/verify Wend.Api/wwwroot/js/main.js
git commit -m "Add the verify landing screen with its expired-link state"
```

---

## Task 9 — Record what this plan deliberately left open

Five known gaps ship with this plan — two security deferrals and three launch gates. All of them are
written down so Plans 8 and 9 inherit them instead of rediscovering them.

- [ ] **Step 1 — add them to `docs/backlog.md`**

Append to the open-items section, matching the file's existing style:

```markdown
- **Register leaks account existence through timing** (Slice 2a Plan 3). `POST /api/auth/register`
  returns the same 204 whether or not the address is taken, but the taken path skips password
  hashing and so returns measurably faster. The spec requires equalised timing for *login*; register
  was left as-is because the app is unreachable from another machine until deployment. **Plan 8
  (security hardening) must close this** — dummy-hash the skipped path, as login will.
- **`/api/auth/*` is not rate limited** (Slice 2a Plan 3). Register and resend-verification both
  trigger outbound email, so both are email-bombing vectors, and register is a credential-stuffing
  surface. Deferred to Plan 8 per the spec's sequencing. **This is a launch gate: Plan 9 must not
  deploy before Plan 8 lands.**
- **The registration form gives no Art. 13 notice** (Slice 2a Plan 3). Wend now collects an email
  address and a display name from members of the public, and the spec makes the privacy policy and
  terms a launch deliverable linked *from the registration form*. It is lawful today only because
  registration is unreachable. **Launch gate for Plan 9: policy and terms exist, and the form links
  to them, before public sign-up opens.**
- **Verify tokens travel in a query string** (Slice 2a Plan 3). The SPA strips them from the address
  bar with `history.replaceState`, and Kestrel logs nothing at Information, but essentially every
  reverse proxy logs query strings by default. **Plan 9 must exclude `/verify` query strings from
  access logging**, per the spec's "path-logging exclusion extends to query strings".
- **Inactive-account retention has no stance** (Slice 2a Plan 3). The spec allows setting one at plan
  time *or* explicitly deferring; this plan defers it. It is a legal-posture decision that belongs
  with the privacy policy, so **Plan 9 decides it** — a retention period, or a documented decision
  to keep accounts indefinitely.
```

- [ ] **Step 2 — commit**

```powershell
git add docs/backlog.md
git commit -m "Record the register timing side channel and the missing rate limits"
```

---

## Definition of done

- [ ] `dotnet build` — 0 warnings.
- [ ] `dotnet test` — **205 passed, 0 failed**. Check the printed number; a silently dropped test
      file is the failure mode this count exists to catch.
- [ ] Every manual check in Tasks 7 and 8 ticked.
- [ ] `git log --oneline` shows one commit per task, none with a co-author trailer.
- [ ] The PR description says: **this branch adds a column with a default and destroys no data**
      (unlike Plan 2's), and **boards still answer 401 — login is Plan 4.**

---

## Review notes for the other owner

This plan was stress-tested on 2026-08-10 across security, privacy, accessibility and loopholes; ten
findings were folded in before you saw it. Read these three things first; everything else is ordinary.

1. **The register handler's validation order** (`AuthEndpoints.cs`). Two properties, and the second
   is the one the stress test caught. First: every path involving an existing address returns the
   same `204` as a new registration — a `Conflict()` or a "that email is taken" anywhere in there is
   a bug. Second, and easier to miss: **email format and password policy must be checked *before*
   the existence lookup.** If the policy only runs inside `CreateAsync`, then a caller sending a
   deliberately weak password gets `400` for a free address and `204` for a taken one, which
   enumerates the entire user table one request at a time. The draft you would have reviewed had
   exactly that hole, and every enumeration test still passed.
2. **The decisions table** above — password minimum 12 with no complexity rules, a 24-hour verify
   token, a 7-day purge window, and `POST /api/auth/verify` instead of the spec's illustrative
   `GET /verify`. These are the spec's "confirm at plan time" items being confirmed. Argue with them
   here, before there is code to unpick.
3. **The five deferrals in Task 9.** All are real, all are written into the backlog, and three of
   them — rate limiting, the privacy notice, and query-string log exclusion — are hard gates on
   deployment. Plan 9 cannot go public until they are closed.

Two consequences of the stress test are worth knowing before you read the code, because both look
like over-engineering until you know why:

- **`Program.cs` now refuses to start outside Development.** The only `IAuthEmailSender` writes to a
  local file, so a deployed Wend would accept registrations, send nothing, and quietly build a file
  of addresses paired with live tokens. Throwing at startup is deliberate; Plan 9 removes it by
  supplying a real provider.
- **Emailed links come from `Wend:PublicBaseUrl`, not the request's `Host` header.** Building them
  from `http.Host` lets anyone who can set that header have Wend email a victim a genuine-looking
  link pointing at the attacker's server, handing over a live token. Harmless on localhost, which is
  exactly why it had to be fixed before it became invisible.
