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

// No authentication until Plan 3 — every request is anonymous, so /api/* answers 401.
builder.Services.AddScoped<ICurrentUser, NullCurrentUser>();

// Identity's token providers are data protectors, and nothing else in Wend had needed data
// protection before now — no cookies, no antiforgery, no session — so IDataProtectionProvider is
// not in the container until this line. Without it the app fails DI validation at startup.
builder.Services.AddDataProtection();

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

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGroup("/api/boards").MapBoardEndpoints();
app.MapGroup("/api/auth").MapAuthEndpoints(publicBaseUrl);
app.MapListEndpoints();
app.MapCardEndpoints();
app.MapLabelEndpoints();
app.MapChecklistItemEndpoints();

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
