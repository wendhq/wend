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
        // UserManager is IDisposable and resolved into a field, so NUnit1032 requires it be
        // disposed here by name — the scope would dispose it anyway, and a second Dispose is a
        // no-op.
        _users.Dispose();
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
