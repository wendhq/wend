using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Wend.Tests;

/// <summary>
/// Account settings on the genuine cookie scheme, no test auth anywhere: which sessions survive a
/// password change, which die, and whether a remembered cookie stays remembered. Every assertion
/// here is one the Test scheme cannot make — see the note in the plan.
/// </summary>
public class RealCookieAccountTests
{
    private const string GoodPassword = "correct horse battery staple";
    private const string NewPassword = "a different long passphrase";

    private WendApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory(useTestAuth: false);
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task The_test_scheme_really_is_off()
    {
        // The canary, and deliberately the first test in the file. The factory seeds a default user
        // and points CurrentUser at it on every CreateClient(), so a suite that forgot
        // useTestAuth: false is authenticated before it does anything — and every assertion below
        // would pass while testing nothing. This repo has been bitten by that shape twice.
        var response = await _client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>Registers and confirms an account over HTTP, the way a browser would.</summary>
    private async Task Arrange(HttpClient client, string email)
    {
        _factory.Email.Sent.Clear();
        await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });
        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Last().Link).Query);
        await client.PostAsJsonAsync("/api/auth/verify",
            new { userId = query["userId"], code = query["code"] });
    }

    private static Task<HttpResponseMessage> Login(
        HttpClient client, string email, bool rememberMe = false) =>
        client.PostAsJsonAsync("/api/auth/login",
            new { email, password = GoodPassword, rememberMe });

    private static Task<HttpResponseMessage> ChangePassword(HttpClient client, string newPassword) =>
        client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = GoodPassword, newPassword });

    // Last(), not Single(): a change-password response can carry more than one wend.session
    // Set-Cookie — RefreshSignInAsync writes one, and SlidingExpiration's renewal can write another.
    // The last one is the cookie the browser ends up holding.
    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Last(c => c.StartsWith("wend.session="));

    [Test]
    public async Task The_acting_session_survives_a_password_change_while_another_one_dies()
    {
        await Arrange(_client, "walker@example.test");
        await Login(_client, "walker@example.test");

        // A second browser, signed in as the same person. Its cookie is what the password change
        // is supposed to kill.
        using var elsewhere = _factory.CreateClient();
        await Login(elsewhere, "walker@example.test");
        var elsewhereBefore = await elsewhere.GetAsync("/api/boards");

        var changed = await ChangePassword(_client, NewPassword);

        var actingAfter = await _client.GetAsync("/api/boards");
        var elsewhereAfter = await elsewhere.GetAsync("/api/boards");

        // The PAIR is the point. Either assertion alone is satisfiable by a wrong implementation:
        // drop RefreshSignInAsync and the second passes while the first fails; skip the stamp
        // rotation and the reverse.
        Assert.Multiple(() =>
        {
            Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "change");
            Assert.That(elsewhereBefore.StatusCode, Is.EqualTo(HttpStatusCode.OK), "other, before");
            Assert.That(actingAfter.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "the browser that changed the password must NOT be signed out");
            Assert.That(elsewhereAfter.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "every other session must be");
        });
    }

    [Test]
    public async Task A_remembered_session_is_still_remembered_after_a_password_change()
    {
        await Arrange(_client, "remembered@example.test");
        await Login(_client, "remembered@example.test", rememberMe: true);

        using var plain = _factory.CreateClient();
        await Arrange(plain, "forgotten@example.test");
        await Login(plain, "forgotten@example.test");

        var remembered = await ChangePassword(_client, NewPassword);
        var session = await ChangePassword(plain, NewPassword);

        // Both directions, because "always writes expires=" would satisfy the first alone. If the
        // reissued cookie loses its persistence, a remembered login silently becomes a session
        // cookie and the user is signed out a day later with nothing to blame.
        //
        // The two status checks keep this from passing on a failure. A refused request can carry
        // a deletion cookie, and that one says expires=Thu, 01 Jan 1970, which satisfies the first
        // cookie assertion while proving nothing.
        Assert.Multiple(() =>
        {
            Assert.That(remembered.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "remembered change");
            Assert.That(session.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "session change");
            Assert.That(SessionCookie(remembered), Does.Contain("expires=").IgnoreCase,
                "remember-me must survive the reissue");
            Assert.That(SessionCookie(session), Does.Not.Contain("expires=").IgnoreCase,
                "and a session cookie must stay one");
        });
    }

    [Test]
    public async Task Every_session_dies_after_an_email_change_including_the_one_that_asked()
    {
        await Arrange(_client, "walker@example.test");
        await Login(_client, "walker@example.test");
        var before = await _client.GetAsync("/api/boards");

        _factory.Email.Sent.Clear();
        await _client.PostAsJsonAsync("/api/auth/change-email",
            new { newEmail = "newer@example.test" });
        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Single().Link).Query);

        // Confirmed from a SECOND client with no session, which is the realistic shape: the link
        // lands in a mailbox that may be open in a different browser entirely.
        using var mailbox = _factory.CreateClient();
        var confirmed = await mailbox.PostAsJsonAsync("/api/auth/confirm-email-change",
            new { userId = query["userId"], newEmail = query["newEmail"], code = query["code"] });

        var after = await _client.GetAsync("/api/boards");

        // No carve-out here, unlike change-password, and that is deliberate: this request is
        // anonymous and may not be coming from the session that asked, so there is no acting
        // session to refresh.
        Assert.Multiple(() =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK), "before");
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.OK), "confirm");
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "after");
        });
    }
}
