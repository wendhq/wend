using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// /api/auth/change-password. The two 400 codes are asserted by name because the Account screen
/// branches on them: one blames the new password, the other the current one, and swapping them
/// produces a screen that tells the user to fix the field they got right.
///
/// The Test auth scheme is deliberate here: this file is about the handler's logic. What the
/// scheme cannot show (the acting session surviving, other sessions dying, persistence carrying
/// across) is in RealCookieAccountTests, because RefreshSignInAsync is a silent no-op without a
/// real cookie and SecurityStampValidator never runs at all.
/// </summary>
public class AuthChangePasswordTests
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

    /// <summary>
    /// Registers and confirms an account, then points the Test scheme at it. Everything after this
    /// call acts as that user. NOTE: never call CreateClient() again afterwards, because
    /// resets CurrentUser to the factory's default user.
    /// </summary>
    private Task<string> ArrangeSignedIn(string email) => ArrangeSignedIn(_factory, _client, email);

    private static async Task<string> ArrangeSignedIn(
        WendApiFactory factory, HttpClient client, string email)
    {
        await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));

        factory.CurrentUser.UserId = user.Id;
        return user.Id;
    }

    private Task<HttpResponseMessage> Change(string currentPassword, string newPassword) =>
        Change(_client, currentPassword, newPassword);

    private static Task<HttpResponseMessage> Change(
        HttpClient client, string currentPassword, string newPassword) =>
        client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword, newPassword });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        // The length guard is load-bearing: /change-email's malformed-address branch answers a
        // BARE 400 with no body at all, and ReadFromJsonAsync throws on empty content rather than
        // returning null.
        if (response.Content.Headers.ContentLength is null or 0) return null;
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body?.GetValueOrDefault("error");
    }

    private Task<bool> PasswordWorks(string email, string password) =>
        PasswordWorks(_factory, email, password);

    private static async Task<bool> PasswordWorks(
        WendApiFactory factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByEmailAsync(email);
        return await users.CheckPasswordAsync(user!, password);
    }

    private async Task<WendUser> Reload(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        return (await users.FindByEmailAsync(email))!;
    }

    [Test]
    public async Task An_anonymous_caller_is_refused()
    {
        _factory.CurrentUser.UserId = null;

        var response = await Change(GoodPassword, NewPassword);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_live_session_whose_account_is_gone_is_401_not_500()
    {
        // GetUserAsync returns null rather than throwing (verified against release/10.0). Plan 7
        // makes this ordinary; today it is reachable by pointing the scheme at an id nobody owns.
        _factory.CurrentUser.UserId = Guid.NewGuid().ToString();

        var response = await Change(GoodPassword, NewPassword);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task The_new_password_replaces_the_old_one()
    {
        await ArrangeSignedIn("changer@example.test");

        var response = await Change(GoodPassword, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await PasswordWorks("changer@example.test", NewPassword), Is.True, "new");
            Assert.That(await PasswordWorks("changer@example.test", GoodPassword), Is.False, "old");
        });
    }

    [Test]
    public async Task A_wrong_current_password_is_refused_with_the_current_code()
    {
        await ArrangeSignedIn("changer@example.test");

        var response = await Change("not the password at all", NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("current"));
            Assert.That(await PasswordWorks("changer@example.test", GoodPassword), Is.True);
        });
    }

    [Test]
    public async Task A_weak_new_password_is_refused_before_the_current_one_is_ever_checked()
    {
        await ArrangeSignedIn("changer@example.test");

        // Deliberately BOTH wrong: a weak new password AND a wrong current one. If the handler
        // called ChangePasswordAsync first, this would spend a lockout attempt and answer
        // "current". The ordering is the assertion.
        var response = await Change("also wrong", "short");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await ErrorCode(response), Is.EqualTo("password"));
            Assert.That((await Reload("changer@example.test")).AccessFailedCount, Is.Zero,
                "a weak new password must not cost an attempt at the current one");
            Assert.That(await PasswordWorks("changer@example.test", GoodPassword), Is.True);
        });
    }

    [Test]
    public async Task Five_wrong_current_passwords_lock_the_account()
    {
        await ArrangeSignedIn("guessed@example.test");

        for (var attempt = 0; attempt < 5; attempt++)
            await Change("wrong wrong wrong wrong", NewPassword);

        var user = await Reload("guessed@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(user.LockoutEnd, Is.Not.Null);
            Assert.That(user.LockoutEnd, Is.GreaterThan(DateTimeOffset.UtcNow));
        });
    }

    [Test]
    public async Task A_locked_out_account_is_refused_without_its_password_being_checked()
    {
        await ArrangeSignedIn("guessed@example.test");
        for (var attempt = 0; attempt < 5; attempt++)
            await Change("wrong wrong wrong wrong", NewPassword);

        // The CORRECT current password, while locked. 401, and nothing changes — otherwise anyone
        // holding a stolen session sidesteps lockout by guessing here instead of at /login.
        var response = await Change(GoodPassword, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(await PasswordWorks("guessed@example.test", GoodPassword), Is.True);
            Assert.That(await PasswordWorks("guessed@example.test", NewPassword), Is.False);
        });
    }

    [Test]
    public async Task A_successful_change_clears_the_failed_count()
    {
        await ArrangeSignedIn("recovered@example.test");
        await Change("wrong wrong wrong wrong", NewPassword);
        await Change("wrong wrong wrong wrong", NewPassword);
        var before = (await Reload("recovered@example.test")).AccessFailedCount;

        await Change(GoodPassword, NewPassword);

        var after = (await Reload("recovered@example.test")).AccessFailedCount;
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(2), "two failures were recorded");
            Assert.That(after, Is.Zero, "a successful change clears them");
        });
    }

    [Test]
    public async Task Change_password_binds_json_only()
    {
        await ArrangeSignedIn("changer@example.test");
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("currentPassword", GoodPassword),
            new KeyValuePair<string, string>("newPassword", NewPassword),
        ]);

        var response = await _client.PostAsync("/api/auth/change-password", form);

        // As in AuthResetTests and AuthForgotTests: 404 is the measured behaviour.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RefreshSignInAsync_failing_still_answers_204()
    {
        // The password write has committed by the time the session is refreshed, so a refresh that
        // throws must not turn a real change into an error the user retries.
        using var factory = new WendApiFactory(configureServices: services =>
            services.AddScoped<SignInManager<WendUser>, ThrowingRefreshSignInManager>());
        using var client = factory.CreateClient();
        await ArrangeSignedIn(factory, client, "refresh@example.test");

        var response = await Change(client, GoodPassword, NewPassword);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await PasswordWorks(factory, "refresh@example.test", NewPassword), Is.True,
                "the change persisted");
        });
    }

    /// <summary>A SignInManager whose RefreshSignInAsync throws, the only way that method fails.</summary>
    private sealed class ThrowingRefreshSignInManager(
        UserManager<WendUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<WendUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<WendUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<WendUser> confirmation)
        : SignInManager<WendUser>(userManager, contextAccessor, claimsFactory, optionsAccessor,
            logger, schemes, confirmation)
    {
        public override Task RefreshSignInAsync(WendUser user) =>
            throw new InvalidOperationException("simulated refresh failure");
    }
}
