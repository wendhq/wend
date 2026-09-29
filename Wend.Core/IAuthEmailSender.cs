namespace Wend.Core;

/// <summary>
/// Outbound authentication email. Named IAuthEmailSender, not IEmailSender, because
/// Microsoft.AspNetCore.Identity already defines an IEmailSender and AuthEndpoints imports both
/// that namespace and this one — an unqualified name there would not compile.
///
/// The only implementation writes to a local file (dev). A transactional provider arrives with
/// deployment, where the provider is a GDPR data processor and needs a DPA before it sees a real
/// address.
/// </summary>
public interface IAuthEmailSender
{
    Task SendEmailConfirmationAsync(string email, string link);

    Task SendPasswordResetAsync(string email, string link);

    Task SendEmailChangeConfirmationAsync(string newEmail, string link);

    /// <summary>
    /// Tells the OLD address that the account's sign-in address was changed. Takes both addresses
    /// because a notice that does not name what the address was changed to tells the owner
    /// something happened without telling them enough to act on it.
    /// </summary>
    Task SendEmailChangedNoticeAsync(string oldEmail, string newEmail);
}
