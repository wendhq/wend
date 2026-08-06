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
}
