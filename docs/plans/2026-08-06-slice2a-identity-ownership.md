# Wend Slice 2a — Plan 2: Identity schema & per-user ownership

> **For agentic workers:** use `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` to implement this task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Give Wend a `WendUser`, make every board owned, and scope every repository query to its
owner — so a row belonging to another user is *not found* — without adding any authentication.

**Architecture:** `WendDbContext` becomes `IdentityDbContext<WendUser>`. `Board` gains a required
`OwnerId` cascading from `WendUser`. Four upward navigations (`List.Board`, `Card.List`,
`Label.Board`, `ChecklistItem.Card`) make ownership expressible in a query; each EF repository then
gets one private `Owned(ownerId)` helper that every method starts from. Endpoints read an
`ICurrentUser` seam and pass the owner id down. Until Plan 3 that seam yields `null`, so the live
app answers 401 to everything and the test suite is the only consumer.

**Tech stack:** `net10.0`, EF Core 10, Npgsql `10.0.2`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` `10.0.9`, NUnit 4 + `Microsoft.AspNetCore.Mvc.Testing`, native PostgreSQL 17.

**Reference:** design spec [`2026-08-06-wend-slice2a-plan2-ownership-design.md`](../2026-08-06-wend-slice2a-plan2-ownership-design.md) · parent spec [`2026-07-08-wend-slice2a-accounts-design.md`](../2026-07-08-wend-slice2a-accounts-design.md).

---

## Global constraints

- Target `net10.0`. EF Core 10 throughout; **no Docker, no Testcontainers, no hypervisor.**
- New package, approved by approving this plan: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` `10.0.9` into `Wend.Core`. **No other new packages.**
- `dotnet build` must end at **0 warnings**; `dotnet test` green at the stated count for every task.
- **No authentication in this plan.** No `AddIdentityCore`, no `SignInManager`, no cookie scheme, no `RequireAuthorization()`, no frontend changes.
- Commits: one per task, authored under your own account, **no co-author trailer and no AI attribution** (house rule). Run every command from the repo root.
- Branch: `feature/slice2a-plan2-ownership`, opened as a PR for the other owner to review and merge.

---

## Notes for the implementer

> ### ⚠️ This branch destroys your local board data, and `git switch` will not undo it
>
> `Program.cs` runs `Database.Migrate()` at **startup**, and the `AddBoardOwner` migration opens
> with `DELETE FROM "Boards"`. Merely running `dotnet run` on this branch — or `dotnet test`, which
> boots the app — wipes every board, list, card, label and checklist item in the `wend` dev
> database. There is no prompt. This is the accepted cost of the clean-slate decision (see the
> design spec), not a bug.
>
> **It is also not reversible by switching branches.** Back on `main`, `Board` has no `OwnerId`,
> but the column still exists in the database as `NOT NULL` with no default, so every board insert
> fails. To return to `main`:
>
> ```powershell
> dotnet ef database drop --force --project Wend.Core --startup-project Wend.Api
> git switch main
> dotnet run --project Wend.Api    # rebuilds the schema from main's migrations
> ```
>
> **Say this in the PR description.** Henry has not reviewed the design spec and will otherwise
> lose his boards the first time he checks the branch out.

- **Start PostgreSQL first.** The service is `Manual` start: `Start-Service postgresql-x64-17`. Connection refused or an EF timeout means the service is stopped, not a code bug.
- **Stop the app before building.** The process is `Wend.Api`, *not* `Wend`:
  `Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force`. Skipping this gives MSB3021/3027 copy-lock errors that look like test failures.
- **Run `dotnet ef` with `ASPNETCORE_ENVIRONMENT=Development`** so user-secrets supply the connection string.
- **Test counts are load-bearing.** A paste-driven edit of exactly this shape once silently dropped three tests behind a green suite. Every task states its expected total; check the number `dotnet test` prints against it before committing. Current baseline: **147** (73 API, 74 repository).
- **`FindAsync` cannot carry a predicate.** All 22 of its call sites across the five EF repositories become `Owned(ownerId).FirstOrDefaultAsync(...)`. This is the same trap as the Plan 7 restore bug.
- **`IgnoreQueryFilters()` stays safe.** `EfCardRepository.RestoreCardAsync` and `EfChecklistItemRepository.RestoreItemAsync` need it to reach soft-deleted rows. Ownership lives in the `Where` clause, not a query filter, so it survives — do **not** move ownership into a global query filter.
- **Endpoint logic does not change.** Only the repository lookups gain an `ownerId` argument. Every existing validation, ordering rule and status code stays exactly as it is; the green suite is the proof.
- **Create-into endpoints scope for free.** `POST /api/boards/{boardId}/lists` already does `if (await boards.GetBoardAsync(boardId) is null) return NotFound()`. Once that call takes an owner id, another user's board is `null` and the endpoint returns 404 unchanged. The same pattern covers cards-into-lists, labels-into-boards and items-into-cards.
- **Create the HTTP client once, *then* switch users.** `WendApiFactory.ConfigureClient` runs on every `CreateClient()` call and ends by setting `CurrentUser.UserId` back to `DefaultUserId`. Calling `CreateClient()` after switching to user B silently reverts you to user A, and the isolation test then passes for the wrong reason — the worst possible failure for a security test, because green means nothing. Pattern for every isolation test in Tasks 3–7:

```csharp
var client = factory.CreateClient();          // once, as the default user
// ... create user A's data ...
factory.CurrentUser.UserId = otherUserId;     // switch AFTER, never CreateClient() again
Assert.That(factory.CurrentUser.UserId, Is.EqualTo(otherUserId));   // cheap guard against the trap
```

- **Task order is dependency-driven, not thematic.** Task 3 makes `OwnerId` required, which is the moment every repository test needs a seeded user. Task 2 exists to put that seam in place first.

---

## File structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Wend.Core/Wend.Core.csproj` | modify | Add the Identity EF package |
| `Wend.Core/WendUser.cs` | **new** | `IdentityUser` subclass with `DisplayName` |
| `Wend.Core/WendDbContext.cs` | modify | Become `IdentityDbContext<WendUser>`; configure `Board.OwnerId` |
| `Wend.Core/Board.cs` | modify | `OwnerId` |
| `Wend.Core/List.cs` · `Card.cs` · `Label.cs` · `ChecklistItem.cs` | modify | One upward navigation each |
| `Wend.Core/I*Repository.cs` (×5) | modify | `string ownerId` on all 34 methods |
| `Wend.Core/Ef*Repository.cs` (×5) | modify | `Owned(ownerId)` helper; every query scoped |
| `Wend.Core/Migrations/` | **new** | `AddIdentitySchema`, `AddBoardOwner` |
| `Wend.Api/ICurrentUser.cs` | **new** | The seam + `NullCurrentUser` |
| `Wend.Api/Program.cs` | modify | Register `ICurrentUser` |
| `Wend.Api/*Endpoints.cs` (×5) | modify | 401 guard; pass `ownerId` to repositories |
| `Wend.Tests/TestCurrentUser.cs` | **new** | Mutable `ICurrentUser` for tests |
| `Wend.Tests/WendApiFactory.cs` | modify | Seed a default user; override `ICurrentUser` |
| `Wend.Tests/TestUsers.cs` | **new** | Seed helper for repository tests |
| `Wend.Tests/*RepositoryTests.cs` (×5) | modify | Seed an owner; pass `ownerId` |
| `Wend.Tests/OwnershipTests.cs` | **new** | Cross-user isolation, cascade, 401 sweep |

---

## Task 1 — Identity schema

Adds Identity's tables and the four navigations. **No ownership yet**, so nothing behaves
differently and no test changes.

**Interfaces produced:** `WendUser` (`Id` string, `Email`, `DisplayName`); `WendDbContext : IdentityDbContext<WendUser>`; navigations `List.Board`, `Card.List`, `Label.Board`, `ChecklistItem.Card`.

- [ ] **Step 1 — add the package**

```powershell
dotnet add Wend.Core package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 10.0.9
```

- [ ] **Step 2 — create `Wend.Core/WendUser.cs`**

```csharp
using Microsoft.AspNetCore.Identity;

namespace Wend.Core;

/// <summary>
/// A Wend account. Email is the login credential (Identity's own field); DisplayName is the
/// human-facing name. Slice 2b will render DisplayName on other users' boards, so it is treated
/// as untrusted user content everywhere it is written or displayed.
/// </summary>
public class WendUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}
```

- [ ] **Step 3 — add the four upward navigations.** Each pairs with a collection that already
exists on the principal, so **no column is added**. `= null!;` tells the compiler EF populates it.

> **Each one needs `[JsonIgnore]`, and skipping it breaks 38 tests.** The navigations are free
> *schema*-wise but not *serialisation*-wise. Endpoints return tracked entities directly
> (`Results.Created(..., list)`), and EF's navigation fixup populates the upward navigation
> whenever the principal is loaded in the same context — which the create-into endpoints always do,
> because they check the parent exists first. `Board.Lists → List.Board → Board.Lists` is then a
> reference cycle, `System.Text.Json` throws, and the bodyless 500 handler turns it into an empty
> response body. The client-side symptom is a misleading
> `JsonException: The input does not contain any JSON tokens` in the *test*, not the server.
> `[JsonIgnore]` says what is true: these navigations are query plumbing, never wire format.
> API responses stay byte-identical to Plan 1. *(Found in execution, 2026-08-06.)*

In `Wend.Core/List.cs`, inside `class List`:

All four files also need `using System.Text.Json.Serialization;`.

```csharp
    // Upward navigation — exists so ownership (Board.OwnerId) is expressible in a query.
    // [JsonIgnore] because EF's fixup populates it whenever the board is in the same context,
    // which would make Board.Lists → List.Board → Board.Lists a serialisation cycle on the wire.
    [JsonIgnore]
    public Board Board { get; set; } = null!;
```

In `Wend.Core/Card.cs`, inside `class Card`:

```csharp
    // Upward navigation — ownership is reached via List → Board → OwnerId.
    // [JsonIgnore]: EF fixup would otherwise make List.Cards → Card.List a serialisation cycle.
    [JsonIgnore]
    public List List { get; set; } = null!;
```

In `Wend.Core/Label.cs`, inside `class Label`:

```csharp
    // Upward navigation — ownership is reached via Board → OwnerId.
    // [JsonIgnore]: EF fixup would otherwise make Board.Labels → Label.Board a serialisation cycle.
    [JsonIgnore]
    public Board Board { get; set; } = null!;
```

In `Wend.Core/ChecklistItem.cs`, inside `class ChecklistItem`:

```csharp
    // Upward navigation — ownership is reached via Card → List → Board → OwnerId.
    // [JsonIgnore]: EF fixup would otherwise make Card.ChecklistItems → ChecklistItem.Card a cycle.
    [JsonIgnore]
    public Card Card { get; set; } = null!;
```

- [ ] **Step 4 — make the context an Identity context** — `Wend.Core/WendDbContext.cs`. Change the
`using`s and the declaration, and call `base.OnModelCreating` **first**:

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>EF Core context for Wend's PostgreSQL database, including ASP.NET Identity's schema.</summary>
public class WendDbContext(DbContextOptions<WendDbContext> options)
    : IdentityDbContext<WendUser>(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<List> Lists => Set<List>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<CardLabel> CardLabels => Set<CardLabel>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own mapping must run first; Wend's configuration layers on top.
        base.OnModelCreating(modelBuilder);

        // DisplayName is user-controlled content rendered on other users' boards in Slice 2b.
        // The column is capped here; the write-time validation and escaping arrive with
        // registration in Plan 3, which is the first thing that can write it.
        modelBuilder.Entity<WendUser>().Property(u => u.DisplayName).HasMaxLength(100);
```

Leave the rest of `OnModelCreating` (the two query filters and the `CardLabel` configuration)
exactly as it is.

- [ ] **Step 5 — generate the migration**

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet ef migrations add AddIdentitySchema --project Wend.Core --startup-project Wend.Api
```

- [ ] **Step 6 — confirm the navigations added no columns.** Open the generated migration. It must
contain **only** `CreateTable` calls for `AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`,
`AspNetUserLogins`, `AspNetUserRoles`, `AspNetUserTokens`, `AspNetRoleClaims` and their indexes.

If it also contains any `AlterColumn`/`AddColumn` against `Boards`, `Lists`, `Cards`, `Labels` or
`ChecklistItems`, the navigations changed the model — stop, note exactly what changed, and resolve
it before continuing (this is a recorded open question from the design spec, not an expected outcome).

- [ ] **Step 7 — run the suite**

```powershell
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
dotnet test
```

Expected: build 0 warnings; **147 passed, 0 failed** — unchanged, because nothing behaves differently yet.

- [ ] **Step 8 — commit**

```powershell
git add Wend.Core Wend.Api
git commit -m "Add ASP.NET Identity schema and upward navigations for ownership"
```

---

## Task 2 — the current-user seam

Puts `ICurrentUser` and the test seeding helpers in place **before** `OwnerId` becomes required.
Nothing consumes the seam yet.

**Interfaces produced:** `ICurrentUser.UserId` (`string?`); `NullCurrentUser`; `TestCurrentUser.UserId` (settable); `WendApiFactory.DefaultUserId`; `TestUsers.SeedAsync(WendDbContext, string?) → string`.

- [ ] **Step 1 — create `Wend.Api/ICurrentUser.cs`**

```csharp
namespace Wend.Api;

/// <summary>
/// The signed-in user for this request, or null when nobody is signed in. Lives in Wend.Api
/// because "current" is an HTTP concept — the domain takes an owner id as an ordinary argument.
/// Plan 3 replaces NullCurrentUser with an HttpContext-backed implementation; nothing else changes.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
}

/// <summary>No authentication exists yet, so nobody is ever signed in and every /api/* call is 401.</summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public string? UserId => null;
}
```

- [ ] **Step 2 — register it** — `Wend.Api/Program.cs`, immediately after the repository registrations:

```csharp
// No authentication until Plan 3 — every request is anonymous, so /api/* answers 401.
builder.Services.AddScoped<ICurrentUser, NullCurrentUser>();
```

- [ ] **Step 3 — create `Wend.Tests/TestCurrentUser.cs`**

```csharp
using Wend.Api;

namespace Wend.Tests;

/// <summary>
/// Mutable ICurrentUser for tests: set UserId to act as that user, or null to act anonymously.
/// This is how one test proves the ownership boundary — act as A, switch to B, assert 404.
/// </summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public string? UserId { get; set; }
}
```

- [ ] **Step 4 — create `Wend.Tests/TestUsers.cs`**

```csharp
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// Seeds WendUser rows directly through the context. Identity's services are not wired in this
/// plan (that is Plan 3), so there is no UserManager — and none is needed to own a board.
/// </summary>
public static class TestUsers
{
    public static async Task<string> SeedAsync(WendDbContext db, string? email = null)
    {
        var id = Guid.NewGuid().ToString();
        email ??= $"{id}@example.test";
        db.Users.Add(new WendUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Test User",
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        await db.SaveChangesAsync();
        return id;
    }
}
```

- [ ] **Step 5 — seed a default user in the API factory** — `Wend.Tests/WendApiFactory.cs`. Add
these usings alongside the existing ones:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wend.Api;
using Wend.Core;
```

Add the shared test user and the service override. Keep the existing `_dbName` field, the database
creation in `ConfigureWebHost`, and `Dispose` exactly as they are — append this to the class:

```csharp
    /// <summary>The user every API test acts as by default. Boards created over HTTP belong to it.</summary>
    public string DefaultUserId { get; } = Guid.NewGuid().ToString();

    /// <summary>Swap UserId to act as somebody else (or null for anonymous) inside a test.</summary>
    public TestCurrentUser CurrentUser { get; } = new();

    protected override void ConfigureClient(System.Net.Http.HttpClient client)
    {
        base.ConfigureClient(client);
        // First client creation boots the app; seed the default user and start acting as them.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();
        if (!db.Users.Any(u => u.Id == DefaultUserId))
        {
            db.Users.Add(new WendUser
            {
                Id = DefaultUserId,
                UserName = "default@example.test",
                NormalizedUserName = "DEFAULT@EXAMPLE.TEST",
                Email = "default@example.test",
                NormalizedEmail = "DEFAULT@EXAMPLE.TEST",
                DisplayName = "Default Test User",
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            db.SaveChanges();
        }
        CurrentUser.UserId = DefaultUserId;
    }
```

And inside the existing `ConfigureWebHost`, after the `builder.UseSetting(...)` line, add:

```csharp
        // Tests supply their own current user; the app's NullCurrentUser would make everything 401.
        builder.ConfigureTestServices(services =>
            services.AddScoped<ICurrentUser>(_ => CurrentUser));
```

`ConfigureTestServices` needs `using Microsoft.AspNetCore.TestHost;`.

- [ ] **Step 6 — write the failing test.** Create `Wend.Tests/OwnershipTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wend.Api;
using Wend.Core;

namespace Wend.Tests;

public class OwnershipTests
{
    [Test]
    public void No_current_user_means_no_user_id()
    {
        Assert.That(new NullCurrentUser().UserId, Is.Null);
    }

    [Test]
    public async Task The_api_factory_seeds_its_default_user()
    {
        using var factory = new WendApiFactory();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();
        var user = db.Users.SingleOrDefault(u => u.Id == factory.DefaultUserId);

        Assert.That(user, Is.Not.Null);
        Assert.That(factory.CurrentUser.UserId, Is.EqualTo(factory.DefaultUserId));
    }
}
```

- [ ] **Step 7 — run it and watch it fail**

```powershell
dotnet test --filter "FullyQualifiedName~OwnershipTests"
```

Expected: FAIL to compile until Steps 1–5 are in place; once they are, PASS.

- [ ] **Step 8 — run the full suite.** Expected: **149 passed, 0 failed** (147 + 2 new).

- [ ] **Step 9 — commit**

```powershell
git add Wend.Api Wend.Tests
git commit -m "Add ICurrentUser seam and test user seeding"
```

---

## Task 3 — board ownership

The pivot: `OwnerId` becomes required, so every board-creating test needs an owner.

**Interfaces produced:** `IBoardRepository` — `GetBoardsAsync(string ownerId)`, `GetBoardAsync(int id, string ownerId)`, `CreateBoardAsync(string title, string ownerId)`, `RenameBoardAsync(int id, string newTitle, string ownerId)`, `DeleteBoardAsync(int id, string ownerId)`.

- [ ] **Step 1 — add the column** — `Wend.Core/Board.cs`, inside `class Board`, after `Title`:

```csharp
    // The owning account. Required: every board belongs to exactly one user, and deleting that
    // user cascades through boards → lists → cards (the GDPR erasure path).
    public string OwnerId { get; set; } = "";
```

- [ ] **Step 2 — configure the relationship** — `Wend.Core/WendDbContext.cs`, in `OnModelCreating`
after the `base.OnModelCreating(modelBuilder);` line:

```csharp
        // Every board belongs to one user; deleting the user erases their boards and everything
        // beneath them via the existing required FKs.
        modelBuilder.Entity<Board>()
            .HasOne<WendUser>()
            .WithMany()
            .HasForeignKey(b => b.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 3 — scope the repository** — replace `Wend.Core/EfBoardRepository.cs` entirely:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfBoardRepository(WendDbContext db) : IBoardRepository
{
    // The single place board ownership is expressed. Every query starts here, so a board belonging
    // to another user is simply absent — which is what makes the API answer 404 rather than 403.
    private IQueryable<Board> Owned(string ownerId) => db.Boards.Where(b => b.OwnerId == ownerId);

    public async Task<IReadOnlyList<Board>> GetBoardsAsync(string ownerId) =>
        await Owned(ownerId).OrderBy(b => b.Id).ToListAsync();

    public async Task<Board> CreateBoardAsync(string title, string ownerId)
    {
        var board = new Board { Title = title, OwnerId = ownerId };
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        return board;
    }

    // FindAsync cannot carry a predicate, so ownership forces FirstOrDefaultAsync here and below.
    public async Task<Board?> GetBoardAsync(int id, string ownerId) =>
        await Owned(ownerId).FirstOrDefaultAsync(b => b.Id == id);

    public async Task<bool> RenameBoardAsync(int id, string newTitle, string ownerId)
    {
        var board = await Owned(ownerId).FirstOrDefaultAsync(b => b.Id == id);
        if (board is null) return false;
        board.Title = newTitle;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteBoardAsync(int id, string ownerId)
    {
        var board = await Owned(ownerId).FirstOrDefaultAsync(b => b.Id == id);
        if (board is null) return false;
        db.Boards.Remove(board);
        await db.SaveChangesAsync();
        return true;
    }
}
```

- [ ] **Step 4 — update the interface** — `Wend.Core/IBoardRepository.cs`, method block only:

```csharp
    Task<IReadOnlyList<Board>> GetBoardsAsync(string ownerId);
    Task<Board?> GetBoardAsync(int id, string ownerId);
    Task<Board> CreateBoardAsync(string title, string ownerId);
    Task<bool> RenameBoardAsync(int id, string newTitle, string ownerId);
    Task<bool> DeleteBoardAsync(int id, string ownerId);
```

- [ ] **Step 5 — guard and thread the endpoints** — `Wend.Api/BoardEndpoints.cs`. Add `ICurrentUser
currentUser` to each handler's parameters and open each with the guard. The `GET /` handler becomes:

```csharp
        group.MapGet("/", async (IBoardRepository repo, ICurrentUser currentUser) =>
        {
            if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
            return Results.Ok(await repo.GetBoardsAsync(ownerId));
        });
```

Apply the same shape to the other four handlers, passing `ownerId` as the new final argument to
`CreateBoardAsync`, `GetBoardAsync`, `RenameBoardAsync` and `DeleteBoardAsync`. In the
`GET /{id:int}` handler only the `boards.GetBoardAsync(id, ownerId)` call changes — the nested
list/card/label/checklist reads stay as they are, because the board itself has already been
ownership-checked and returns 404 first.

- [ ] **Step 6 — fix the two other endpoint files that call `GetBoardAsync`.** In
`Wend.Api/ListEndpoints.cs` and `Wend.Api/LabelEndpoints.cs`, the create-into handlers do a board
existence check. Add `ICurrentUser currentUser` to those handlers, open with the guard, and change
the check to pass the owner. In `ListEndpoints`:

```csharp
                if (await boards.GetBoardAsync(boardId, ownerId) is null) return Results.NotFound();
```

Leave every other line in both files alone; their own repositories are scoped in Tasks 4 and 6.

- [ ] **Step 7 — generate the migration**

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet ef migrations add AddBoardOwner --project Wend.Core --startup-project Wend.Api
```

- [ ] **Step 8 — hand-edit the migration for the clean slate.** Open the generated file and make
`migrationBuilder.Sql` the **first** statement in `Up`, before the `AddColumn`:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clean slate: OwnerId is required and pre-existing boards have no owner, so they go.
            // Existing required FKs cascade to lists, cards, labels, join rows and checklist items.
            // On a fresh database (CI, every test, production) this is a no-op against an empty table.
            migrationBuilder.Sql(@"DELETE FROM ""Boards"";");

            // ... EF's generated AddColumn / CreateIndex / AddForeignKey follow, unchanged ...
```

Ordering matters: EF adds the required column with `defaultValue: ""`, and the foreign key would
then fail against rows whose owner does not exist.

- [ ] **Step 9 — give every repository test an owner.** In **each** of the five
`Wend.Tests/*RepositoryTests.cs` files, add a field:

```csharp
    private string _ownerId = null!;
```

Then make `SetUp` async and seed the owner as its last statement. NUnit awaits an
`async Task` setup properly — do **not** block on the seed with `.GetAwaiter().GetResult()`:

```csharp
    [SetUp]
    public async Task SetUp()
    {
        // ... existing connection / context / repository construction, unchanged ...
        _ownerId = await TestUsers.SeedAsync(_db);
    }
```

Then update each file's `NewBoardAsync` helper to pass it, for example in `ListRepositoryTests`:

```csharp
    private async Task<int> NewBoardAsync(string title = "Board") =>
        (await _boards.CreateBoardAsync(title, _ownerId)).Id;
```

`CardRepositoryTests`, `ChecklistItemRepositoryTests` and `LabelRepositoryTests` reach boards
through the same helper shape — update whichever call creates the board. Three files also construct
`new Board { ... }` directly (one in `BoardRepositoryTests`, two in `ListRepositoryTests`); add
`OwnerId = _ownerId` to those initialisers.

- [ ] **Step 10 — update `BoardRepositoryTests` call sites.** Every call to `_repo.*` in this file
takes `_ownerId` as its new final argument. The other four repository test files call *their own*
repositories, which are unchanged until Tasks 4–7.

- [ ] **Step 11 — add board isolation tests** to `Wend.Tests/OwnershipTests.cs`:

```csharp
    [Test]
    public async Task A_board_is_invisible_to_another_user()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" });
        var board = await created.Content.ReadFromJsonAsync<Board>();

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);

        Assert.That((await client.GetAsync($"/api/boards/{board!.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.DeleteAsync($"/api/boards/{board.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/boards/{board.Id}", new { Title = "Yours" })).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await (await client.GetAsync("/api/boards")).Content
            .ReadFromJsonAsync<List<Board>>(), Is.Empty);
    }

    [Test]
    public async Task Anonymous_requests_are_unauthorized()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();
        factory.CurrentUser.UserId = null;

        Assert.That((await client.GetAsync("/api/boards")).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static async Task<string> SeedOtherUserAsync(WendApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await TestUsers.SeedAsync(scope.ServiceProvider.GetRequiredService<WendDbContext>());
    }
```

Add `using System.Net;`, `using System.Net.Http.Json;` to the file.

- [ ] **Step 12 — run the suite.** Expected: **152 passed, 0 failed** — 149 + 2 API isolation tests
+ 1 repository-level isolation test (`Another_users_board_is_invisible_and_untouchable` in
`BoardRepositoryTests`, added in execution: the boundary is worth pinning at the repository layer
too, not only over HTTP). If the number is lower, a test was dropped in the Step 9–10 edits — find
it before continuing.

- [ ] **Step 13 — commit**

```powershell
git add Wend.Core Wend.Api Wend.Tests
git commit -m "Give boards a required owner and scope board queries to it"
```

---

## Tasks 4–7 — scope the remaining repositories

These four tasks are the same shape. For each: add the `Owned` helper, add `string ownerId` as the
final parameter of every interface method, replace every `db.<Set>` and every `FindAsync` with the
scoped equivalent, thread `ownerId` through that repository's endpoint file behind the 401 guard,
and update that repository's test file.

**The mechanical rule, applied per repository:**

| Before | After |
|---|---|
| `db.Cards.Where(...)` | `Owned(ownerId).Where(...)` |
| `await db.Cards.FindAsync(id)` | `await Owned(ownerId).FirstOrDefaultAsync(c => c.Id == id)` |
| `db.Cards.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id)` | `Owned(ownerId).IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id)` |

Private helpers that already take a parent id (`ResequenceAsync(boardId)`, `ResequenceAsync(listId)`)
need **no** owner argument: they are only ever reached after an owner-scoped lookup has succeeded.

### Task 4 — lists

**Interfaces produced:** `GetListsForBoardAsync(int boardId, string ownerId)`, `CreateListAsync(int boardId, string title, string ownerId)`, `GetListAsync(int id, string ownerId)`, `RenameListAsync(int id, string newTitle, string ownerId)`, `DeleteListAsync(int id, string ownerId)`, `MoveListAsync(int id, int position, string ownerId)`.

- [ ] **Step 1 — the helper** — top of `Wend.Core/EfListRepository.cs`:

```csharp
    private IQueryable<List> Owned(string ownerId) => db.Lists.Where(l => l.Board.OwnerId == ownerId);
```

- [ ] **Step 2** — apply the mechanical rule to all 6 methods and update `IListRepository`.
`MoveListAsync`'s sibling query becomes `Owned(ownerId).Where(l => l.BoardId == list.BoardId)`.
- [ ] **Step 3** — `Wend.Api/ListEndpoints.cs`: add `ICurrentUser` + the guard to the four handlers
that do not yet have it, and pass `ownerId` to every `lists.*` call.
- [ ] **Step 4** — `Wend.Tests/ListRepositoryTests.cs`: add `_ownerId` to every `_repo.*` call.
- [ ] **Step 5** — add to `OwnershipTests.cs`: user B gets 404 renaming, deleting, moving and
reading user A's list, and 404 posting a list into user A's board.
- [ ] **Step 6** — `dotnet test`. Expected **156** (152 + 4 new).
- [ ] **Step 7** — commit: `Scope list queries to the board owner`

### Task 5 — cards

**Interfaces produced:** `GetCardsForListAsync(int listId, string ownerId)`, `GetCardAsync(int id, string ownerId)`, `CreateCardAsync(int listId, string title, string ownerId)`, `EditCardAsync(int id, string title, string? description, DateOnly? dueDate, string ownerId)`, `DeleteCardAsync(int id, string ownerId)`, `RestoreCardAsync(int id, string ownerId)`, `MoveCardAsync(int id, int targetListId, int position, string ownerId)`, `SetCardCompletedAsync(int id, bool completed, string ownerId)`.

- [ ] **Step 1 — the helper** — top of `Wend.Core/EfCardRepository.cs`:

```csharp
    private IQueryable<Card> Owned(string ownerId) => db.Cards.Where(c => c.List.Board.OwnerId == ownerId);
```

- [ ] **Step 2** — apply the mechanical rule to all 8 methods and update `ICardRepository`.
- [ ] **Step 3 — the restore path keeps `IgnoreQueryFilters`, scoped:**

```csharp
    public async Task<bool> RestoreCardAsync(int id, string ownerId)
    {
        // IgnoreQueryFilters reaches the soft-deleted row from ANY context. Ownership lives in the
        // Where clause, not a query filter, so it is NOT dropped here — that is the whole reason
        // ownership was not implemented as a global query filter.
        var card = await Owned(ownerId).IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (card is null) return false;
```

- [ ] **Step 4** — `MoveCardAsync` validates that the target list is on the same board. That lookup
becomes owner-scoped too, so a target list belonging to another user resolves as missing.
- [ ] **Step 5** — `Wend.Api/CardEndpoints.cs`: guard + `ownerId` on every `cards.*` call.
- [ ] **Step 6** — `Wend.Tests/CardRepositoryTests.cs`: `_ownerId` on every `_repo.*` call (28 tests).
- [ ] **Step 7** — add to `OwnershipTests.cs`: user B gets 404 reading, editing, moving, completing,
deleting **and restoring** user A's card. The restore case is the regression test for Step 3.
- [ ] **Step 8 — pin the move endpoint's enumeration resistance.** `MoveCardAsync` distinguishes
"target list missing" (404) from "target list on another board" (400). A **400 would confirm that
another user's list exists** and sits on a different board — exactly the leak 404-not-403 exists to
prevent. Prose in Step 4 is not enough; pin it:

```csharp
    [Test]
    public async Task Moving_a_card_into_another_users_list_is_404_not_400()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        // User A's board, list and card.
        var boardA = await (await client.PostAsJsonAsync("/api/boards", new { Title = "A" }))
            .Content.ReadFromJsonAsync<Board>();
        var listA = await (await client.PostAsJsonAsync($"/api/boards/{boardA!.Id}/lists", new { Title = "A list" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();
        var cardA = await (await client.PostAsJsonAsync($"/api/lists/{listA!.Id}/cards", new { Title = "A card" }))
            .Content.ReadFromJsonAsync<Card>();

        // User B's own board and list.
        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);
        Assert.That(factory.CurrentUser.UserId, Is.Not.EqualTo(factory.DefaultUserId));
        var boardB = await (await client.PostAsJsonAsync("/api/boards", new { Title = "B" }))
            .Content.ReadFromJsonAsync<Board>();
        var listB = await (await client.PostAsJsonAsync($"/api/boards/{boardB!.Id}/lists", new { Title = "B list" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();

        // B moving A's card anywhere: the card is not B's, so it is simply missing.
        var moveAsB = await client.PutAsJsonAsync($"/api/cards/{cardA!.Id}/move",
            new { ListId = listB!.Id, Position = 0 });
        Assert.That(moveAsB.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // A moving their own card into B's list: the target is not A's, so it is missing too —
        // 404, never 400, which would confirm B's list exists on another board.
        factory.CurrentUser.UserId = factory.DefaultUserId;
        var moveAsA = await client.PutAsJsonAsync($"/api/cards/{cardA.Id}/move",
            new { ListId = listB.Id, Position = 0 });
        Assert.That(moveAsA.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
```

(`MoveCardRequest` is `(int ListId, int Position)` — the target list is `ListId`, not `TargetListId`.)

- [ ] **Step 9** — `dotnet test`. Expected **163** (156 + 7 new).
- [ ] **Step 10** — commit: `Scope card queries to the board owner`

### Task 6 — labels

**Interfaces produced:** `GetBoardLabelsAsync(int boardId, string ownerId)`, `GetLabelAsync(int id, string ownerId)`, `CreateLabelAsync(int boardId, string name, string colour, string ownerId)`, `EditLabelAsync(int id, string name, string colour, string ownerId)`, `DeleteLabelAsync(int id, string ownerId)`, `AttachAsync(int cardId, int labelId, string ownerId)`, `DetachAsync(int cardId, int labelId, string ownerId)`, `GetCardLabelsAsync(int cardId, string ownerId)`, `GetLabelIdsByCardAsync(int boardId, string ownerId)`.

- [ ] **Step 1 — the helper** — top of `Wend.Core/EfLabelRepository.cs`:

```csharp
    private IQueryable<Label> Owned(string ownerId) => db.Labels.Where(l => l.Board.OwnerId == ownerId);
```

- [ ] **Step 2 — the join queries scope through the label side.** Labels are board-scoped, so
filtering labels by owner is sufficient for both card-label reads:

```csharp
    public async Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId, string ownerId) =>
        await db.CardLabels
            .Where(cl => cl.CardId == cardId)
            .Join(Owned(ownerId), cl => cl.LabelId, l => l.Id, (cl, l) => l)
            .OrderBy(l => l.Id)
            .ToListAsync();
```

- [ ] **Step 3** — `AttachAsync` and `DetachAsync` take `ownerId` and resolve both the card and the
label through owner-scoped lookups before touching the join table. **Keep the existing same-board
validation exactly as it is** — it now runs after ownership has already excluded other users' rows.
- [ ] **Step 4** — apply the mechanical rule to the remaining methods and update `ILabelRepository`.
- [ ] **Step 5** — `Wend.Api/LabelEndpoints.cs`: guard + `ownerId` on every `labels.*` call.
- [ ] **Step 6** — `Wend.Tests/LabelRepositoryTests.cs`: `_ownerId` on every `_repo.*` call (16 tests).
- [ ] **Step 7** — add to `OwnershipTests.cs`: user B gets 404 reading, editing and deleting user A's
label, and cannot attach their own label to user A's card.
- [ ] **Step 8** — `dotnet test`. Expected **167** (163 + 4 new).
- [ ] **Step 9** — commit: `Scope label queries to the board owner`

### Task 7 — checklist items

**Interfaces produced:** `GetItemsForCardAsync(int cardId, string ownerId)`, `AddItemAsync(int cardId, string text, string ownerId)`, `RenameItemAsync(int id, string text, string ownerId)`, `SetCheckedAsync(int id, bool isChecked, string ownerId)`, `MoveItemAsync(int id, int position, string ownerId)`, `DeleteItemAsync(int id, string ownerId)`, `RestoreItemAsync(int id, string ownerId)`, `GetCountsByCardAsync(int boardId, string ownerId)`.

- [ ] **Step 1 — the helper** — top of `Wend.Core/EfChecklistItemRepository.cs`:

```csharp
    private IQueryable<ChecklistItem> Owned(string ownerId) =>
        db.ChecklistItems.Where(i => i.Card.List.Board.OwnerId == ownerId);
```

- [ ] **Step 2 — settle the open question from the design spec before going further.** This is the
deepest traversal in the codebase and `Card` carries a soft-delete query filter. Today
`GetItemsForCardAsync` filters on the scalar `i.CardId`, so `Card`'s filter never applies and a
soft-deleted card keeps its items — which is exactly what makes card undo restore the checklist.
Adding `i.Card.List.Board` may make EF apply that filter mid-traversal and silently break undo.

Write this regression test **first**, in `Wend.Tests/ChecklistItemRepositoryTests.cs`:

```csharp
    [Test]
    public async Task Items_survive_their_cards_soft_delete()
    {
        // Traversing i.Card for ownership must NOT drag Card's soft-delete filter along:
        // card undo restores the card AND its checklist, so the items have to still be there.
        var boardId = await NewBoardAsync();
        var list = await _lists.CreateListAsync(boardId, "To do", _ownerId);
        var card = await _cards.CreateCardAsync(list.Id, "A card", _ownerId);
        await _repo.AddItemAsync(card.Id, "Step one", _ownerId);

        await _cards.DeleteCardAsync(card.Id, _ownerId);   // soft delete

        var items = await _repo.GetItemsForCardAsync(card.Id, _ownerId);
        Assert.That(items, Has.Count.EqualTo(1));
    }
```

Run it. **If it passes**, EF did not propagate the filter — record that in a one-line comment above
the `Owned` helper and move on. **If it fails**, the traversal is dragging `Card`'s filter in; fix
it by scoping through the card's own ignore-filtered lookup rather than by weakening the ownership
helper, and leave the test in place as the guard.

- [ ] **Step 3** — apply the mechanical rule to all 8 methods and update `IChecklistItemRepository`.
`RestoreItemAsync` keeps `IgnoreQueryFilters()`, scoped exactly as `RestoreCardAsync` in Task 5.
- [ ] **Step 4** — `Wend.Api/ChecklistItemEndpoints.cs`: guard + `ownerId` on every call.
- [ ] **Step 5** — `Wend.Tests/ChecklistItemRepositoryTests.cs`: `_ownerId` on every `_repo.*` call (14 tests).
- [ ] **Step 6** — add to `OwnershipTests.cs`: user B gets 404 reading, renaming, checking, moving,
deleting and restoring user A's checklist item.
- [ ] **Step 7** — `dotnet test`. Expected **174** (167 + 6 isolation + 1 from Step 2).
- [ ] **Step 8** — commit: `Scope checklist item queries to the board owner`

---

## Task 8 — cascade, sweep, and acceptance

- [ ] **Step 1 — prove the erasure cascade.** Add to `OwnershipTests.cs`:

```csharp
    [Test]
    public async Task Deleting_a_user_erases_every_table_their_data_touches()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        // Populate ALL six tables. Labels and CardLabels reach the user through a different FK
        // path (Board → Label → CardLabel) than cards do (Board → List → Card), so they are
        // exactly the rows that could survive unnoticed — and the GDPR erasure claim rests on them.
        var board = await (await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" }))
            .Content.ReadFromJsonAsync<Board>();
        var list = await (await client.PostAsJsonAsync($"/api/boards/{board!.Id}/lists", new { Title = "To do" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();
        var card = await (await client.PostAsJsonAsync($"/api/lists/{list!.Id}/cards", new { Title = "A card" }))
            .Content.ReadFromJsonAsync<Card>();
        var label = await (await client.PostAsJsonAsync($"/api/boards/{board.Id}/labels",
            new { Name = "Urgent", Colour = "red" })).Content.ReadFromJsonAsync<LabelDto>();
        await client.PostAsJsonAsync($"/api/cards/{card!.Id}/labels", new { LabelId = label!.Id });
        await client.PostAsJsonAsync($"/api/cards/{card.Id}/checklist-items", new { Text = "Step one" });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();

        // Sanity: everything really is there before the delete, or the assertions below prove nothing.
        Assert.That(db.CardLabels.Any(), Is.True, "join row was not created — the test proves nothing");
        Assert.That(db.ChecklistItems.IgnoreQueryFilters().Any(), Is.True);

        db.Users.Remove(db.Users.Single(u => u.Id == factory.DefaultUserId));
        await db.SaveChangesAsync();

        Assert.That(db.Boards.Any(), Is.False, "boards survived");
        Assert.That(db.Lists.Any(), Is.False, "lists survived");
        Assert.That(db.Cards.IgnoreQueryFilters().Any(), Is.False, "cards survived");
        Assert.That(db.Labels.Any(), Is.False, "labels survived");
        Assert.That(db.CardLabels.Any(), Is.False, "card-label join rows survived");
        Assert.That(db.ChecklistItems.IgnoreQueryFilters().Any(), Is.False, "checklist items survived");
    }
```

`LabelDto` comes from `Wend.Api`; the colour must be a valid palette key (`Wend.Core.LabelColours`),
so use one the existing label tests already use rather than inventing one.

If any of the last three assertions fail, the cascade path is genuinely incomplete — fix the FK
configuration in `WendDbContext`, do **not** weaken the test.

- [ ] **Step 2 — sweep every endpoint group for 401.** Routes below are the real ones; `/api/health`
is deliberately *not* owner-scoped and must stay `200`.

```csharp
    [Test]
    public async Task Every_owner_scoped_group_is_401_when_anonymous()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();
        factory.CurrentUser.UserId = null;   // after CreateClient, which sets the default user

        foreach (var path in new[]
                 {
                     "/api/boards",
                     "/api/boards/1",
                     "/api/cards/1",
                     "/api/boards/1/labels",
                     "/api/cards/1/labels",
                 })
        {
            var response = await client.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), $"GET {path}");
        }

        Assert.That((await client.DeleteAsync("/api/lists/1")).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.DeleteAsync("/api/checklist-items/1")).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));

        Assert.That((await client.GetAsync("/api/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
```

- [ ] **Step 3 — confirm migrations apply from empty.** Every `WendApiFactory` already creates a
brand-new database that the app migrates at startup, so this asserts nothing is left pending:

```csharp
    [Test]
    public async Task Migrations_apply_cleanly_to_an_empty_database()
    {
        using var factory = new WendApiFactory();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();

        Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);

        // Applied names carry EF's timestamp prefix, e.g. 20260806120000_AddBoardOwner.
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.That(applied.Any(m => m.EndsWith("AddIdentitySchema")), Is.True);
        Assert.That(applied.Any(m => m.EndsWith("AddBoardOwner")), Is.True);
    }
```

`GetPendingMigrationsAsync` needs `using Microsoft.EntityFrameworkCore;`.

- [ ] **Step 4 — settle the guard's shape.** The design spec left this open until the repetition was
visible. It now is: count the `if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();`
lines across the five endpoint files. **Keep them as they are** — Plan 3 puts real authentication in
front of these routes, and a per-handler guard degrades into harmless defence in depth, whereas a
route-group filter would have to be unpicked to let `/api/auth/*` through. Record that decision in a
one-line comment above the guard in `BoardEndpoints.cs` so the next reader does not re-open it.

- [ ] **Step 5 — full green build**

```powershell
Get-Process Wend.Api -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
dotnet test
```

Expected: 0 warnings; **178 passed, 0 failed** (174 + 4 new).

- [ ] **Step 6 — manual acceptance.** Start the app and confirm the honest, expected end state:

```powershell
Start-Service postgresql-x64-17
dotnet run --project Wend.Api
```

`GET http://127.0.0.1:5174/api/health` → `200 {"status":"ok"}`.
`GET http://127.0.0.1:5174/api/boards` → **401**. The board UI is dead until Plan 3 — this is the
accepted consequence of the clean-slate decision, not a bug.

**Confirm how it is dead, and write down what you see.** `MapFallbackToFile("index.html")` still
serves the SPA, and `js/api.js` has no 401 handling until Plan 3, so open
`http://127.0.0.1:5174` in a browser (hard-reload — dev static files carry no `Cache-Control`) and
check the console. Expected: the app shell renders, the board list stays empty, and the console
shows the failed `/api/boards` fetch. What you are ruling out is a **JavaScript crash** — an
unhandled exception that leaves the page half-rendered is a real regression that Plan 3 would
inherit, whereas an empty list plus a logged 401 is the intended dead state. If it crashes, note
the exact error in the PR description so Plan 3's auth gate is written against it.

Sanity-check the schema: `psql -U postgres -d wend -c "\dt"` lists the Wend tables, the seven
`AspNet*` tables and `__EFMigrationsHistory`.

- [ ] **Step 7 — README.** Add two lines to dev setup: (1) every `/api/*` route returns 401 until
Plan 3 lands authentication, so the board UI renders an empty shell and logs a failed fetch —
expected, not a bug; (2) checking out this branch destroys local board data, and returning to
`main` needs `dotnet ef database drop --force` (link the warning at the top of this plan).

- [ ] **Step 8 — commit and open the PR**

```powershell
git add Wend.Tests README.md
git commit -m "Prove ownership cascade and anonymous 401 across every endpoint group"
git push -u origin feature/slice2a-plan2-ownership
```

**The PR description must open with the data-loss warning**, in these words or close to them, so
the reviewer reads it before checking the branch out:

> ⚠️ **This branch destroys local board data.** The `AddBoardOwner` migration deletes all boards,
> and `Migrate()` runs on app startup — so `dotnet run` or `dotnet test` wipes your `wend` dev
> database with no prompt. This is the accepted clean-slate decision from the design spec.
> Switching back to `main` afterwards needs
> `dotnet ef database drop --force --project Wend.Core --startup-project Wend.Api`, because
> `main`'s `Board` has no `OwnerId` while the column remains `NOT NULL`.
>
> Also expected on this branch: every `/api/*` route returns 401 and the board UI renders empty.
> Authentication is Plan 3.

---

## Definition of done

- `dotnet test` green at **178**; `dotnet build` clean at 0 warnings.
- `WendUser` exists, `WendDbContext` is an `IdentityDbContext<WendUser>`, and Identity's seven tables are created by a committed migration.
- `Board.OwnerId` is required and cascades from `WendUser`; deleting a user erases every board, list, card, label, join row and checklist item they own, proven by test.
- All **34** repository methods take an explicit `ownerId`; all **22** former `FindAsync` call sites are owner-scoped lookups.
- Cross-user access returns **404** for every entity and every verb; anonymous access returns **401** for every endpoint group.
- Both `IgnoreQueryFilters()` restore paths remain owner-scoped, with a regression test each.
- No authentication, no Identity services, no frontend change — Plan 3's surface is untouched.
- Foundation ready for **Plan 3 (register + verify + login, and the real `ICurrentUser`)**.
