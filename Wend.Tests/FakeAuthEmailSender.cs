using Wend.Core;

namespace Wend.Tests;

/// <summary>Captures what would have been emailed, so tests can assert on links and on silence.</summary>
public sealed class FakeAuthEmailSender : IAuthEmailSender
{
    public List<(string Email, string Link)> Sent { get; } = [];

    public Task SendEmailConfirmationAsync(string email, string link)
    {
        Sent.Add((email, link));
        return Task.CompletedTask;
    }
}
