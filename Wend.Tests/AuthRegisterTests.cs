using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class AuthRegisterTests
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

    private Task<HttpResponseMessage> Register(
        string email = "new@example.test",
        string password = GoodPassword,
        string displayName = "Malin") =>
        _client.PostAsJsonAsync("/api/auth/register", new { email, password, displayName });

    private async Task<WendUser?> Find(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        return await users.FindByEmailAsync(email);
    }

    [Test]
    public async Task Registering_creates_an_unconfirmed_account()
    {
        var response = await Register();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var user = await Find("new@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(user, Is.Not.Null);
            Assert.That(user!.EmailConfirmed, Is.False);
            Assert.That(user.DisplayName, Is.EqualTo("Malin"));
        });
    }

    [Test]
    public async Task Registering_emails_a_confirmation_link()
    {
        await Register();

        var sent = _factory.Email.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Email, Is.EqualTo("new@example.test"));
            Assert.That(sent.Link, Does.Contain("/verify?userId="));
            Assert.That(sent.Link, Does.Contain("&code="));
        });
    }

    [Test]
    public async Task Registering_a_taken_address_reports_the_same_generic_success()
    {
        await Register(email: "taken@example.test");
        var user = await Find("taken@example.test");
        await Confirm(user!.Id);
        _factory.Email.Sent.Clear();

        var response = await Register(email: "taken@example.test", displayName: "Impostor");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Registering_a_taken_address_neither_creates_nor_emails_anything()
    {
        await Register(email: "taken@example.test");
        var user = await Find("taken@example.test");
        await Confirm(user!.Id);
        _factory.Email.Sent.Clear();

        await Register(email: "taken@example.test", displayName: "Impostor");

        var stored = await Find("taken@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(stored!.DisplayName, Is.EqualTo("Malin"), "the existing account must be untouched");
            Assert.That(_factory.Email.Sent, Is.Empty, "a confirmed account must not be emailed");
        });
    }

    [Test]
    public async Task Registering_an_unconfirmed_address_resends_the_link()
    {
        await Register(email: "squatted@example.test");
        _factory.Email.Sent.Clear();

        var response = await Register(email: "squatted@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Email.Sent.Single().Email, Is.EqualTo("squatted@example.test"));
        });
    }

    [Test]
    public async Task A_blank_display_name_is_rejected()
    {
        var response = await Register(displayName: "   ");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task An_over_long_display_name_is_rejected()
    {
        var response = await Register(displayName: new string('x', 101));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Control_characters_are_stripped_from_the_display_name()
    {
        // A null and an escape character inside the name, written as C# escape sequences so they
        // survive copy-paste. Both must be stripped; the ordinary letters must not be.
        await Register(displayName: "Ma\u0000lin\u001B");

        var user = await Find("new@example.test");
        Assert.That(user!.DisplayName, Is.EqualTo("Malin"));
    }

    [Test]
    public async Task A_weak_password_is_rejected()
    {
        var response = await Register(password: "short1!");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // The oracle this endpoint is most likely to grow by accident: if the password policy is only
    // checked inside CreateAsync — which runs for a free address and not a taken one — then a
    // deliberately weak password answers 400 for free and 204 for taken, and one request per
    // address enumerates the whole user table. Validation order is the fix; this is the guard.
    [Test]
    public async Task A_weak_password_answers_the_same_for_a_taken_address_as_a_free_one()
    {
        await Register(email: "taken@example.test");

        var free = await Register(email: "free@example.test", password: "short1!");
        var taken = await Register(email: "taken@example.test", password: "short1!");

        Assert.That(taken.StatusCode, Is.EqualTo(free.StatusCode));
    }

    [Test]
    public async Task An_address_that_is_not_an_email_is_rejected()
    {
        var response = await Register(email: "not-an-email");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>Marks an account confirmed without going through the endpoint (Task 4 tests that).</summary>
    private async Task Confirm(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByIdAsync(userId);
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        await users.ConfirmEmailAsync(user!, token);
    }
}
