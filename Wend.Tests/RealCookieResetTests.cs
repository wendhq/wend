using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Wend.Tests;

/// <summary>
/// Password reset on the genuine cookie scheme, no test auth anywhere: the emailed link, the new
/// password, and what happens to sessions that were already live. WebApplicationFactory's client
/// keeps cookies, so the session flows exactly as it does in a browser.
/// </summary>
public class RealCookieResetTests
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
        // useTestAuth: false is authenticated before it does anything, and every assertion below
        // would pass while testing nothing.
        var response = await _client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>Registers and confirms an account over HTTP, the way a browser would.</summary>
    private async Task Arrange(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });
        var link = new Uri(_factory.Email.Sent.Last().Link);
        var query = HttpUtility.ParseQueryString(link.Query);
        await _client.PostAsJsonAsync("/api/auth/verify",
            new { userId = query["userId"], code = query["code"] });
    }

    private async Task<(string UserId, string Code)> RequestReset(string email)
    {
        _factory.Email.Sent.Clear();
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Single().Link).Query);
        return (query["userId"]!, query["code"]!);
    }

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    [Test]
    public async Task Forgot_then_reset_replaces_the_password()
    {
        await Arrange("walker@example.test");
        var (userId, code) = await RequestReset("walker@example.test");

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { userId, code, password = NewPassword });
        var withOld = await Login("walker@example.test", GoodPassword);
        var withNew = await Login("walker@example.test", NewPassword);

        Assert.Multiple(() =>
        {
            Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "reset");
            Assert.That(withOld.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "old password");
            Assert.That(withNew.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "new password");
        });
    }

    [Test]
    public async Task A_live_session_is_refused_on_its_next_request_after_a_reset()
    {
        await Arrange("walker@example.test");
        await Login("walker@example.test", GoodPassword);
        var before = await _client.GetAsync("/api/boards");

        // A second client stands in for the other device — or the attacker holding a stolen
        // session. The reset arrives from somewhere else entirely; this client never re-logs-in.
        using var elsewhere = _factory.CreateClient();
        var (userId, code) = await RequestReset("walker@example.test");
        await elsewhere.PostAsJsonAsync("/api/auth/reset-password",
            new { userId, code, password = NewPassword });

        var after = await _client.GetAsync("/api/boards");

        // This is what Plan 4 bought TimeSpan.Zero for, and the first thing to check it.
        Assert.Multiple(() =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK), "session before the reset");
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "same cookie after");
        });
    }

    [Test]
    public async Task A_locked_out_account_can_sign_in_immediately_after_a_reset()
    {
        await Arrange("locked@example.test");
        for (var attempt = 0; attempt < 5; attempt++)
            await Login("locked@example.test", "wrong wrong wrong wrong");
        var whileLocked = await Login("locked@example.test", GoodPassword);

        var (userId, code) = await RequestReset("locked@example.test");
        await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { userId, code, password = NewPassword });
        var afterReset = await Login("locked@example.test", NewPassword);

        Assert.Multiple(() =>
        {
            Assert.That(whileLocked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "the correct password is refused while locked out");
            Assert.That(afterReset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
                "no fifteen-minute wait after a reset");
        });
    }
}
