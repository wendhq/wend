using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wend.Api;

/// <summary>
/// Confirmation tokens with their own lifespan, independent of every other Identity token.
/// Without this the global DataProtectionTokenProviderOptions governs all of them, so Plan 5
/// shortening the default to the ~1 hour a password reset wants would silently shorten email
/// confirmation to an hour too.
/// </summary>
public class EmailConfirmationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;

public class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailConfirmationTokenProviderOptions()
    {
        Name = "WendEmailConfirmationTokenProvider";
        // Long enough to survive "I'll do it in the morning", short enough that a leaked link in a
        // forwarded mailbox goes stale quickly.
        TokenLifespan = TimeSpan.FromHours(24);
    }
}
