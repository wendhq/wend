using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Wend.Tests;

/// <summary>
/// The browser's path, end to end, with no test scheme in sight: register, confirm, sign in, use
/// the cookie, sign out. WebApplicationFactory's client keeps cookies by default, so the session
/// flows exactly as it does in a browser.
/// </summary>
public class RealCookieAuthTests
{
    private const string GoodPassword = "correct horse battery staple";

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
        // The canary, and deliberately the first test in the file. WendApiFactory seeds a default
        // user and points CurrentUser at it on every CreateClient(), so a suite constructed without
        // useTestAuth: false is authenticated before it does anything — and every assertion below
        // would pass while testing nothing. This repo has been bitten by that shape twice.
        var response = await _client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Boards_are_refused_without_a_session()
    {
        var response = await _client.GetAsync("/api/boards");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Register_confirm_sign_in_use_and_sign_out()
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "walker@example.test", password = GoodPassword, displayName = "Malin" });

        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Single().Link).Query);
        var confirmed = await _client.PostAsJsonAsync("/api/auth/verify",
            new { userId = query["userId"], code = query["code"] });

        var signedIn = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "walker@example.test", password = GoodPassword });

        // No header is set by hand anywhere in this test: the cookie the login response issued is
        // what carries the session from here.
        var boards = await _client.GetAsync("/api/boards");
        var me = await _client.GetAsync("/api/auth/me");

        var signedOut = await _client.PostAsync("/api/auth/logout", null);
        var afterLogout = await _client.GetAsync("/api/boards");

        Assert.Multiple(() =>
        {
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "verify");
            Assert.That(signedIn.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "login");
            Assert.That(boards.StatusCode, Is.EqualTo(HttpStatusCode.OK), "boards with a session");
            Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK), "me with a session");
            Assert.That(signedOut.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "logout");
            Assert.That(afterLogout.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "boards after logout");
        });
    }

    [Test]
    public async Task Remember_me_issues_a_cookie_that_survives_the_browser()
    {
        await RegisterAndConfirmAsync("remembered@example.test");

        var signedIn = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "remembered@example.test", password = GoodPassword, rememberMe = true });

        // expires= is the entire difference between the two cases: a cookie carrying one is written
        // to disk and outlives closing the browser, a cookie without one does not.
        Assert.That(SessionCookie(signedIn), Does.Contain("expires=").IgnoreCase);
    }

    [Test]
    public async Task Without_remember_me_the_cookie_dies_with_the_browser()
    {
        await RegisterAndConfirmAsync("forgotten@example.test");

        // The field is omitted rather than sent as false, because that is what every client written
        // before remember-me sends. It guards LoginRequest's default: drop it and this suite's
        // other logins would silently start issuing persistent cookies nobody asked for.
        var signedIn = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "forgotten@example.test", password = GoodPassword });

        Assert.That(SessionCookie(signedIn), Does.Not.Contain("expires=").IgnoreCase);
    }

    private async Task RegisterAndConfirmAsync(string address)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = address, password = GoodPassword, displayName = "Malin" });

        var query = HttpUtility.ParseQueryString(new Uri(_factory.Email.Sent.Single().Link).Query);
        await _client.PostAsJsonAsync("/api/auth/verify",
            new { userId = query["userId"], code = query["code"] });
    }

    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("wend.session="));
}
