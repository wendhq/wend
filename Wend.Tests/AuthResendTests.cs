using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class AuthResendTests
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

    private Task<HttpResponseMessage> Resend(string email) =>
        _client.PostAsJsonAsync("/api/auth/resend-verification", new { email });

    [Test]
    public async Task Resending_emails_a_fresh_link_to_an_unconfirmed_account()
    {
        await Register("waiting@example.test");
        _factory.Email.Sent.Clear();

        await Resend("waiting@example.test");

        Assert.That(_factory.Email.Sent.Single().Email, Is.EqualTo("waiting@example.test"));
    }

    [Test]
    public async Task Resending_to_a_confirmed_account_sends_nothing()
    {
        await Register("done@example.test");
        await ConfirmDirectly("done@example.test");
        _factory.Email.Sent.Clear();

        await Resend("done@example.test");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task Resending_to_an_unknown_address_sends_nothing()
    {
        await Resend("stranger@example.test");

        Assert.That(_factory.Email.Sent, Is.Empty);
    }

    [Test]
    public async Task Every_resend_outcome_returns_the_same_response()
    {
        await Register("waiting@example.test");
        await Register("done@example.test");
        await ConfirmDirectly("done@example.test");

        var unconfirmed = await Resend("waiting@example.test");
        var confirmed = await Resend("done@example.test");
        var unknown = await Resend("stranger@example.test");
        var rubbish = await Resend("not-an-email");

        Assert.Multiple(() =>
        {
            Assert.That(unconfirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(rubbish.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    private async Task ConfirmDirectly(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        await users.ConfirmEmailAsync(user!, token);
    }
}
