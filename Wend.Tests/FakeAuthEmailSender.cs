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

    public Task SendEmailChangeConfirmationAsync(string newEmail, string link)
    {
        Sent.Add((newEmail, link, "change-email"));
        return Task.CompletedTask;
    }

    // The notice has no link, so Link carries the NEW address here. Several tests turn on the
    // notice naming what the address was changed to, and this keeps the tuple shape every other
    // test in the suite already reads.
    public Task SendEmailChangedNoticeAsync(string oldEmail, string newEmail)
    {
        Sent.Add((oldEmail, newEmail, "changed-notice"));
        return Task.CompletedTask;
    }
}
