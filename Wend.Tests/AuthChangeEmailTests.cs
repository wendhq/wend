using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/change-email — the request half. Four different outcomes fall out of the same 204,
/// and which of them is which is invisible from outside on purpose: this endpoint is authenticated
/// and feels private, which is exactly why a 409 for a taken address looks reasonable here. It
/// would let any account holder walk the user table one address at a time from their own settings
/// screen.
/// </summary>
public class AuthChangeEmailTests
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

    /// <summary>Registers and confirms an account WITHOUT pointing the Test scheme at it.</summary>
    private async Task<string> Seed(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));
        return user.Id;
    }

    /// <summary>
    /// Seeds an account and acts as it. NOTE: never call CreateClient() after this —
    /// ConfigureClient resets CurrentUser to the factory's default user.
    /// </summary>
    private async Task<string> ArrangeSignedIn(string email)
    {
        var id = await Seed(email);
        _factory.CurrentUser.UserId = id;
        return id;
    }

    private Task<HttpResponseMessage> Request(string newEmail) =>
        _client.PostAsJsonAsync("/api/auth/change-email", new { newEmail });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        // The length guard is load-bearing: /change-email's malformed-address branch answers a
        // BARE 400 with no body at all, and ReadFromJsonAsync throws on empty content rather than
        // returning null.
        if (response.Content.Headers.ContentLength is null or 0) return null;
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body?.GetValueOrDefault("error");
    }

    private static (string UserId, string NewEmail, string Code) ReadLink(string link)
    {
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"]!, query["newEmail"]!, query["code"]!);
    }

    [Test]
    public async Task An_anonymous_caller_is_refused()
    {
        _factory.CurrentUser.UserId = null;

        var response = await Request("elsewhere@example.test");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_live_session_whose_account_is_gone_is_401_not_500()
    {
        // The same guard as /change-password, on a different handler. A reviewer can reject one
        // while approving the other, so both are asserted.
        _factory.CurrentUser.UserId = Guid.NewGuid().ToString();

        var response = await Request("elsewhere@example.test");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task The_address_the_account_already_has_is_refused_as_same()
    {
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        // Cased differently on purpose: the comparison is normalised, not a raw string compare.
        var response = await Request("MALIN@example.test");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("same"));
            Assert.That(_factory.Email.Sent, Is.Empty);
        });
    }

    [Test]
    public async Task A_malformed_empty_or_over_length_address_is_a_bare_400()
    {
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var empty = await Request("   ");
        var malformed = await Request("not-an-address");
        var tooLong = await Request(new string('a', 250) + "@example.test");

        // Bare, with no code: the screen validates format client-side, so reaching this means a
        // caller bypassing the form, and there is no screen state that needs to tell them apart.
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "empty");
            Assert.That(await ErrorCode(empty), Is.Null, "empty carries no code");
            Assert.That(malformed.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "malformed");
            Assert.That(tooLong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "over 254");
            Assert.That(_factory.Email.Sent, Is.Empty);
        });
    }

    [Test]
    public async Task An_address_with_a_character_a_user_name_cannot_hold_is_a_bare_400()
    {
        // EmailAddressAttribute accepts o'brien@example.test, but UserName is held to
        // AllowedUserNameCharacters. Let it through and the confirm step commits Email, then fails
        // SetUserNameAsync with InvalidUserName: a half-changed account and a false "taken".
        var userId = await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("o'brien@example.test");

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByIdAsync(userId))!;
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.Null, "bare, like the format check");
            Assert.That(_factory.Email.Sent, Is.Empty);
            Assert.That(user.Email, Is.EqualTo("malin@example.test"), "Email");
            Assert.That(user.UserName, Is.EqualTo("malin@example.test"), "UserName");
        });
    }

    [Test]
    public async Task An_address_another_account_holds_gets_a_silent_204_with_no_mail()
    {
        await Seed("taken@example.test");
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("taken@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Email.Sent, Is.Empty,
                "a mail here would answer 'does this address exist' for any account holder");
        });
    }

    [Test]
    public async Task An_address_another_account_holds_only_as_a_user_name_gets_the_same_silent_204()
    {
        var otherId = await Seed("other@example.test");
        await MoveUserNameOnly(otherId, "stranded@example.test");
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("stranded@example.test");

        // FindByEmailAsync alone would call this free and mint a token for an address that then
        // fails DuplicateUserName at confirm time — a link that cannot work, sent to a real inbox.
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Email.Sent, Is.Empty);
        });
    }

    [Test]
    public async Task The_callers_own_stranded_user_name_is_not_treated_as_taken()
    {
        // The 409 half-changed state, arranged directly: Email is the new address, UserName is
        // still the old one. Re-requesting the OLD address is the repair path out of it, and
        // excluding self from the lookup is the only thing that makes it reachable — without it
        // the user gets a silent 204 forever, on the one address they most want back.
        var id = await ArrangeSignedIn("malin@example.test");
        await MoveEmailOnly(id, "moved@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("malin@example.test");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Email.Sent.Single().Kind, Is.EqualTo("change-email"));
            Assert.That(_factory.Email.Sent.Single().Email, Is.EqualTo("malin@example.test"));
        });
    }

    [Test]
    public async Task A_free_address_gets_exactly_one_confirmation_to_the_new_address()
    {
        await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();

        var response = await Request("newer@example.test");

        var sent = _factory.Email.Sent.Single();
        var (userId, newEmail, code) = ReadLink(sent.Link);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(sent.Kind, Is.EqualTo("change-email"));
            Assert.That(sent.Email, Is.EqualTo("newer@example.test"),
                "the confirmation goes to the NEW address, never the old one");
            Assert.That(sent.Link, Does.Contain("/confirm-email-change"));
            Assert.That(userId, Is.Not.Empty);
            Assert.That(newEmail, Is.EqualTo("newer@example.test"));
            Assert.That(code, Is.Not.Empty);
        });
    }

    [Test]
    public async Task An_older_token_still_works_after_a_newer_one_is_issued()
    {
        // Nothing rotates on request, so two requests leave two live tokens and whichever link is
        // clicked first wins. Someone who re-requests BECAUSE they think the first link was seen
        // has revoked nothing. This test writes that down rather than leaving it to be discovered;
        // if it ever starts failing, the revocation model has changed and the design is wrong.
        var id = await ArrangeSignedIn("malin@example.test");
        _factory.Email.Sent.Clear();
        await Request("newer@example.test");
        var first = ReadLink(_factory.Email.Sent.Single().Link);
        await Request("newer@example.test");

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByIdAsync(id))!;
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(first.Code));
        var redeemed = await users.ChangeEmailAsync(user, "newer@example.test", token);

        Assert.That(redeemed.Succeeded, Is.True, "the first link still works");
    }

    [Test]
    public async Task Change_email_binds_json_only()
    {
        await ArrangeSignedIn("malin@example.test");
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("newEmail", "newer@example.test"),
        ]);

        var response = await _client.PostAsync("/api/auth/change-email", form);

        // As in AuthResetTests and AuthForgotTests: 404 is the measured behaviour.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>Leaves an account holding <paramref name="userName"/> as a UserName only.</summary>
    private async Task MoveUserNameOnly(string userId, string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByIdAsync(userId))!;
        var result = await users.SetUserNameAsync(user, userName);
        Assert.That(result.Succeeded, Is.True, "arrangement failed");
    }

    /// <summary>
    /// Writes Email straight through the context, leaving UserName behind — the half-changed state
    /// /confirm-email-change's 409 branch produces. Deliberately corrupt, and deliberately not
    /// built through UserManager, which would keep the two in step.
    /// </summary>
    private async Task MoveEmailOnly(string userId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();
        var row = await db.Users.SingleAsync(u => u.Id == userId);
        row.Email = email;
        row.NormalizedEmail = email.ToUpperInvariant();
        await db.SaveChangesAsync();
    }
}
