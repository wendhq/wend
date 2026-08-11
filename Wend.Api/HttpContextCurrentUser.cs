using System.Security.Claims;

namespace Wend.Api;

/// <summary>
/// The signed-in user, read from the request principal. Identity issues the user id as
/// ClaimTypes.NameIdentifier (IdentityOptions.ClaimsIdentity.UserIdClaimType's default), which is
/// also what the test scheme issues, so both paths land here identically.
///
/// The IsAuthenticated check is not redundant: a request that failed authentication still carries
/// an anonymous ClaimsPrincipal, and reading a claim off it would be reading nothing very quietly.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            return principal?.Identity?.IsAuthenticated == true
                ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;
        }
    }
}
