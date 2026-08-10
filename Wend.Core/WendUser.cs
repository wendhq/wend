using Microsoft.AspNetCore.Identity;

namespace Wend.Core;

/// <summary>
/// A Wend account. Email is the login credential (Identity's own field); DisplayName is the
/// human-facing name. Slice 2b will render DisplayName on other users' boards, so it is treated
/// as untrusted user content everywhere it is written or displayed.
/// </summary>
public class WendUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// When the account was created (UTC). IdentityUser has no such field, and the unverified-account
    /// purge needs one to know what is stale.
    ///
    /// The initializer is load-bearing: Npgsql maps DateTime to 'timestamp with time zone' and throws
    /// on a Kind=Unspecified value, which is exactly what default(DateTime) is. Test helpers build
    /// WendUser by object initializer and never set this, so without the default they would all fail.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
