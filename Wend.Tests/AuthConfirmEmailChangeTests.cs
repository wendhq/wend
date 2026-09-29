using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/confirm-email-change — the half that applies the change. Anonymous, because the link
/// lands in a mailbox that may be open in a different browser and possession of a token bound to
/// (user, new address, stamp) is the proof.
///
/// The load-bearing test here is Registering_the_old_address_afterwards_still_works: it is the
/// only test that observes the abandoned address staying occupied. Without SetUserNameAsync it
/// fails, along with A_valid_link_changes_both_fields..., which also asserts UserName; nothing
/// else in this repo notices.
/// </summary>
public class AuthConfirmEmailChangeTests
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

    /// <summary>Seeds an account, acts as it, requests a change, and returns the emailed link.</summary>
    private async Task<(string UserId, string NewEmail, string Code)> ArrangeLink(
        string from, string to)
    {
        _factory.CurrentUser.UserId = await Seed(from);
        _factory.Email.Sent.Clear();
        await _client.PostAsJsonAsync("/api/auth/change-email", new { newEmail = to });
        return ReadLink(_factory.Email.Sent.Single().Link);
    }

    private static (string UserId, string NewEmail, string Code) ReadLink(string link)
    {
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"]!, query["newEmail"]!, query["code"]!);
    }

    private Task<HttpResponseMessage> Confirm(string userId, string newEmail, string code) =>
        _client.PostAsJsonAsync("/api/auth/confirm-email-change",
            new { userId, newEmail, code });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        // The length guard is load-bearing: /change-email's malformed-address branch answers a
        // BARE 400 with no body at all, and ReadFromJsonAsync throws on empty content rather than
        // returning null.
        if (response.Content.Headers.ContentLength is null or 0) return null;
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body?.GetValueOrDefault("error");
    }

    private async Task<WendUser> Reload(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        return (await users.FindByIdAsync(userId))!;
    }

    [Test]
    public async Task A_valid_link_changes_both_fields_and_returns_the_stored_address()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");

        var response = await Confirm(userId, newEmail, code);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var user = await Reload(userId);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            // Read back off the row after BOTH writes, so the screen reports what the database
            // holds rather than what the caller asked for.
            Assert.That(body?.GetValueOrDefault("email"), Is.EqualTo("newer@example.test"));
            Assert.That(user.Email, Is.EqualTo("newer@example.test"), "Email");
            Assert.That(user.UserName, Is.EqualTo("newer@example.test"), "UserName");
        });
    }

    [Test]
    public async Task Registering_the_old_address_afterwards_still_works()
    {
        // THE regression test. ChangeEmailAsync does not touch UserName (verified against
        // release/10.0), and RequireUniqueEmail switches on UserValidator's UserName uniqueness
        // check as well as the email one — so without SetUserNameAsync the abandoned address stays
        // occupied. A later registration to it then fails DuplicateUserName, which /register
        // answers with 204 and a code-only log line. The caller sees success, no mail is ever sent,
        // and the failure lands on a stranger months later.
        //
        // Login would keep working either way (it resolves through NormalizedEmail), which is why
        // this is the only test that observes the squatted address. The valid-link test above
        // also fails without the call, because it asserts UserName, but it does so as a side
        // effect of checking the row; this one demonstrates the consequence.
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await Confirm(userId, newEmail, code);
        _factory.Email.Sent.Clear();

        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "malin@example.test", password = GoodPassword, displayName = "Someone" });

        Assert.That(_factory.Email.Sent.Where(s => s.Kind == "confirm"), Is.Not.Empty,
            "the old address is squatted as a UserName — SetUserNameAsync is missing");
    }

    [Test]
    public async Task Sign_in_uses_the_new_address_and_the_old_one_is_refused()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await Confirm(userId, newEmail, code);

        var withNew = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "newer@example.test", password = GoodPassword });
        var withOld = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "malin@example.test", password = GoodPassword });

        Assert.Multiple(() =>
        {
            Assert.That(withNew.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "new address");
            Assert.That(withOld.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "old address");
        });
    }

    [Test]
    public async Task A_reused_link_is_a_token_error()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await Confirm(userId, newEmail, code);

        // Single-use comes from stamp rotation, not from a guard: the first confirmation rotated
        // the stamp the token was bound to.
        var second = await Confirm(userId, newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(second), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task A_tampered_new_email_is_a_token_error()
    {
        var (userId, _, code) = await ArrangeLink("malin@example.test", "newer@example.test");

        // The token is bound to the address, so swapping the address in the URL invalidates it.
        var response = await Confirm(userId, "attacker@example.test", code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
            Assert.That((await Reload(userId)).Email, Is.EqualTo("malin@example.test"));
        });
    }

    [Test]
    public async Task A_token_minted_for_one_user_cannot_change_another()
    {
        var (_, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        var victimId = await Seed("victim@example.test");

        var response = await Confirm(victimId, newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
            Assert.That((await Reload(victimId)).Email, Is.EqualTo("victim@example.test"));
        });
    }

    [Test]
    public async Task A_missing_or_unknown_user_id_is_a_token_error()
    {
        var (_, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");

        var missing = await Confirm("", newEmail, code);
        var unknown = await Confirm(Guid.NewGuid().ToString(), newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "missing");
            Assert.That(await ErrorCode(missing), Is.EqualTo("token"));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "unknown");
            Assert.That(await ErrorCode(unknown), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task An_address_taken_since_the_request_is_409_taken()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        // Somebody else claims it in the minutes between the request and the click. This is where
        // the parent spec's "uniqueness re-checked at confirm time" actually happens: UserValidator
        // firing inside UpdateUserAsync. This plan's job is only to tell the two failures apart.
        await Seed("newer@example.test");

        var response = await Confirm(userId, newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            // Not "token". A dead token means "get a new link"; a taken address means "pick a
            // different address". Collapsing them tells someone their link expired when the link
            // was fine.
            Assert.That(await ErrorCode(response), Is.EqualTo("taken"));
            Assert.That((await Reload(userId)).Email, Is.EqualTo("malin@example.test"));
        });
    }

    [Test]
    public async Task No_notice_goes_to_the_old_address_when_the_confirm_is_a_409()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await Seed("newer@example.test");
        _factory.Email.Sent.Clear();

        var response = await Confirm(userId, newEmail, code);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            // Filter on the kind: for "changed-notice" the Link slot holds the new address, so
            // matching on anything else would misread the recorded mail.
            Assert.That(_factory.Email.Sent.Where(s => s.Kind == "changed-notice"), Is.Empty,
                "the change did not happen, so the old address has nothing to be told about");
        });
    }

    [Test]
    public async Task One_notice_goes_to_the_old_address_after_success_and_none_on_failure()
    {
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        _factory.Email.Sent.Clear();

        await Confirm(userId, newEmail, code);
        var afterSuccess = _factory.Email.Sent.Where(s => s.Kind == "changed-notice").ToList();
        _factory.Email.Sent.Clear();
        await Confirm(userId, newEmail, code);   // the replay: a token failure

        Assert.Multiple(() =>
        {
            Assert.That(afterSuccess, Has.Count.EqualTo(1));
            // Sent to the OLD address, naming the new one — the only mechanism by which an owner
            // learns that somebody with a live session repointed their account.
            Assert.That(afterSuccess[0].Email, Is.EqualTo("malin@example.test"), "recipient");
            Assert.That(afterSuccess[0].Link, Is.EqualTo("newer@example.test"), "names the new one");
            Assert.That(_factory.Email.Sent.Where(s => s.Kind == "changed-notice"), Is.Empty,
                "a notice on failure is an email bomb an attacker aims at the victim");
        });
    }

    [Test]
    public async Task A_password_change_between_the_request_and_the_confirmation_kills_the_link()
    {
        // Correct behaviour, and the kind of coupling that only shows up if something looks for it:
        // both flows rotate the same security stamp, and the change-email token is bound to it.
        var (userId, newEmail, code) = await ArrangeLink("malin@example.test", "newer@example.test");
        await _client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = GoodPassword, newPassword = "a different long passphrase" });

        var response = await Confirm(userId, newEmail, code);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task Confirm_email_change_binds_json_only()
    {
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("userId", "whoever"),
            new KeyValuePair<string, string>("newEmail", "newer@example.test"),
            new KeyValuePair<string, string>("code", "whatever"),
        ]);

        var response = await _client.PostAsync("/api/auth/confirm-email-change", form);

        // As in AuthResetTests and AuthForgotTests: 404 is the measured behaviour.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
