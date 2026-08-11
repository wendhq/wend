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
