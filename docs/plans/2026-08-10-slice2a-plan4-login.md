# Wend Slice 2a — Plan 4: Login, session & the auth gate

> **For agentic workers:** use `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` to implement this task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let a confirmed account sign in — cookie authentication, lockout, `/login`, `/logout`,
`/me`, the real `ICurrentUser`, and the frontend gate that finally puts a board back on the screen.

**Architecture:** Identity gains a `SignInManager` and the application cookie on top of Plan 3's
headless `AddIdentityCore`. `NullCurrentUser` is replaced by `HttpContextCurrentUser`, reading the
signed-in principal, so all 34 repository signatures and all 28 per-handler guards stay exactly as
they are. `RequireAuthorization()` goes in front of the board-family endpoints as defence in depth.
The existing 205 tests keep working through a `Test` authentication scheme registered inside
`WendApiFactory`, which also means they now run through the real `HttpContextCurrentUser` and a real
`ClaimsPrincipal` instead of injecting past them; a factory flag turns that scheme off so a small
suite can drive the genuine cookie path.

**Tech stack:** `net10.0`, EF Core 10, Npgsql `10.0.3`,
`Microsoft.AspNetCore.Identity.EntityFrameworkCore` `10.0.9`, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`,
native PostgreSQL 17, vanilla-JS MVC (no build step).

**Reference:** parent spec [`2026-07-08-wend-slice2a-accounts-design.md`](../2026-07-08-wend-slice2a-accounts-design.md) ·
plan design [`2026-08-10-wend-slice2a-plan4-login-design.md`](../2026-08-10-wend-slice2a-plan4-login-design.md) ·
previous plan [`2026-08-10-slice2a-register-verify.md`](./2026-08-10-slice2a-register-verify.md).

---

## Scope

The spec's sequencing (§ *Sequencing*, items 1–8) numbers the remaining plans. Plans 1 and 2
delivered item 1 (*Foundation*); Plan 3 delivered item 2 (*Register + verify*). **This plan is item
3, "Login / session"** — and stops there.

| In | Out (and which plan owns it) |
|---|---|
| Cookie auth, `SignInManager`, lockout, the security stamp interval | Forgot / reset password — **Plan 5** |
| `POST /api/auth/login` (generic response, constant time) | Change email / password, **remember-me** — **Plan 6** |
| The unconfirmed-account nudge email | Account deletion — **Plan 7** |
| `GET /api/auth/me`, `POST /api/auth/logout` | Rate limiting, antiforgery, HTTPS/HSTS, headers — **Plan 8** |
| `HttpContextCurrentUser` + `RequireAuthorization()` | Host, real email provider, privacy policy / terms / DPA — **Plan 9** |
| The login screen, the `/login` route, the auth gate, the logout control | Preserving unsaved input across a mid-session 401 — **backlogged in Task 8** |
| The link out of Plan 3's verify success state | |

**This is the plan where `/api/boards` answers 200 again.** It has answered 401 to every request
since Plan 2, which was accepted, not a regression. When Task 7 lands, a confirmed user can sign in
and see their boards for the first time.

---

## Global constraints

- Target `net10.0`. EF Core 10 throughout; **no Docker, no Testcontainers, no hypervisor.**
- **No new NuGet packages.** `Microsoft.AspNetCore.Identity.EntityFrameworkCore` `10.0.9` already
  supplies `SignInManager`, `ConfigureApplicationCookie` and `AddIdentityCookies`.
- **No new configuration keys.** `Wend:PublicBaseUrl` (Plan 3) is the only one the nudge needs.
- `dotnet build` must end at **0 warnings**; `dotnet test` green at the stated count for every task.
- **No third-party resources on any page.** Same-origin CSS and JS only — no font, icon CDN or
  analytics tag. The login screen is subject to this exactly as the verify screen is.
- Mobile-first CSS: baseline styles target the smallest screen, `min-width` queries layer up at
  768px / 1024px. **No `max-width` queries.**
- `escapeHtml` at **every** user-content interpolation in a view. No exceptions.
- **Never log an email address, a password, or a token.** A lockout is logged by user id; a failed
  login is not logged at all until Plan 8 gives it a rate-limiting context.
- Commits: one per task, authored under your own account, **no co-author trailer and no AI
  attribution** (house rule). Run every command from the repo root.
- Branch: `feature/slice2a-plan4-login`, opened as a PR for the other owner to review and merge.

---

## Decisions locked at plan time

These resolve the design doc's *Open items*. Each was **verified against the .NET 10 Identity source**
(`dotnet/aspnetcore` v10.0.1) rather than inferred from older `AddIdentity` guidance — Plan 3 shipped
three defects that came from exactly that kind of inference.

| Decision | Value | Why |
|---|---|---|
| **Cookie configuration API** | `builder.Services.ConfigureApplicationCookie(...)` | Confirmed present in Identity Core 10's shipped public API, and it works with `AddIdentityCore` + `AddIdentityCookies` — it configures `CookieAuthenticationOptions` for `IdentityConstants.ApplicationScheme` by name. |
| **The nudge uses `UserManager.CheckPasswordAsync`** | **not** `SignInManager.CheckPasswordSignInAsync` | `CheckPasswordSignInAsync` opens with the same `PreSignInCheck` that produced `NotAllowed` in the first place, so on an unconfirmed account it would return `NotAllowed` forever and **never verify the password**. `UserManager.CheckPasswordAsync` is the pure check. Getting this wrong yields a nudge that silently never sends. |
| **The nudge branch does its own lockout accounting** | `IsLockedOutAsync` before, `AccessFailedAsync` / `ResetAccessFailedCountAsync` after | `PreSignInCheck` returns `NotAllowed` **before** it evaluates lockout, so Identity never counts a failure against an unconfirmed account. Without this branch doing it by hand, an unconfirmed account accepts **unlimited password guesses** — the nudge feature would open a hole the rest of the plan closes. |
| **The nudge is dispatched from `Response.OnCompleted`** | not `Task.Run`, not inline `await` | Awaiting the send inline makes the "correct password on an unconfirmed account" branch measurably slower than every other outcome, which is the timing oracle this plan promises to close. `OnCompleted` is the framework's own after-the-response hook — no orphaned task, no lost DI scope. The token and link are built *before* the response, so only the network call is deferred. |
| **Dummy hash** | A `static readonly` hash built once from a directly-constructed `PasswordHasher<WendUser>` | `IPasswordHasher<TUser>` is registered **scoped**, so a singleton holding one would fail startup DI validation — the same class of failure as Plan 3's missing `AddDataProtection()`. A static built from a directly-constructed hasher needs no container and exists exactly once per process. |
| **The security-stamp validator does not touch the test scheme** | Confirmed | It is wired through the *cookie's* `OnValidatePrincipal`, so `TestAuthHandler` is unaffected by `ValidationInterval` and needs no security-stamp claim. |
| **`ValidationInterval = TimeSpan.Zero` renews the cookie every request** | Accepted | The validator sets `ShouldRenew = true` whenever it re-validates, so every authenticated response carries a `Set-Cookie`. With `SlidingExpiration = true` that is ordinary sliding behaviour, not a bug. The cost is one user lookup and one cookie write per authenticated request, bought deliberately for immediate revocation. |
| **Generic login failure = `Results.Unauthorized()`** | 401, empty body | Every failure returns the identical empty 401. The rest of the API already answers with bodyless statuses, and an empty body is the easiest shape to keep identical across five branches. |
| **`RequireAuthorization()` is applied via a prefix-less group** | `app.MapGroup("").RequireAuthorization()` | Only `BoardEndpoints` is mapped as a group today; `ListEndpoints`, `CardEndpoints`, `LabelEndpoints` and `ChecklistItemEndpoints` map absolute paths straight onto `app`, and their paths span two prefixes (`/api/boards/{id}/lists` and `/api/lists/{id}`) so no single prefix fits. **Fallback if an empty prefix is rejected:** add `.RequireAuthorization()` to each `Map*` call inside those four files and record the deviation in the PR body. Task 1's metadata test tells you which happened. |

---

## Notes for the implementer

- **Start PostgreSQL first.** The service is `Manual` start: `Start-Service postgresql-x64-17`.
  Connection refused or an EF timeout means the service is stopped, not a code bug.
- **Stop the app before building.** The process is `Wend.Api`, *not* `Wend`:
  `Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force`. Skipping this gives
  MSB3021/3027 copy-lock errors that look like test failures.
- **Test counts are load-bearing.** A paste-driven edit once silently dropped three tests behind a
  green suite. Every task states its expected total; check the number `dotnet test` prints against
  it before committing. **Baseline: 205.** This plan ends at **231**.
- **Task 1 changes how every existing test authenticates.** It is the riskiest task in the plan and
  it does nothing else on purpose. If the 205 do not come back green, the fault is in the scheme
  wiring, not in the test that failed.
- **`TestAuthHandler` belongs to `Wend.Tests` and must never appear in `Wend.Api`.** A handler that
  authenticates whoever asks is an auth bypass the moment it is reachable from the shipping project.
- **There is no migration in this plan.** Nothing about the schema changes; Identity's lockout and
  security-stamp columns have existed since Plan 2.
- **Frontend tasks have no automated tests.** This repo has no JS test harness (no `package.json`,
  no Biome — `.editorconfig` only), exactly as through all of Slice 1. Tasks 6 and 7 are verified by
  the scripted manual checks written into them. Run those checks; do not skip them because the C#
  suite is green.
- **Hard-reload every browser check.** `UseStaticFiles` sends no `Cache-Control`, so a normal reload
  serves stale ES modules from an earlier `:5174` session.
- **A bare `<button>` is 28px high**, under the 44×44 minimum. Use `.btn` plus a variant. This was
  caught twice during Plan 3.

---

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Api/HttpContextCurrentUser.cs` | **new** | The real `ICurrentUser`, reading the request principal |
| `Wend.Api/ICurrentUser.cs` | modify | Drop `NullCurrentUser` — nothing registers it after Task 1 |
| `Wend.Api/AuthEndpoints.cs` | modify | `/login`, `/logout`, `/me`, the dummy hash, the nudge |
| `Wend.Api/Program.cs` | modify | `AddSignInManager`, cookies, lockout, stamp interval, `RequireAuthorization()` |
| `Wend.Api/wwwroot/index.html` | modify | The logout control; both header controls start hidden |
| `Wend.Api/wwwroot/js/main.js` | modify | The auth gate, the `/login` route, `showAppChrome`, logout |
| `Wend.Api/wwwroot/js/auth/login/{model,view,controller}.js` | **new** | The login screen |
| `Wend.Api/wwwroot/js/auth/verify/view.js` | modify | Success states link on to `/login` |
| `Wend.Api/wwwroot/js/auth/register/view.js` | modify | Link back to `/login` |
| `Wend.Api/wwwroot/css/app.css` | modify | The help block (mobile-first) |
| `Wend.Tests/TestAuthHandler.cs` | **new** | The test authentication scheme |
| `Wend.Tests/WendApiFactory.cs` | modify | Register the scheme; the `useTestAuth` flag |
| `Wend.Tests/OwnershipTests.cs` | modify | Replace the `NullCurrentUser` test |
| `Wend.Tests/AuthConfigurationTests.cs` | **new** | Cookie, lockout, stamp, authorization metadata |
| `Wend.Tests/AuthLoginTests.cs` | **new** | Login, enumeration resistance, lockout |
| `Wend.Tests/AuthNudgeTests.cs` | **new** | The unconfirmed-account nudge |
| `Wend.Tests/AuthSessionTests.cs` | **new** | `/me`, `/logout`, the form-encoded refusal |
| `Wend.Tests/RealCookieAuthTests.cs` | **new** | The genuine cookie walk, behind the canary |
| `docs/backlog.md` | modify | Input preservation; the Plan 8 gates |

---

## Task 1 — Cookie authentication, the real `ICurrentUser`, and the test scheme

The riskiest task in the plan: every one of the 205 existing tests changes how it authenticates.
Nothing else happens here.

**Interfaces produced:** `Wend.Api.HttpContextCurrentUser`; `Wend.Tests.TestAuthHandler.SchemeName`
(`"Test"`); `new WendApiFactory(useTestAuth: false)`; the application cookie named `wend.session`.

- [ ] **Step 1 — write the failing tests**

Create `Wend.Tests/AuthConfigurationTests.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Wend.Tests;

/// <summary>
/// The auth configuration is security policy expressed as settings, so it is asserted rather than
/// eyeballed. Every value here is one the design doc locked; changing one should break a test.
/// </summary>
public class AuthConfigurationTests
{
    private WendApiFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _factory.CreateClient().Dispose();   // boots the app
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public void An_unconfirmed_account_cannot_sign_in()
    {
        var options = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.That(options.SignIn.RequireConfirmedAccount, Is.True);
    }

    [Test]
    public void Five_failures_lock_an_account_for_fifteen_minutes()
    {
        var options = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.Lockout.MaxFailedAccessAttempts, Is.EqualTo(5));
            Assert.That(options.Lockout.DefaultLockoutTimeSpan, Is.EqualTo(TimeSpan.FromMinutes(15)));
            // Without this a brand-new account — the one an attacker reaches first — is exempt.
            Assert.That(options.Lockout.AllowedForNewUsers, Is.True);
        });
    }

    [Test]
    public void The_session_cookie_is_locked_down()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Multiple(() =>
        {
            Assert.That(options.Cookie.Name, Is.EqualTo("wend.session"));
            Assert.That(options.Cookie.HttpOnly, Is.True);
            Assert.That(options.Cookie.SameSite, Is.EqualTo(SameSiteMode.Lax));
            Assert.That(options.SlidingExpiration, Is.True);
            Assert.That(options.ExpireTimeSpan, Is.EqualTo(TimeSpan.FromDays(7)));
            // Development only. Always outside it — asserted by reading the environment guard in
            // Program.cs at review, because the test host pins Development.
            Assert.That(options.Cookie.SecurePolicy, Is.EqualTo(CookieSecurePolicy.SameAsRequest));
        });
    }

    [Test]
    public void The_security_stamp_is_revalidated_on_every_request()
    {
        var options = _factory.Services
            .GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;

        // Zero, not the 30-minute default: a password reset (Plan 5) and an account deletion
        // (Plan 7) must evict a live session on its NEXT request, not within half an hour.
        Assert.That(options.ValidationInterval, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Every_board_family_endpoint_requires_authorization()
    {
        // The per-handler guards would return 401 anyway, so a status-code test cannot tell whether
        // RequireAuthorization() is actually in front. Endpoint metadata can.
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } path
                && path.StartsWith("/api/", StringComparison.Ordinal)
                && !path.StartsWith("/api/auth/", StringComparison.Ordinal)
                && path != "/api/health"
                && !path.Contains("{**path}", StringComparison.Ordinal))
            .ToList();

        Assert.That(endpoints, Is.Not.Empty, "no board-family endpoints were found to check");
        Assert.That(endpoints.Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null),
            Is.Empty, "these endpoints are missing RequireAuthorization()");
    }
}
```

Replace the `NullCurrentUser` test in `Wend.Tests/OwnershipTests.cs` — delete this:

```csharp
    [Test]
    public void No_current_user_means_no_user_id()
    {
        Assert.That(new NullCurrentUser().UserId, Is.Null);
    }
```

and put these two in its place:

```csharp
    [Test]
    public void An_anonymous_request_has_no_current_user()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        Assert.That(new HttpContextCurrentUser(accessor).UserId, Is.Null);
    }

    [Test]
    public void A_signed_in_request_yields_the_principals_user_id()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-42")], "Test");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        Assert.That(new HttpContextCurrentUser(accessor).UserId, Is.EqualTo("user-42"));
    }
```

Add these usings to the top of `OwnershipTests.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
```

- [ ] **Step 2 — run them and watch them fail**

```powershell
dotnet test --filter FullyQualifiedName~AuthConfigurationTests
```

Expected: build error — `HttpContextCurrentUser` does not exist, and `SignInManager` is not
registered.

- [ ] **Step 3 — create `Wend.Api/HttpContextCurrentUser.cs`**

```csharp
using System.Security.Claims;

namespace Wend.Api;

/// <summary>
/// The signed-in user, read from the request principal. Identity issues the user id as
/// ClaimTypes.NameIdentifier (IdentityOptions.ClaimsIdentity.UserIdClaimType's default), which is
/// also what the test scheme issues, so both paths land here identically.
///
/// The IsAuthenticated check is not redundant: a request that failed authentication still carries
/// an anonymous ClaimsPrincipal, and reading a claim off it would be reading nothing very quietly.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            return principal?.Identity?.IsAuthenticated == true
                ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;
        }
    }
}
```

- [ ] **Step 4 — drop `NullCurrentUser` from `Wend.Api/ICurrentUser.cs`**

Delete the whole class and its doc comment, leaving only the interface. Update the interface's
comment, which still promises a Plan 3 that has been and gone:

```csharp
namespace Wend.Api;

/// <summary>
/// The signed-in user for this request, or null when nobody is signed in. Lives in Wend.Api
/// because "current" is an HTTP concept — the domain takes an owner id as an ordinary argument.
/// Implemented by HttpContextCurrentUser (Plan 4); tests reach the same seam through the request
/// principal their test scheme issues, not by replacing this service.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
}
```

- [ ] **Step 5 — wire authentication in `Wend.Api/Program.cs`**

Add `using Microsoft.AspNetCore.Authentication.Cookies;` to the top of the file.

Replace the `AddScoped<ICurrentUser, NullCurrentUser>()` line and its comment with:

```csharp
// The signed-in user comes off the request principal now. Every repository call still takes an
// explicit ownerId; this is only where that id is read from.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
```

Add `.AddSignInManager()` to the existing Identity registration, between
`.AddEntityFrameworkStores<WendDbContext>()` and `.AddDefaultTokenProviders()`:

```csharp
    .AddEntityFrameworkStores<WendDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
```

Inside the `AddIdentityCore` options lambda, below the existing password block, add:

```csharp
        // A confirmed address is the gate on signing in at all — this is what makes Plan 3's
        // verification step mean something rather than being a formality.
        options.SignIn.RequireConfirmedAccount = true;

        // Small enough to blunt credential stuffing, short enough that a real user who mistyped is
        // not locked out for the afternoon. AllowedForNewUsers matters because otherwise a
        // freshly-registered account — the one an attacker reaches first — is exempt.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
```

Then, immediately after the `AddTransient<EmailConfirmationTokenProvider<WendUser>>()` line:

```csharp
// Cookie authentication. AddIdentityCookies supplies the application cookie that AddIdentityCore
// deliberately left out in Plan 3; no login-redirect events are configured because .NET 10's cookie
// handler already answers 401/403 for JSON endpoints, and Wend has no server-rendered login page to
// redirect to — the client owns routing.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    // A name that does not announce the stack to anyone reading response headers.
    options.Cookie.Name = "wend.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;

    // Dev runs plain HTTP on 127.0.0.1:5174. CookieSecurePolicy.Always there means the browser
    // silently DROPS the cookie: login answers 204, the next request is anonymous, and it reads as
    // a session bug rather than a config one. Always everywhere else, where HTTPS is required.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;

    // Non-persistent: every login in this plan issues a session cookie that dies with the browser.
    // Plan 6 adds remember-me as a deliberate opt-in. ExpireTimeSpan still bounds the ticket.
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

// Zero, not the 30-minute default. The stamp is re-checked on every authenticated request, so a
// password reset (Plan 5) and an account deletion (Plan 7) evict a live session on its NEXT
// request. The cost is one user lookup and one Set-Cookie per authenticated response, bought
// deliberately: a cache interval would turn those promises into "within half an hour".
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
```

- [ ] **Step 6 — add the middleware and the authorization group**

In the same file, directly after `app.UseStaticFiles();`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

Then replace the five endpoint-mapping lines. `BoardEndpoints` already maps as a group; the other
four map absolute paths straight onto `app`, and their paths span two prefixes, so they go into a
prefix-less group that carries nothing but the policy:

```csharp
// RequireAuthorization() in front, the 28 per-handler ICurrentUser guards behind. The attribute is
// what a future endpoint inherits for free; the guard is what the compiler enforces. An empty
// prefix adds no path segment — these routes keep the URLs they have always had.
var authed = app.MapGroup("").RequireAuthorization();
authed.MapGroup("/api/boards").MapBoardEndpoints();
authed.MapListEndpoints();
authed.MapCardEndpoints();
authed.MapLabelEndpoints();
authed.MapChecklistItemEndpoints();

app.MapGroup("/api/auth").MapAuthEndpoints(publicBaseUrl);
```

If `MapGroup("")` is rejected at startup, take the fallback from *Decisions locked*: add
`.RequireAuthorization()` to each `Map*` call inside the four endpoint files, and record the
deviation in the PR body.

- [ ] **Step 7 — create `Wend.Tests/TestAuthHandler.cs`**

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Wend.Tests;

/// <summary>
/// Authenticates every request as WendApiFactory.CurrentUser.UserId, or as nobody when that is
/// null. This exists so the API tests can keep saying "act as user B" in one line while real
/// authorization runs in front of the endpoints — and, unlike the ICurrentUser override it
/// replaces, it makes those tests exercise the real HttpContextCurrentUser and a real principal.
///
/// It lives in Wend.Tests and must never move to Wend.Api: a scheme that authenticates whoever
/// asks is an auth bypass the moment it is reachable from the shipping project.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TestCurrentUser currentUser)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (currentUser.UserId is not { Length: > 0 } id)
            return Task.FromResult(AuthenticateResult.NoResult());

        // NameIdentifier because that is what Identity issues and what HttpContextCurrentUser
        // reads. Any other claim type here would make the tests green against a seam the app
        // does not actually use.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

- [ ] **Step 8 — rewire `Wend.Tests/WendApiFactory.cs`**

Add `using Microsoft.AspNetCore.Authentication;` to the top.

Replace the class summary's second paragraph (the one describing the `ICurrentUser` override) with:

```csharp
/// Authorization runs for real, so tests authenticate through a Test scheme that issues a ticket
/// for CurrentUser.UserId — set it to act as somebody else, or null to act anonymously. Pass
/// useTestAuth: false to get the genuine Identity cookie scheme instead (see RealCookieAuthTests).
```

Give the class a constructor and keep the field beside `_dbName`:

```csharp
    private readonly bool _useTestAuth;

    public WendApiFactory(bool useTestAuth = true) => _useTestAuth = useTestAuth;
```

Replace the whole `ConfigureTestServices` block at the end of `ConfigureWebHost` with:

```csharp
        builder.ConfigureTestServices(services =>
        {
            // The file-writing dev sender, swapped for one that records in memory.
            services.AddSingleton<IAuthEmailSender>(Email);

            if (!_useTestAuth) return;

            // AddAuthentication(scheme) sets DefaultScheme through a Configure action, and
            // ConfigureTestServices runs after the app's own registration, so this one wins.
            services.AddSingleton(CurrentUser);
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });
```

In `ConfigureClient`, the trailing `CurrentUser.UserId = DefaultUserId;` line stays, but its
`NOTE` comment gains one sentence:

```csharp
        // NOTE: this runs on EVERY CreateClient() call, so create the client once and switch
        // CurrentUser.UserId afterwards — calling CreateClient() again silently reverts to the
        // default user, and an isolation test would then pass for the wrong reason. With
        // useTestAuth: false this dial does nothing; those tests sign in over HTTP instead.
```

- [ ] **Step 9 — run the whole suite**

```powershell
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test
```

Expected: **211 passed, 0 failed** (205 baseline − 1 deleted + 2 replacements + 5 new). If a large
block of the original 205 fails, the fault is the scheme wiring in Step 8, not the tests.

- [ ] **Step 10 — commit**

```powershell
git add Wend.Api/HttpContextCurrentUser.cs Wend.Api/ICurrentUser.cs Wend.Api/Program.cs Wend.Tests/TestAuthHandler.cs Wend.Tests/WendApiFactory.cs Wend.Tests/OwnershipTests.cs Wend.Tests/AuthConfigurationTests.cs
git commit -m "Add cookie authentication and read the current user from the request"
```

---

## Task 2 — `POST /api/auth/login`

One generic 401 for every failure, and equal work on every path. **The response shape is the
security property here**, exactly as it was for register in Plan 3.

**Interfaces produced:** `POST /api/auth/login` taking `{ email, password }` → `204` with the
session cookie, or `401` with an empty body; `Wend.Api.LoginRequest`.

- [ ] **Step 1 — write the failing tests**

Create `Wend.Tests/AuthLoginTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// Login's security property is that its failures are indistinguishable. Most tests here compare
/// one failure against another rather than asserting a specific code, because the moment two
/// failures differ, the endpoint enumerates the user table.
/// </summary>
public class AuthLoginTests
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

    private Task<HttpResponseMessage> Login(string email, string password = GoodPassword) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>Creates an account and confirms it, bypassing the endpoints (they have their own tests).</summary>
    private async Task<WendUser> Account(string email, bool confirmed = true)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = new WendUser { UserName = email, Email = email, DisplayName = "Malin" };
        await users.CreateAsync(user, GoodPassword);
        if (confirmed)
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            await users.ConfirmEmailAsync(user, token);
        }
        return user;
    }

    [Test]
    public async Task A_confirmed_account_signs_in_and_gets_a_session_cookie()
    {
        await Account("malin@example.test");

        var response = await Login("malin@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(response.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith("wend.session")),
                Is.True, "no session cookie was issued");
        });
    }

    [Test]
    public async Task A_wrong_password_is_refused()
    {
        await Account("malin@example.test");

        var response = await Login("malin@example.test", "not the right passphrase");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task An_unknown_address_answers_exactly_as_a_wrong_password_does()
    {
        await Account("malin@example.test");

        var wrongPassword = await Login("malin@example.test", "not the right passphrase");
        var unknown = await Login("nobody@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(unknown.StatusCode, Is.EqualTo(wrongPassword.StatusCode));
            Assert.That(unknown.Content.Headers.ContentLength ?? 0,
                Is.EqualTo(wrongPassword.Content.Headers.ContentLength ?? 0));
        });
    }

    [Test]
    public async Task An_unconfirmed_account_answers_exactly_as_a_wrong_password_does()
    {
        await Account("waiting@example.test", confirmed: false);
        await Account("malin@example.test");

        var wrongPassword = await Login("malin@example.test", "not the right passphrase");
        var unconfirmed = await Login("waiting@example.test");

        Assert.That(unconfirmed.StatusCode, Is.EqualTo(wrongPassword.StatusCode));
    }

    [Test]
    public async Task An_unconfirmed_account_is_not_signed_in_even_with_the_right_password()
    {
        await Account("waiting@example.test", confirmed: false);

        var response = await Login("waiting@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.Headers.Contains("Set-Cookie"), Is.False, "an unconfirmed account was given a session");
        });
    }

    [Test]
    public async Task Five_failures_lock_the_account_out()
    {
        await Account("malin@example.test");

        for (var i = 0; i < 5; i++) await Login("malin@example.test", "wrong");

        // The correct password now, which would otherwise succeed.
        var response = await Login("malin@example.test");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_locked_out_account_answers_exactly_as_a_wrong_password_does()
    {
        await Account("locked@example.test");
        await Account("malin@example.test");
        for (var i = 0; i < 5; i++) await Login("locked@example.test", "wrong");

        var wrongPassword = await Login("malin@example.test", "not the right passphrase");
        var lockedOut = await Login("locked@example.test");

        // "Your account is locked" would confirm the account exists — the same leak wearing a
        // helpful face. The user learns this from the help block on the login screen instead.
        Assert.That(lockedOut.StatusCode, Is.EqualTo(wrongPassword.StatusCode));
    }
}
```

- [ ] **Step 2 — run them and watch them fail**

```powershell
dotnet test --filter FullyQualifiedName~AuthLoginTests
```

Expected: all 7 FAIL with `404` — `/api/auth/login` is not mapped, so the `/api/{**path}` catch-all
answers.

- [ ] **Step 3 — add the dummy hash to `Wend.Api/AuthEndpoints.cs`**

Beside the existing `MaxEmailLength` constant at the top of the class:

```csharp
    // An unknown address must cost the same as a real one. Without this, "no such user" returns in
    // the time of a database read while a real user costs a full password hash — a timing oracle
    // that enumerates the user table however generic the response body is.
    //
    // Built from a directly-constructed hasher rather than the container's: IPasswordHasher<TUser>
    // is registered SCOPED, so a singleton holding one fails startup DI validation. The verify call
    // in the handler still uses the injected hasher; only this fixed hash is built here.
    private static readonly WendUser DummyUser = new()
    {
        UserName = "unknown@example.invalid",
        Email = "unknown@example.invalid",
    };

    private static readonly string DummyPasswordHash =
        new PasswordHasher<WendUser>().HashPassword(DummyUser, "the password of an account that does not exist");
```

- [ ] **Step 4 — add the endpoint**

Insert inside `MapAuthEndpoints`, after the `/resend-verification` mapping and before `return group;`:

```csharp
        // Anonymous, like the rest of this group bar /me and /logout. Every failure below answers
        // with the same empty 401 — unknown address, wrong password, unconfirmed account and
        // locked-out account alike. A response that distinguishes them enumerates the user table.
        group.MapPost("/login", async (LoginRequest req, SignInManager<WendUser> signIn,
            UserManager<WendUser> users, IPasswordHasher<WendUser> hasher) =>
        {
            var address = req.Email?.Trim() ?? "";
            var password = req.Password ?? "";

            if (await users.FindByEmailAsync(address) is not { } user)
            {
                // Do the work anyway, then answer as everyone else does.
                hasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, password);
                return Results.Unauthorized();
            }

            var result = await signIn.PasswordSignInAsync(
                user, password, isPersistent: false, lockoutOnFailure: true);

            // isPersistent: false — the cookie dies with the browser session. Remember-me is Plan 6,
            // and until it exists an opt-in nobody asked for is not the safe default.
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
```

Add the request record beside the others at the bottom of the file:

```csharp
public record LoginRequest(string Email, string Password);
```

- [ ] **Step 5 — run the tests**

```powershell
dotnet test
```

Expected: **218 passed, 0 failed** (211 + 7).

- [ ] **Step 6 — commit**

```powershell
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthLoginTests.cs
git commit -m "Sign confirmed accounts in with a generic, constant-time login"
```

---

## Task 3 — The unconfirmed-account nudge

A real user who never verified gets a fresh link instead of a dead end — without handing anyone who
knows an address a one-request email bomb.

**Read the two Identity behaviours in *Decisions locked* before writing this.** Both are
counter-intuitive and both are load-bearing: `SignInManager.CheckPasswordSignInAsync` cannot verify
a password for an unconfirmed account, and Identity never counts a failed attempt against one.

**Interfaces produced:** no new surface — `/api/auth/login` gains a side effect on one branch.

- [ ] **Step 1 — write the failing tests**

Create `Wend.Tests/AuthNudgeTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// The nudge exists so a user who never verified is not stuck at a generic failure forever. It must
/// fire ONLY for someone holding the right password: PasswordSignInAsync answers NotAllowed for an
/// unconfirmed account without looking at the password at all, so the naive version would let
/// anyone who knows an address bomb its inbox.
/// </summary>
public class AuthNudgeTests
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

    private Task<HttpResponseMessage> Login(string email, string password = GoodPassword) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task Account(string email, bool confirmed)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = new WendUser { UserName = email, Email = email, DisplayName = "Malin" };
        await users.CreateAsync(user, GoodPassword);
        if (confirmed)
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            await users.ConfirmEmailAsync(user, token);
        }
    }

    private async Task<bool> IsLockedOut(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        return await users.IsLockedOutAsync(user!);
    }

    [Test]
    public async Task The_right_password_on_an_unconfirmed_account_sends_a_fresh_link()
    {
        await Account("waiting@example.test", confirmed: false);
        _factory.Email.Sent.Clear();

        await Login("waiting@example.test");

        var sent = _factory.Email.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Email, Is.EqualTo("waiting@example.test"));
            Assert.That(sent.Link, Does.Contain("/verify?userId="));
        });
    }

    [Test]
    public async Task A_wrong_password_on_an_unconfirmed_account_sends_nothing()
    {
        await Account("waiting@example.test", confirmed: false);
        _factory.Email.Sent.Clear();

        await Login("waiting@example.test", "not the right passphrase");

        Assert.That(_factory.Email.Sent, Is.Empty, "anyone knowing the address could bomb this inbox");
    }

    [Test]
    public async Task A_confirmed_account_is_never_nudged()
    {
        await Account("malin@example.test", confirmed: true);
        _factory.Email.Sent.Clear();

        await Login("malin@example.test");
        await Login("malin@example.test", "not the right passphrase");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task An_unconfirmed_account_still_locks_out_after_five_wrong_passwords()
    {
        // Identity returns NotAllowed from PreSignInCheck BEFORE it evaluates lockout, so it never
        // counts a failure against an unconfirmed account. Without the handler doing that itself,
        // this branch would accept unlimited password guesses.
        await Account("waiting@example.test", confirmed: false);

        for (var i = 0; i < 5; i++) await Login("waiting@example.test", "wrong");

        Assert.That(await IsLockedOut("waiting@example.test"), Is.True);
    }

    [Test]
    public async Task A_locked_out_unconfirmed_account_is_not_nudged()
    {
        await Account("waiting@example.test", confirmed: false);
        for (var i = 0; i < 5; i++) await Login("waiting@example.test", "wrong");
        _factory.Email.Sent.Clear();

        await Login("waiting@example.test");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }
}
```

- [ ] **Step 2 — run them and watch them fail**

```powershell
dotnet test --filter FullyQualifiedName~AuthNudgeTests
```

Expected: 4 FAIL (the two "sends nothing" tests pass already — nothing sends anything yet).

- [ ] **Step 3 — extend the login handler in `Wend.Api/AuthEndpoints.cs`**

Replace the whole `/login` mapping from Task 2 with this. Three parameters are new
(`IAuthEmailSender`, `HttpRequest`, `HttpResponse`); everything above `if (result.Succeeded)` is
unchanged from Task 2 and is repeated here so the handler can be read in one piece:

```csharp
        group.MapPost("/login", async (LoginRequest req, SignInManager<WendUser> signIn,
            UserManager<WendUser> users, IPasswordHasher<WendUser> hasher, IAuthEmailSender email,
            HttpRequest http, HttpResponse response) =>
        {
            var address = req.Email?.Trim() ?? "";
            var password = req.Password ?? "";

            if (await users.FindByEmailAsync(address) is not { } user)
            {
                hasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, password);
                return Results.Unauthorized();
            }

            var result = await signIn.PasswordSignInAsync(
                user, password, isPersistent: false, lockoutOnFailure: true);

            if (result.Succeeded) return Results.NoContent();

            // NotAllowed is the unconfirmed case. Identity returns it from PreSignInCheck BEFORE it
            // looks at the password AND before it evaluates lockout, which has two consequences and
            // both are load-bearing:
            //
            //   * SignInManager.CheckPasswordSignInAsync runs that same PreSignInCheck, so it would
            //     answer NotAllowed forever and never verify anything. UserManager.CheckPasswordAsync
            //     is the pure password check and the only one that works here.
            //   * Lockout never increments on this path, so without the accounting below an
            //     unconfirmed account accepts unlimited password guesses.
            if (result.IsNotAllowed && !await users.IsLockedOutAsync(user))
            {
                if (await users.CheckPasswordAsync(user, password))
                {
                    await users.ResetAccessFailedCountAsync(user);

                    // Build the link now — it needs the scoped UserManager — but send it AFTER the
                    // response. Awaiting a transactional provider inline would make this branch
                    // measurably slower than every other outcome, which is the timing oracle this
                    // endpoint exists to avoid.
                    var link = await BuildConfirmationLinkAsync(user, users, http, publicBaseUrl);
                    response.OnCompleted(async () =>
                        await email.SendEmailConfirmationAsync(user.Email!, link));
                }
                else
                {
                    await users.AccessFailedAsync(user);
                }
            }

            return Results.Unauthorized();
        });
```

- [ ] **Step 4 — split the link builder out of `SendConfirmationAsync`**

The nudge needs the link without sending it. Refactor the existing private helper into two, keeping
its doc comment on the builder where the Host-header reasoning belongs:

```csharp
    /// <summary>
    /// Mints a confirmation token and builds the link to the SPA's /verify screen. The token is
    /// Base64Url-encoded because Identity's raw token is not URL-safe.
    ///
    /// The origin comes from configuration, NOT from the request. Building it from http.Host would
    /// mean an attacker who can set the Host header gets Wend to email a victim a genuine-looking
    /// link pointing at the attacker's server — handing over a live confirmation token. Development
    /// falls back to the request host because there is no configured origin on localhost.
    /// </summary>
    private static async Task<string> BuildConfirmationLinkAsync(WendUser user,
        UserManager<WendUser> users, HttpRequest http, string? publicBaseUrl)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var origin = publicBaseUrl?.TrimEnd('/') ?? $"{http.Scheme}://{http.Host}";
        return $"{origin}/verify" +
               $"?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(code)}";
    }

    /// <summary>Builds the link and sends it. Used by register and resend, which have no timing
    /// commitment to keep — register's side channel is Plan 8's, recorded in the backlog.</summary>
    private static async Task SendConfirmationAsync(WendUser user, UserManager<WendUser> users,
        IAuthEmailSender email, HttpRequest http, string? publicBaseUrl)
    {
        var link = await BuildConfirmationLinkAsync(user, users, http, publicBaseUrl);
        await email.SendEmailConfirmationAsync(user.Email!, link);
    }
```

- [ ] **Step 5 — run the tests**

```powershell
dotnet test
```

Expected: **223 passed, 0 failed** (218 + 5).

**If `The_right_password_on_an_unconfirmed_account_sends_a_fresh_link` fails with an empty `Sent`
list**, the cause is `Response.OnCompleted` not having run before the assertion. It runs when the
response completes, which is before `PostAsJsonAsync` returns — but if that proves flaky under the
test host, replace the callback with an awaited send and record the deviation plus the reinstated
timing side channel in the PR body and the backlog. Do not delete the test.

- [ ] **Step 6 — commit**

```powershell
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthNudgeTests.cs
git commit -m "Email a fresh confirmation link when an unverified account signs in correctly"
```

---

## Task 4 — `GET /api/auth/me` and `POST /api/auth/logout`

The two endpoints the frontend gate is built on, plus the test that guards the reasoning behind
deferring antiforgery to Plan 8.

**Interfaces produced:** `GET /api/auth/me` → `200 { displayName, email }` or `401`;
`POST /api/auth/logout` → `204`, authorized.

- [ ] **Step 1 — write the failing tests**

Create `Wend.Tests/AuthSessionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class AuthSessionTests
{
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

    [Test]
    public async Task Me_returns_the_signed_in_users_name_and_address()
    {
        var response = await _client.GetAsync("/api/auth/me");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("displayName").GetString(), Is.EqualTo("Default Test User"));
            Assert.That(body.GetProperty("email").GetString(), Is.EqualTo("default@example.test"));
        });
    }

    [Test]
    public async Task Me_is_401_when_nobody_is_signed_in()
    {
        // The gate's boot check depends on this exact answer: it is the signal to mount login, not
        // an error to report.
        _factory.CurrentUser.UserId = null;

        var response = await _client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Logging_out_succeeds_for_a_signed_in_user()
    {
        var response = await _client.PostAsync("/api/auth/logout", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Logging_out_is_refused_for_an_anonymous_caller()
    {
        // An anonymous logout endpoint is a free CSRF target: it costs an attacker nothing and a
        // victim their session.
        _factory.CurrentUser.UserId = null;

        var response = await _client.PostAsync("/api/auth/logout", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_form_encoded_login_is_refused()
    {
        // This is the guard on the reasoning that lets antiforgery wait for Plan 8: /api/auth/*
        // binds JSON only, and an HTML form cannot send application/json, so a cross-site form
        // POST cannot log anyone in. If someone later adds form binding, this test is what tells
        // them they have opened login-CSRF.
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("email", "default@example.test"),
            new KeyValuePair<string, string>("password", "correct horse battery staple"),
        ]);

        var response = await _client.PostAsync("/api/auth/login", form);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
    }
}
```

- [ ] **Step 2 — run them and watch them fail**

```powershell
dotnet test --filter FullyQualifiedName~AuthSessionTests
```

Expected: the `/me` and `/logout` tests FAIL with `404`. `A_form_encoded_login_is_refused` may pass
already — that is fine, it is a regression guard, not a driver.

**If it fails with `400` rather than `415`,** minimal APIs rejected the body at a different layer.
The property under test is "not processed as a login", so change the expected status to what the
framework actually returns, keep the comment, and note the value in the PR body.

- [ ] **Step 3 — add both endpoints to `Wend.Api/AuthEndpoints.cs`**

Add `using System.Security.Claims;` to the top of the file. Insert inside `MapAuthEndpoints`, after
the `/login` mapping and before `return group;`:

```csharp
        // The SPA's boot check. Its 401 is an ordinary expected answer — the signal to mount the
        // login screen — not a failure to report to the user.
        group.MapGet("/me", async (UserManager<WendUser> users, ClaimsPrincipal principal) =>
            await users.GetUserAsync(principal) is { } user
                ? Results.Ok(new { displayName = user.DisplayName, email = user.Email })
                : Results.Unauthorized())
            .RequireAuthorization();

        group.MapPost("/logout", async (SignInManager<WendUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
```

- [ ] **Step 4 — run the tests**

```powershell
dotnet test
```

Expected: **228 passed, 0 failed** (223 + 5).

- [ ] **Step 5 — commit**

```powershell
git add Wend.Api/AuthEndpoints.cs Wend.Tests/AuthSessionTests.cs
git commit -m "Add the session endpoints the frontend gate reads"
```

---

## Task 5 — The real-cookie walk

Everything so far has been tested through the `Test` scheme. This task proves the genuine Identity
cookie path works, because a test scheme that is never bypassed means the cookie middleware ships
untested.

**Interfaces produced:** none — this task adds coverage only.

- [ ] **Step 1 — write the failing tests**

Create `Wend.Tests/RealCookieAuthTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Wend.Tests;

/// <summary>
/// The browser's path, end to end, with no test scheme in sight: register, confirm, sign in, use
/// the cookie, sign out. WebApplicationFactory's client keeps cookies by default, so the session
/// flows exactly as it does in a browser.
/// </summary>
public class RealCookieAuthTests
{
    private const string GoodPassword = "correct horse battery staple";

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
        // The canary, and deliberately the first test in the file. WendApiFactory seeds a default
        // user and points CurrentUser at it on every CreateClient(), so a suite constructed without
        // useTestAuth: false is authenticated before it does anything — and every assertion below
        // would pass while testing nothing. This repo has been bitten by that shape twice.
        var response = await _client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Boards_are_refused_without_a_session()
    {
        var response = await _client.GetAsync("/api/boards");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Register_confirm_sign_in_use_and_sign_out()
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "walker@example.test", password = GoodPassword, displayName = "Malin" });

        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Single().Link).Query);
        var confirmed = await _client.PostAsJsonAsync("/api/auth/verify",
            new { userId = query["userId"], code = query["code"] });

        var signedIn = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "walker@example.test", password = GoodPassword });

        // No header is set by hand anywhere in this test: the cookie the login response issued is
        // what carries the session from here.
        var boards = await _client.GetAsync("/api/boards");
        var me = await _client.GetAsync("/api/auth/me");

        var signedOut = await _client.PostAsync("/api/auth/logout", null);
        var afterLogout = await _client.GetAsync("/api/boards");

        Assert.Multiple(() =>
        {
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "verify");
            Assert.That(signedIn.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "login");
            Assert.That(boards.StatusCode, Is.EqualTo(HttpStatusCode.OK), "boards with a session");
            Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK), "me with a session");
            Assert.That(signedOut.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "logout");
            Assert.That(afterLogout.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "boards after logout");
        });
    }
}
```

- [ ] **Step 2 — run them**

```powershell
dotnet test --filter FullyQualifiedName~RealCookieAuthTests
```

Expected: all 3 PASS — Tasks 1–4 already built everything this exercises. **This is the one task in
the plan whose tests are not expected to fail first**, because it adds no production code. If
`The_test_scheme_really_is_off` fails with `200`, the `useTestAuth` flag in Task 1 Step 8 is not
being honoured; fix that before trusting any other result in this file.

- [ ] **Step 3 — run the whole suite**

```powershell
dotnet test
```

Expected: **231 passed, 0 failed** (228 + 3).

- [ ] **Step 4 — commit**

```powershell
git add Wend.Tests/RealCookieAuthTests.cs
git commit -m "Cover the real cookie session from register through sign-out"
```

---

## Task 6 — The login screen

The MVC trio, matching `auth/register/` and `auth/verify/` exactly. **No automated tests** — the
scripted checks in Step 5 are the verification, and they are not optional.

**Interfaces produced:** `createLoginModel()`, `createLoginView(root)`,
`createLoginController(model, view, announce, { onSignedIn })`.

- [ ] **Step 1 — create `Wend.Api/wwwroot/js/auth/login/model.js`**

```js
import { api } from "../../api.js";

// State only: what was submitted, what came back, how many times in a row it failed. No DOM,
// no timers. The failure count lives here rather than in the controller because it is state.
export function createLoginModel() {
  let state = { status: "editing", errors: [], failures: 0 };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    async submit({ email, password }) {
      const failures = state.failures;
      state = { status: "sending", errors: [], failures };
      notify();
      try {
        await api("/api/auth/login", {
          method: "POST",
          body: JSON.stringify({ email, password }),
        });
        state = { status: "signedIn", errors: [], failures: 0 };
      } catch (error) {
        // The server answers one generic 401 for a wrong password, an unknown address, an
        // unconfirmed account and a locked-out one alike, so this message must not guess which.
        // The help block after three tries is what covers the last two.
        state = {
          status: "editing",
          failures: failures + 1,
          errors: [
            error?.status === 401
              ? "That email address and password don't match an account."
              : "Something went wrong. Please try again.",
          ],
        };
      }
      notify();
    },
  };
}
```

- [ ] **Step 2 — create `Wend.Api/wwwroot/js/auth/login/view.js`**

```js
// Renders the login form, its error summary and the after-three-tries help. No logic; events
// via data-action. Nothing here interpolates user content, so there is no escapeHtml call — the
// only dynamic strings are the model's own fixed messages.
export function createLoginView(root) {
  let h = {};

  // Shown only after three consecutive failures. The server cannot tell the user they are locked
  // out or unverified without confirming the account exists, so this says all three things at
  // once, to everyone, and leaks nothing.
  const HELP = `
    <div class="auth-help" tabindex="-1">
      <p>Still not working? One of these usually explains it:</p>
      <ul>
        <li>Several wrong tries in a row pause sign-in for about fifteen minutes.</li>
        <li>A new account needs its email address confirmed first — check your inbox for the link.</li>
        <li>Forgotten the password? Password reset arrives in the next release.</li>
      </ul>
    </div>`;

  function render(state) {
    const errors = state.errors ?? [];
    root.innerHTML = `
      <div class="auth-view">
        <h2 class="auth-heading" tabindex="-1">Sign in to Wend</h2>
        ${errors.length ? `
        <div class="auth-errors alert alert-danger" tabindex="-1">
          <p>${errors[0]}</p>
        </div>` : ""}
        <form class="auth-form" data-action="submit">
          <label for="login-email">Email</label>
          <input id="login-email" name="email" type="email" autocomplete="email"
            maxlength="254" required />

          <!-- current-password, not new-password: this is what tells a password manager to offer
               the saved credential rather than generate a fresh one. -->
          <label for="login-password">Password</label>
          <input id="login-password" name="password" type="password" autocomplete="current-password"
            required />

          <!-- .btn carries the design system's min-height: 2.75rem, which is what keeps this
               control at the 44x44 minimum target size. A bare <button> here measures 28px high. -->
          <button type="submit" class="btn btn-primary" data-role="submit">Sign in</button>
        </form>
        ${(state.failures ?? 0) >= 3 ? HELP : ""}
        <p class="auth-links">No account yet? <a href="/register">Create one</a>.</p>
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  // A server-side 401 belongs to no field, so focus goes to the summary. Per-field errors are
  // left to native validation, which focuses the offending input itself — sending focus to the
  // email box on a generic failure would land a screen-reader user mid-form having heard nothing.
  function focusError() {
    const summary = root.querySelector(".auth-errors");
    if (summary) summary.focus();
    else focusHeading();
  }

  function focusHelp() { root.querySelector(".auth-help")?.focus(); }

  // Disabled while a request is in flight, so a double-clicked button cannot burn two of the five
  // lockout attempts.
  function setBusy(busy) {
    const button = root.querySelector('[data-role="submit"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Signing in…" : "Sign in";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="submit"]')) return;
      e.preventDefault();
      const data = new FormData(e.target);
      h.submit({ email: data.get("email") ?? "", password: data.get("password") ?? "" });
    });
  }

  return { render, focusHeading, focusError, focusHelp, setBusy, bindActions };
}
```

- [ ] **Step 3 — create `Wend.Api/wwwroot/js/auth/login/controller.js`**

```js
// Wires the login view: submits, announces every outcome, moves focus deliberately.
export function createLoginController(model, view, announce, { onSignedIn }) {
  let seenFirstRender = false;

  view.bindActions({ submit: (fields) => model.submit(fields) });

  model.subscribe((state) => {
    if (state.status === "sending") {
      view.setBusy(true);
      announce("Signing in…");
      return;
    }

    if (state.status === "signedIn") {
      announce("Signed in.");
      onSignedIn();
      return;
    }

    view.render(state);
    view.setBusy(false);

    // The first render is the empty form: announce nothing, and leave focus where the caller put
    // it (the heading, with its own reason announced if there was one). Every later render is a
    // submit result and gets both.
    if (!seenFirstRender) {
      seenFirstRender = true;
      return;
    }

    if (state.failures === 3) {
      // The help block only just appeared. Focus it rather than the summary the user has already
      // read twice, and say why it is there.
      view.focusHelp();
      announce("Sign-in failed again. Some things to check are now shown below the form.");
    } else if (state.errors?.length) {
      view.focusError();
      announce(state.errors[0]);
    }
  });
}
```

- [ ] **Step 4 — style the new blocks in `Wend.Api/wwwroot/css/app.css`**

Add directly after the existing `.auth-errors ul` rule, before the `@media (min-width: 768px)`
block. Mobile-first: these are the baseline styles, and nothing here needs a breakpoint.

```css
/* Revealed after three consecutive failures. Uses the same muted-text token as .field-hint so it
   reads as guidance rather than a second error, and takes the focus ring the controller's
   focusHelp() depends on. */
.auth-help {
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.auth-help ul {
  margin-block-end: 0;
}

.auth-links {
  margin: 0;
  font-size: var(--text-sm);
}
```

- [ ] **Step 5 — verify by hand** (Task 7 wires the route; until then, check what exists)

There is nothing to click yet — `main.js` does not know about this screen until the next task. Run
the C# suite to confirm nothing regressed, and leave the browser checks to Task 7 Step 6, where the
screen is reachable.

```powershell
dotnet test
```

Expected: **231 passed, 0 failed** — unchanged. Frontend files are not covered by the C# suite; this
is a "did I break the build" check, not a verification of this task.

- [ ] **Step 6 — commit**

```powershell
git add Wend.Api/wwwroot/js/auth/login Wend.Api/wwwroot/css/app.css
git commit -m "Add the login screen"
```

---

## Task 7 — The auth gate

The task that puts a board back on the screen. It also closes the three dead ends the stress test
found: login had no route, `hideAppChrome()` had no inverse, and the verify screen had nowhere to
send anyone.

**Interfaces produced:** the `/login` route; `showAppChrome()`; the `logout-link` control.

- [ ] **Step 1 — add the logout control to `Wend.Api/wwwroot/index.html`**

Replace the header block. **Both controls now start hidden** — they belong to the signed-in app, and
the gate reveals them. Without this, they flash on screen during the boot check and then disappear.

```html
  <!-- Both controls belong to the signed-in app, so both start hidden and main.js's
       showAppChrome() reveals them once the gate has confirmed a session. On an auth screen they
       are a trap: Settings mounts the board settings over the login form, and both sit ahead of
       that form in the tab order. -->
  <header class="app-header">
    <h1>Wend</h1>
    <button type="button" id="settings-link" hidden>Settings</button>
    <button type="button" id="logout-link" hidden>Sign out</button>
  </header>
```

- [ ] **Step 2 — import the login screen in `Wend.Api/wwwroot/js/main.js`**

Add below the existing verify imports:

```js
import { createLoginModel } from "./auth/login/model.js";
import { createLoginView } from "./auth/login/view.js";
import { createLoginController } from "./auth/login/controller.js";
```

- [ ] **Step 3 — replace `hideAppChrome` and `reportLoadFailure`**

Replace the whole `hideAppChrome` function and its comment with the pair:

```js
// index.html's header belongs to the signed-in app. Left visible on an auth screen, Settings is a
// trap: it mounts the boards settings over the auth screen, and its Back goes to the board
// overview, which 401s. Both controls also sit between the skip link and the form the user came
// for. They start hidden in index.html; the gate reveals them and every auth screen re-hides them.
const APP_CHROME = ["settings-link", "logout-link"];

function hideAppChrome() {
  for (const id of APP_CHROME) document.getElementById(id).hidden = true;
}

function showAppChrome() {
  for (const id of APP_CHROME) document.getElementById(id).hidden = false;
}
```

Replace `reportLoadFailure` with the bounce:

```js
// A 401 mid-session is not a load failure to report — it is a session that ended, so the user goes
// back to the login screen with the reason announced and focus on the heading. Anything else is a
// genuine failure with no control to return focus to and no state to keep, so it is announced and
// nothing else happens.
function reportLoadFailure(error) {
  if (error?.status === 401) showLogin("Your session expired — please sign in again.");
  else announce("Couldn't load — please try again.");
}
```

- [ ] **Step 4 — add `showLogin`, `signOut`, and the boot gate**

Add `showLogin` beside `showRegister`:

```js
function showLogin(reason) {
  hideAppChrome();
  mount((root) => {
    const model = createLoginModel();
    const view = createLoginView(root);
    createLoginController(model, view, announce, {
      onSignedIn: () => {
        showAppChrome();
        showOverview(null, true); // focus the new-board input: the first thing to do here
      },
    });
    // Focus the screen the user has just been moved to, whether they asked to come here or were
    // bounced. Never left on <body>.
    view.focusHeading();
    if (reason) announce(reason);
  });
}
```

Add the sign-out handler beside the existing `settings-link` listener:

```js
async function signOut() {
  try {
    await api("/api/auth/logout", { method: "POST" });
  } catch {
    // The session may already be gone — that is the state we were heading for anyway. Moving the
    // user to the login screen is what matters, so this failure changes nothing.
  }
  showLogin("You're signed out.");
}
document.getElementById("logout-link").addEventListener("click", signOut);
```

Replace the whole trailing `switch (location.pathname)` block with the gate:

```js
// The server renders the SPA shell for every non-API path, so the client owns routing. Auth screens
// are reached by URL because an emailed link has to land somewhere, and because /login has to be
// linkable from the register and verify screens.
async function boot() {
  switch (location.pathname) {
    case "/register": showRegister(); return;
    case "/verify": showVerify(); return;
    case "/login": showLogin(); return;
  }

  // The gate: one call decides between the app and the login screen. /me answering 401 here is an
  // ordinary expected outcome, not an error to report.
  try {
    await api("/api/auth/me");
    showAppChrome();
    showOverview(); // first paint: no forced focus, skip link is available
  } catch (error) {
    if (error?.status === 401) showLogin();
    else announce("Couldn't load — please try again.");
  }
}

boot();
```

- [ ] **Step 5 — give the verify and register screens their way on**

In `Wend.Api/wwwroot/js/auth/verify/view.js`, replace the `confirmed` and `already` bodies. Plan 3
wrote these when there was nowhere to send anyone; there is now.

```js
    confirmed: `
      <h2 class="auth-heading" tabindex="-1">Address confirmed</h2>
      <p>Your email address is confirmed. <a href="/login">Sign in</a> to start using Wend.</p>`,
    already: `
      <h2 class="auth-heading" tabindex="-1">Already confirmed</h2>
      <p>This address was confirmed already, so this link has done its job.
        <a href="/login">Sign in</a> whenever you're ready.</p>`,
```

In `Wend.Api/wwwroot/js/auth/register/view.js`, add a way back below the form. Insert directly after
the closing `</form>` in the editing state, before the closing `</div>`:

```js
        <p class="auth-links">Already have an account? <a href="/login">Sign in</a>.</p>
```

- [ ] **Step 6 — verify by hand**

Start PostgreSQL and the app, then hard-reload before every check (`UseStaticFiles` sends no
`Cache-Control`, so a normal reload serves stale ES modules).

```powershell
Start-Service postgresql-x64-17
dotnet run --project Wend.Api
```

Walk these in order and tick each:

- [ ] `http://127.0.0.1:5174/` with no session → the login screen, both header controls hidden,
      focus on "Sign in to Wend".
- [ ] Register a fresh address at `/register`, then open the link from
      `%LOCALAPPDATA%\Wend\auth-emails.log` → confirm → the success screen offers **Sign in**.
- [ ] Sign in → the board overview appears, **Settings and Sign out are both visible**, focus is on
      the new-board input.
- [ ] Tab from the top: skip link → Settings → Sign out → content. Every control shows a focus ring.
- [ ] Sign out → the login screen, header controls hidden again, focus on the heading, "You're
      signed out." announced.
- [ ] Sign in again, then delete the `wend.session` cookie in devtools and click into a board → the
      login screen with "Your session expired — please sign in again."
- [ ] Sign in with the wrong password three times → the help block appears below the form and takes
      focus.
- [ ] Register a second account, do **not** confirm it, and sign in with its correct password →
      generic failure on screen, and a fresh link in `auth-emails.log`.
- [ ] At <768px (or with the `min-width: 768px` rules disabled in devtools) the login form is
      single-column with no horizontal scrolling, and every control clears 44×44.

- [ ] **Step 7 — run the suite and commit**

```powershell
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test
```

Expected: **231 passed, 0 failed** — unchanged.

```powershell
git add Wend.Api/wwwroot/index.html Wend.Api/wwwroot/js/main.js Wend.Api/wwwroot/js/auth/verify/view.js Wend.Api/wwwroot/js/auth/register/view.js
git commit -m "Gate the app behind sign-in and add the logout control"
```

---

## Task 8 — Backlog and docs

What this plan deferred, written down where the next plan will find it. Two new entries and one
update.

- [ ] **Step 1 — add the input-preservation entry to `docs/backlog.md`**

Append after the *Inactive-account retention* entry:

```markdown
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
```

- [ ] **Step 2 — add the lockout denial-of-service entry**

```markdown
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
```

- [ ] **Step 3 — widen the existing rate-limiting entry**

`/api/auth/*` gained three endpoints in this plan, so update the **Now** line of the existing
*`/api/auth/*` is not rate limited* entry to name them:

```markdown
- **Now:** none of register, resend-verification, login or logout is rate limited. Register, resend
  and the unconfirmed-account nudge on login all trigger outbound email, so all three are
  email-bombing vectors, and login is the credential-stuffing surface.
```

- [ ] **Step 4 — commit**

```powershell
git add docs/backlog.md
git commit -m "Record what Plan 4 deferred"
```

- [ ] **Step 5 — open the pull request**

```powershell
git push -u origin feature/slice2a-plan4-login
```

Open the PR against `main` for the other owner to review and merge. In the body, state the final
test count, and record every deviation from this plan — Plan 3 had six, and they were the most
useful part of its PR body. In particular, say which of these happened:

- whether `MapGroup("")` worked or the per-file `.RequireAuthorization()` fallback was needed;
- what status a form-encoded POST to `/login` actually returns;
- whether `Response.OnCompleted` delivered the nudge reliably under the test host.

---

## Self-review against the design doc

Every section of [`2026-08-10-wend-slice2a-plan4-login-design.md`](../2026-08-10-wend-slice2a-plan4-login-design.md)
maps to a task:

| Design section | Task |
|---|---|
| Authentication wiring (cookie, lockout, stamp interval) | 1 |
| `ICurrentUser`, for real | 1 |
| `RequireAuthorization()` on the five groups | 1 |
| Test seam, `TestAuthHandler`, the `useTestAuth` flag | 1 |
| `POST /api/auth/login`, constant time, generic 401 | 2 |
| The nudge, correct-password-only, off the response path | 3 |
| `GET /api/auth/me`, `POST /api/auth/logout` | 4 |
| Login binds JSON only (the antiforgery guard) | 4 |
| The real cookie walk and the `/me` canary | 5 |
| The login screen, focus rules, three-failure help | 6 |
| The gate, `/login` route, `showAppChrome`, logout focus | 7 |
| The verify screen's link to login | 7 |
| Input preservation, lockout DoS, Plan 8 rate limits | 8 |

**Counts:** 205 baseline (verified by counting `[Test]` attributes across `Wend.Tests`) → 211 → 218
→ 223 → 228 → 231, and 231 at every frontend task.

**Deliberately not covered by automated tests:** everything in Tasks 6 and 7. This repo has no JS
test harness, which has been true since Slice 1; the scripted browser checks in Task 7 Step 6 are
the verification, and they are the reason that step is a checklist rather than a paragraph.
