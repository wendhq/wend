namespace Wend.Api;

/// <summary>
/// The signed-in user for this request, or null when nobody is signed in. Lives in Wend.Api
/// because "current" is an HTTP concept — the domain takes an owner id as an ordinary argument.
/// Plan 3 replaces NullCurrentUser with an HttpContext-backed implementation; nothing else changes.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
}

/// <summary>No authentication exists yet, so nobody is ever signed in and every /api/* call is 401.</summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public string? UserId => null;
}
