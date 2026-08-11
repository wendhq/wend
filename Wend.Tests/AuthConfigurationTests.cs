using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wend.Api;

namespace Wend.Tests;

/// <summary>
/// The auth configuration is security policy expressed as settings, so it is asserted rather than
/// eyeballed. Every value here is one the design doc locked; changing one should break a test.
/// </summary>
public class AuthConfigurationTests
{
    private WendApiFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _factory.CreateClient().Dispose();   // boots the app
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public void An_unconfirmed_account_cannot_sign_in()
    {
        var options = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.That(options.SignIn.RequireConfirmedAccount, Is.True);
    }

    [Test]
    public void Five_failures_lock_an_account_for_fifteen_minutes()
    {
        var options = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.Lockout.MaxFailedAccessAttempts, Is.EqualTo(5));
            Assert.That(options.Lockout.DefaultLockoutTimeSpan, Is.EqualTo(TimeSpan.FromMinutes(15)));
            // Without this a brand-new account — the one an attacker reaches first — is exempt.
            Assert.That(options.Lockout.AllowedForNewUsers, Is.True);
        });
    }

    [Test]
    public void The_session_cookie_is_locked_down()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Multiple(() =>
        {
            Assert.That(options.Cookie.Name, Is.EqualTo("wend.session"));
            Assert.That(options.Cookie.HttpOnly, Is.True);
            Assert.That(options.Cookie.SameSite, Is.EqualTo(SameSiteMode.Lax));
            Assert.That(options.SlidingExpiration, Is.True);
            Assert.That(options.ExpireTimeSpan, Is.EqualTo(TimeSpan.FromDays(7)));
            // Development only. Always outside it — asserted by reading the environment guard in
            // Program.cs at review, because the test host pins Development.
            Assert.That(options.Cookie.SecurePolicy, Is.EqualTo(CookieSecurePolicy.SameAsRequest));
        });
    }

    [Test]
    public void The_security_stamp_is_revalidated_on_every_request()
    {
        var options = _factory.Services
            .GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;

        // Zero, not the 30-minute default: a password reset (Plan 5) and an account deletion
        // (Plan 7) must evict a live session on its NEXT request, not within half an hour.
        Assert.That(options.ValidationInterval, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Reset_tokens_last_an_hour_and_confirmation_tokens_still_last_a_day()
    {
        var identity = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var confirmation = _factory.Services
            .GetRequiredService<IOptions<EmailConfirmationTokenProviderOptions>>().Value;
        var reset = _factory.Services
            .GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(reset.TokenLifespan, Is.EqualTo(TimeSpan.FromHours(1)));
            // Both, in one test, on purpose: the failure this pairing exists to prevent is a
            // change to either lifespan silently dragging the other with it.
            Assert.That(confirmation.TokenLifespan, Is.EqualTo(TimeSpan.FromHours(24)));
            Assert.That(identity.Tokens.PasswordResetTokenProvider, Is.EqualTo("WendPasswordReset"));
            Assert.That(identity.Tokens.EmailConfirmationTokenProvider,
                Is.EqualTo("WendEmailConfirmation"));
        });
    }

    [Test]
    public void Every_board_family_endpoint_requires_authorization()
    {
        // The per-handler guards would return 401 anyway, so a status-code test cannot tell whether
        // RequireAuthorization() is actually in front. Endpoint metadata can.
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } path
                && path.StartsWith("/api/", StringComparison.Ordinal)
                && !path.StartsWith("/api/auth/", StringComparison.Ordinal)
                && path != "/api/health"
                && !path.Contains("{**path}", StringComparison.Ordinal))
            .ToList();

        Assert.That(endpoints, Is.Not.Empty, "no board-family endpoints were found to check");
        Assert.That(endpoints.Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null),
            Is.Empty, "these endpoints are missing RequireAuthorization()");
    }
}
