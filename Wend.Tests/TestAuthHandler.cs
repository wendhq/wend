using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Wend.Tests;

/// <summary>
/// Authenticates every request as WendApiFactory.CurrentUser.UserId, or as nobody when that is
/// null. This exists so the API tests can keep saying "act as user B" in one line while real
/// authorization runs in front of the endpoints — and, unlike the ICurrentUser override it
/// replaces, it makes those tests exercise the real HttpContextCurrentUser and a real principal.
///
/// It lives in Wend.Tests and must never move to Wend.Api: a scheme that authenticates whoever
/// asks is an auth bypass the moment it is reachable from the shipping project.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TestCurrentUser currentUser)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (currentUser.UserId is not { Length: > 0 } id)
            return Task.FromResult(AuthenticateResult.NoResult());

        // NameIdentifier because that is what Identity issues and what HttpContextCurrentUser
        // reads. Any other claim type here would make the tests green against a seam the app
        // does not actually use.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
