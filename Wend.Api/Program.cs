using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Wend.Api;
using Wend.Core;

var builder = WebApplication.CreateBuilder(args);

// Config seam — connection string from user-secrets (dev) or environment (prod).
var connectionString = builder.Configuration.GetConnectionString("WendDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:WendDb is not configured. Set it via user-secrets (dev) or environment (prod).");
var port = int.TryParse(builder.Configuration["Wend:Port"], out var p) ? p : 5174;

// Emailed links are built from this, never from the request's Host header — see
// AuthEndpoints.SendConfirmationAsync. Development has no configured origin and falls back to the
// request host, which on localhost is the only thing it can be.
var publicBaseUrl = builder.Configuration["Wend:PublicBaseUrl"];
if (publicBaseUrl is null && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "Wend:PublicBaseUrl is not configured. Set it via environment variables so confirmation "
        + "links cannot be forged through the Host header.");

builder.Services.AddDbContext<WendDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IBoardRepository, EfBoardRepository>();
builder.Services.AddScoped<IListRepository, EfListRepository>();
builder.Services.AddScoped<ICardRepository, EfCardRepository>();
builder.Services.AddScoped<ILabelRepository, EfLabelRepository>();
builder.Services.AddScoped<IChecklistItemRepository, EfChecklistItemRepository>();

// The signed-in user comes off the request principal now. Every repository call still takes an
// explicit ownerId; this is only where that id is read from.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// Identity's token providers are data protectors, and nothing else in Wend had needed data
// protection before now — no cookies, no antiforgery, no session — so IDataProtectionProvider is
// not in the container until this line. Without it the app fails DI validation at startup.
builder.Services.AddDataProtection();

// Identity: AddIdentityCore gives UserManager and the token providers; AddSignInManager here and
// AddIdentityCookies below supply the sign-in half that Plan 3 deliberately left out.
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

        // A confirmed address is the gate on signing in at all — this is what makes Plan 3's
        // verification step mean something rather than being a formality.
        options.SignIn.RequireConfirmedAccount = true;

        // Small enough to blunt credential stuffing, short enough that a real user who mistyped is
        // not locked out for the afternoon. AllowedForNewUsers matters because otherwise a
        // freshly-registered account — the one an attacker reaches first — is exempt.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;

        options.Tokens.ProviderMap.Add("WendEmailConfirmation",
            new TokenProviderDescriptor(typeof(EmailConfirmationTokenProvider<WendUser>)));
        options.Tokens.EmailConfirmationTokenProvider = "WendEmailConfirmation";
    })
    .AddEntityFrameworkStores<WendDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddTransient<EmailConfirmationTokenProvider<WendUser>>();

// Cookie authentication. AddIdentityCookies supplies the application cookie that AddIdentityCore
// deliberately left out in Plan 3; no login-redirect events are configured because .NET 10's cookie
// handler already answers 401/403 for JSON endpoints, and Wend has no server-rendered login page to
// redirect to — the client owns routing.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

// Required by UseAuthorization() and RequireAuthorization(): AddIdentityCore and AddIdentityCookies
// register authentication only, so without this the app builds and then throws "Unable to find the
// required services" on the first authorized route.
builder.Services.AddAuthorization();

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

    // Answer the challenge with a status code instead of redirecting to /Account/Login, which does
    // not exist here. VERIFIED against the running app, not assumed: the handler redirects even for
    // Accept: application/json, so without this an anonymous /api/boards answers 302 → the SPA
    // shell at 200. The client would then parse HTML as JSON, and the auth gate would never see the
    // 401 it is built on. Safe as a blanket rule because nothing outside /api is authorized — the
    // static files and the SPA fallback are anonymous, so this only ever fires for the API.
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Zero, not the 30-minute default. The stamp is re-checked on every authenticated request, so a
// password reset (Plan 5) and an account deletion (Plan 7) evict a live session on its NEXT
// request. The cost is one user lookup and one Set-Cookie per authenticated response, bought
// deliberately: a cache interval would turn those promises into "within half an hour".
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

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

// Outside the environment guard above on purpose: releasing a squatted address is a data-retention
// concern, not a dev-email one. Registered inside that branch it would silently stop running the
// day Plan 9 supplies a real sender.
builder.Services.AddHostedService<UnverifiedAccountPurgeService>();


// Keep request paths and bodies out of the framework logs; quiet the startup banner.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

// Local-first: listen on 127.0.0.1 + [::1] only, never the public network.
builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(port));

var app = builder.Build();

// Apply pending EF Core migrations on startup (dev-simple; the deployment plan switches this to
// migration bundles / scripts, which Microsoft recommends for production).
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<WendDbContext>().Database.Migrate();

// Unhandled failures → bodyless 500 (no developer exception page over the wire).
app.UseExceptionHandler(b => b.Run(ctx => { ctx.Response.StatusCode = 500; return Task.CompletedTask; }));

// Serve the vanilla-JS frontend (wwwroot) same-origin.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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

// Anything under /api that no endpoint above claimed is a missing API route, not a client route.
// Without this it reaches the fallback below and returns the SPA shell at 200, so a typo'd or
// not-yet-wired route reads as success and the client throws parsing HTML as JSON. Literal
// segments outrank a catch-all, so every real endpoint above still matches first.
app.Map("/api/{**path}", () => Results.NotFound());

// Any non-API path renders the SPA shell; the client handles routing from there.
app.MapFallbackToFile("index.html");

Console.WriteLine($"Wend → http://127.0.0.1:{port}");

app.Run();

// Exposed so Wend.Tests can boot the real app with WebApplicationFactory<Program>.
public partial class Program;
