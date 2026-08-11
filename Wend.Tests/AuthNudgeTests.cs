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
