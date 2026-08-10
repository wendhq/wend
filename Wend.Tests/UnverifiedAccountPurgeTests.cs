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
        // WendDbContext is IDisposable and resolved into a field, so NUnit1032 requires it be
        // disposed here by name — the scope would dispose it anyway, and a second Dispose is a
        // no-op.
        _db.Dispose();
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
