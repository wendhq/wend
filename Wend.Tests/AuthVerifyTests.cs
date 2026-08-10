using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class AuthVerifyTests
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

    /// <summary>Registers, then reads userId + code straight out of the emailed link.</summary>
    private async Task<(string UserId, string Code)> RegisterAndCaptureLink(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = GoodPassword, displayName = "Malin" });
        var link = _factory.Email.Sent.Last().Link;
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"]!, query["code"]!);
    }

    private Task<HttpResponseMessage> Verify(string userId, string code) =>
        _client.PostAsJsonAsync("/api/auth/verify", new { userId, code });

    [Test]
    public async Task A_valid_link_confirms_the_account()
    {
        var (userId, code) = await RegisterAndCaptureLink("new@example.test");

        var response = await Verify(userId, code);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var user = await users.FindByIdAsync(userId);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(user!.EmailConfirmed, Is.True);
        });
    }

    [Test]
    public async Task A_reused_link_reports_already_confirmed()
    {
        var (userId, code) = await RegisterAndCaptureLink("new@example.test");
        await Verify(userId, code);

        var response = await Verify(userId, code);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task A_garbled_code_reports_an_expired_link()
    {
        var (userId, _) = await RegisterAndCaptureLink("new@example.test");

        var response = await Verify(userId, "!!!not-base64url!!!");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task An_unknown_user_id_reports_an_expired_link()
    {
        var (_, code) = await RegisterAndCaptureLink("new@example.test");

        var response = await Verify(Guid.NewGuid().ToString(), code);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task A_code_minted_for_another_account_is_refused()
    {
        var (_, victimCode) = await RegisterAndCaptureLink("victim@example.test");
        var (attackerId, _) = await RegisterAndCaptureLink("attacker@example.test");

        var response = await Verify(attackerId, victimCode);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<WendUser>>();
        var attacker = await users.FindByIdAsync(attackerId);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(attacker!.EmailConfirmed, Is.False);
        });
    }
}
