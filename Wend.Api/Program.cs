using Microsoft.EntityFrameworkCore;
using Wend.Api;
using Wend.Core;

var builder = WebApplication.CreateBuilder(args);

// Config seam — connection string from user-secrets (dev) or environment (prod).
var connectionString = builder.Configuration.GetConnectionString("WendDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:WendDb is not configured. Set it via user-secrets (dev) or environment (prod).");
var port = int.TryParse(builder.Configuration["Wend:Port"], out var p) ? p : 5174;

builder.Services.AddDbContext<WendDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IBoardRepository, EfBoardRepository>();builder.Services.AddScoped<IListRepository, EfListRepository>();
builder.Services.AddScoped<ICardRepository, EfCardRepository>();
builder.Services.AddScoped<ILabelRepository, EfLabelRepository>();
builder.Services.AddScoped<IChecklistItemRepository, EfChecklistItemRepository>();

// No authentication until Plan 3 — every request is anonymous, so /api/* answers 401.
builder.Services.AddScoped<ICurrentUser, NullCurrentUser>();


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
