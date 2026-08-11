using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/forgot-password answers 204 to everything, so nearly every test here asserts on which
/// mail went out rather than on the response.
/// </summary>
public class AuthForgotTests
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

    private Task<HttpResponseMessage> Forgot(string email) =>
        _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

    private async Task ConfirmDirectly(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        await users.ConfirmEmailAsync(user!, token);
    }

    [Test]
    public async Task A_confirmed_account_is_emailed_exactly_one_reset_link()
    {
        await Register("member@example.test");
        await ConfirmDirectly("member@example.test");
        _factory.Email.Sent.Clear();

        await Forgot("member@example.test");

        var sent = _factory.Email.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Email, Is.EqualTo("member@example.test"));
            Assert.That(sent.Kind, Is.EqualTo("reset"));
            Assert.That(sent.Link, Does.Contain("/reset-password?userId="));
        });
    }

    [Test]
    public async Task An_unconfirmed_account_is_emailed_a_confirmation_link_and_no_reset_link()
    {
        await Register("waiting@example.test");
        _factory.Email.Sent.Clear();

        await Forgot("waiting@example.test");

        // ResetPasswordAsync does not confirm an address and RequireConfirmedAccount still blocks
        // the login afterwards, so a reset link here would succeed and leave the user at the same
        // wall. The confirmation link is the mail that actually unblocks them.
        var sent = _factory.Email.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Kind, Is.EqualTo("confirm"));
            Assert.That(_factory.Email.Sent.Any(m => m.Kind == "reset"), Is.False);
        });
    }

    [Test]
    public async Task An_unknown_address_is_emailed_nothing()
    {
        await Forgot("stranger@example.test");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task A_malformed_address_is_emailed_nothing()
    {
        await Forgot("not-an-email");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task A_locked_out_account_still_gets_its_link()
    {
        await Register("locked@example.test");
        await ConfirmDirectly("locked@example.test");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await _client.PostAsJsonAsync("/api/auth/login",
                new { email = "locked@example.test", password = "wrong wrong wrong wrong" });
        }
        _factory.Email.Sent.Clear();

        await Forgot("locked@example.test");

        // A reset is precisely how a locked-out owner gets back in. Refusing here would only
        // punish the victim of somebody else's guessing.
        Assert.That(_factory.Email.Sent.Single().Kind, Is.EqualTo("reset"));
    }

    [Test]
    public async Task Every_forgot_outcome_returns_the_same_response()
    {
        await Register("waiting@example.test");
        await Register("member@example.test");
        await ConfirmDirectly("member@example.test");

        var confirmed = await Forgot("member@example.test");
        var unconfirmed = await Forgot("waiting@example.test");
        var unknown = await Forgot("stranger@example.test");
        var rubbish = await Forgot("not-an-email");

        Assert.Multiple(() =>
        {
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(unconfirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(rubbish.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task Forgot_password_binds_json_only()
    {
        // The guard on the reasoning that lets antiforgery wait for Plan 8: an HTML form cannot
        // send application/json, and a cross-site fetch that does triggers a preflight there is no
        // CORS policy to satisfy. If someone later adds form binding, this test says so.
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("email", "member@example.test"),
        ]);

        var response = await _client.PostAsync("/api/auth/forgot-password", form);

        // 404 is the measured behaviour for the equivalent login test (AuthSessionTests).
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
