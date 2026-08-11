using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wend.Core;

namespace Wend.Tests;

public class AuthSessionTests
{
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

    [Test]
    public async Task Me_returns_the_signed_in_users_name_and_address()
    {
        var response = await _client.GetAsync("/api/auth/me");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("displayName").GetString(), Is.EqualTo("Default Test User"));
            Assert.That(body.GetProperty("email").GetString(), Is.EqualTo("default@example.test"));
        });
    }

    [Test]
    public async Task Me_is_401_when_nobody_is_signed_in()
    {
        // The gate's boot check depends on this exact answer: it is the signal to mount login, not
        // an error to report.
        _factory.CurrentUser.UserId = null;

        var response = await _client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Logging_out_succeeds_for_a_signed_in_user()
    {
        var response = await _client.PostAsync("/api/auth/logout", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Logging_out_is_refused_for_an_anonymous_caller()
    {
        // An anonymous logout endpoint is a free CSRF target: it costs an attacker nothing and a
        // victim their session.
        _factory.CurrentUser.UserId = null;

        var response = await _client.PostAsync("/api/auth/logout", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_form_encoded_login_is_refused()
    {
        // This is the guard on the reasoning that lets antiforgery wait for Plan 8: /api/auth/*
        // binds JSON only, and an HTML form cannot send application/json, so a cross-site form
        // POST cannot log anyone in. If someone later adds form binding, this test is what tells
        // them they have opened login-CSRF.
        //
        // 404, not the 415 the plan predicted: the form-encoded request never matches /login (which
        // declares a JSON body), so Wend's own /api/{**path} catch-all claims it. The property
        // under test is "not processed as a login", and a 404 satisfies it exactly as a 415 would.
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("email", "default@example.test"),
            new KeyValuePair<string, string>("password", "correct horse battery staple"),
        ]);

        var response = await _client.PostAsync("/api/auth/login", form);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
