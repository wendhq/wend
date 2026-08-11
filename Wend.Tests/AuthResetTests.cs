using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/reset-password. The two 400 codes are asserted by name because the reset screen
/// branches on them: one tells the user their password is too short, the other tells them their
/// link is dead, and swapping them produces a screen that lies.
/// </summary>
public class AuthResetTests
{
    private const string GoodPassword = "correct horse battery staple";
    private const string NewPassword = "a different long passphrase";

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

    /// <summary>Registers, confirms, requests a reset, and returns the emailed userId + code.</summary>
    private async Task<(string UserId, string Code)> ArrangeResetLink(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });
        await ConfirmDirectly(email);
        _factory.Email.Sent.Clear();
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        return ReadLink(_factory.Email.Sent.Last().Link);
    }

    private static (string UserId, string Code) ReadLink(string link)
    {
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"]!, query["code"]!);
    }

    private Task<HttpResponseMessage> Reset(string userId, string code, string password) =>
        _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, code, password });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body?.GetValueOrDefault("error");
    }

    private async Task ConfirmDirectly(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        await users.ConfirmEmailAsync(user!, token);
    }

    private async Task<WendUser> Reload(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        return (await users.FindByEmailAsync(email))!;
    }

    private async Task<bool> PasswordWorks(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        return await users.CheckPasswordAsync(user!, password);
    }

    [Test]
    public async Task A_valid_token_sets_the_new_password()
    {
        var (userId, code) = await ArrangeResetLink("member@example.test");

        var response = await Reset(userId, code, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await PasswordWorks("member@example.test", NewPassword), Is.True);
            Assert.That(await PasswordWorks("member@example.test", GoodPassword), Is.False);
        });
    }

    [Test]
    public async Task A_reused_token_is_refused()
    {
        var (userId, code) = await ArrangeResetLink("member@example.test");
        await Reset(userId, code, NewPassword);

        var second = await Reset(userId, code, "yet another long passphrase");

        // Identity's tokens are stamp-bound, and a completed reset rotates the stamp. That is
        // where the single-use guarantee comes from — there is no explicit guard to read.
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(second), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task An_older_token_still_works_after_a_newer_one_is_issued()
    {
        var (userId, older) = await ArrangeResetLink("member@example.test");
        await _client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = "member@example.test" });

        var response = await Reset(userId, older, NewPassword);

        // Requesting a new link revokes nothing: only a COMPLETED reset rotates the stamp. Asking
        // for a fresh link because you think the old one was seen does not kill the old one.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task A_token_minted_for_one_account_cannot_reset_another()
    {
        var (mine, code) = await ArrangeResetLink("mine@example.test");
        var (theirs, _) = await ArrangeResetLink("theirs@example.test");
        Assert.That(mine, Is.Not.EqualTo(theirs));

        var response = await Reset(theirs, code, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
            Assert.That(await PasswordWorks("theirs@example.test", NewPassword), Is.False);
        });
    }

    [Test]
    public async Task A_garbage_code_is_refused()
    {
        var (userId, _) = await ArrangeResetLink("member@example.test");

        var notBase64Url = await Reset(userId, "!!!not base64!!!", NewPassword);
        var wellFormedRubbish = await Reset(userId,
            WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("not a token")), NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(notBase64Url.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(notBase64Url), Is.EqualTo("token"));
            Assert.That(wellFormedRubbish.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(wellFormedRubbish), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task An_unknown_user_id_is_refused()
    {
        var (_, code) = await ArrangeResetLink("member@example.test");

        var response = await Reset(Guid.NewGuid().ToString(), code, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("token"));
        });
    }

    [Test]
    public async Task A_weak_password_is_refused_by_code_and_leaves_the_token_usable()
    {
        var (userId, code) = await ArrangeResetLink("member@example.test");

        var weak = await Reset(userId, code, "short");
        var retry = await Reset(userId, code, NewPassword);

        // The whole point of validating policy BEFORE redeeming the token: the user is told which
        // of the two things went wrong, and a rejected attempt does not cost them their link.
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(weak.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(weak), Is.EqualTo("password"));
            Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task A_successful_reset_clears_the_lockout()
    {
        var (userId, code) = await ArrangeResetLink("locked@example.test");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await _client.PostAsJsonAsync("/api/auth/login",
                new { email = "locked@example.test", password = "wrong wrong wrong wrong" });
        }
        Assert.That((await Reload("locked@example.test")).LockoutEnd, Is.Not.Null, "arrange failed");

        await Reset(userId, code, NewPassword);

        // Two columns, two calls. Resetting the count alone leaves a live LockoutEnd and the user
        // holds a working password they cannot use for fifteen minutes.
        var user = await Reload("locked@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(user.LockoutEnd, Is.Null);
            Assert.That(user.AccessFailedCount, Is.Zero);
        });
    }

    [Test]
    public async Task Resetting_does_not_confirm_an_unconfirmed_address()
    {
        // Unreachable through forgot-password, which never mints a reset token for an unconfirmed
        // account — so the token is minted directly. This pins the reason that branch exists: a
        // reset is not a second, quieter way through the verification gate.
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "waiting@example.test", password = GoodPassword, displayName = "Malin" });
        string userId, code;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
            var user = (await users.FindByEmailAsync("waiting@example.test"))!;
            userId = user.Id;
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
                await users.GeneratePasswordResetTokenAsync(user)));
        }

        var response = await Reset(userId, code, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That((await Reload("waiting@example.test")).EmailConfirmed, Is.False);
        });
    }

    [Test]
    public async Task Reset_password_binds_json_only()
    {
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("userId", "whoever"),
            new KeyValuePair<string, string>("code", "whatever"),
            new KeyValuePair<string, string>("password", NewPassword),
        ]);

        var response = await _client.PostAsync("/api/auth/reset-password", form);

        // As in AuthForgotTests: 404 is the measured behaviour.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
