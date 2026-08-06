using Wend.Api;

namespace Wend.Tests;

/// <summary>
/// Mutable ICurrentUser for tests: set UserId to act as that user, or null to act anonymously.
/// This is how one test proves the ownership boundary — act as A, switch to B, assert 404.
/// </summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public string? UserId { get; set; }
}
