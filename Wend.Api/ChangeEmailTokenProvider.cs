using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wend.Api;

/// <summary>
/// Change-email tokens with their own lifespan. The third instance of the pattern
/// EmailConfirmationTokenProvider and PasswordResetTokenProvider exist to enforce: without one
/// provider per token type, the global DataProtectionTokenProviderOptions governs all of them, and
/// the hour this one wants would silently become the lifespan of every confirmation link.
/// </summary>
public class ChangeEmailTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<ChangeEmailTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;

public class ChangeEmailTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public ChangeEmailTokenProviderOptions()
    {
        Name = "WendChangeEmailTokenProvider";
        // One hour, matching reset rather than confirmation's 24: the user typed the address
        // seconds ago and has one mailbox to reach. The link also repoints an account's login
        // identity, which is worth as much to an attacker as a reset link.
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
