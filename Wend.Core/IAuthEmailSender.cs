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
}
