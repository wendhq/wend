using Wend.Core;

namespace Wend.Api;

/// <summary>
/// Development email sender: appends the link to a log file and echoes it to the console, so the
/// whole register-and-verify flow can be built and walked with no provider account and no real
/// send. Deliberately the only implementation in this plan.
/// </summary>
public sealed class FileAuthEmailSender(string path) : IAuthEmailSender
{
    public async Task SendEmailConfirmationAsync(string email, string link)
    {
        var entry = $"[{DateTime.UtcNow:u}] confirm {email}{Environment.NewLine}  {link}{Environment.NewLine}";
        await File.AppendAllTextAsync(path, entry);
        Console.WriteLine(entry);
    }
}
