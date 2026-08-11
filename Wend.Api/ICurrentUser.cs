namespace Wend.Api;

/// <summary>
/// The signed-in user for this request, or null when nobody is signed in. Lives in Wend.Api
/// because "current" is an HTTP concept — the domain takes an owner id as an ordinary argument.
/// Implemented by HttpContextCurrentUser (Plan 4); tests reach the same seam through the request
/// principal their test scheme issues, not by replacing this service.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
}
