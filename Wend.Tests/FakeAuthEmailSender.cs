using Wend.Core;

namespace Wend.Tests;

/// <summary>Captures what would have been emailed, so tests can assert on links and on silence.</summary>
public sealed class FakeAuthEmailSender : IAuthEmailSender
{
    // Kind, not just the address: several Plan 5 tests turn on WHICH mail went out — a reset
    // request against an unconfirmed account must produce a confirmation link and no reset link.
    public List<(string Email, string Link, string Kind)> Sent { get; } = [];

    public Task SendEmailConfirmationAsync(string email, string link)
    {
        Sent.Add((email, link, "confirm"));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string link)
    {
        Sent.Add((email, link, "reset"));
        return Task.CompletedTask;
    }
}
