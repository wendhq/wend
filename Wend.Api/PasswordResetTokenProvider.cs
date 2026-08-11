using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wend.Api;

/// <summary>
/// Password-reset tokens with their own lifespan. The mirror image of
/// EmailConfirmationTokenProvider, and the reason that class exists: without one provider per token
/// type, the global DataProtectionTokenProviderOptions governs both, and the hour a reset wants
/// would silently become the lifespan of every confirmation link too.
/// </summary>
public class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;

public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        Name = "WendPasswordResetTokenProvider";
        // One hour. A reset link is the most powerful string Wend sends by email, and the screen
        // that greets an expired one offers a replacement in a click.
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
