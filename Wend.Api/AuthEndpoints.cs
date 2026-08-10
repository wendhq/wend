using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Wend.Core;

namespace Wend.Api;

public static class AuthEndpoints
{
    // RFC 5321's maximum path length — an outer bound before anything else looks at the value.
    private const int MaxEmailLength = 254;

    /// <param name="publicBaseUrl">
    /// Origin to build emailed links from, e.g. "https://wend.example". Null only in Development,
    /// where the request's host is used instead. See the Host-header note on SendConfirmationAsync.
    /// </param>
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group, string? publicBaseUrl)
    {
        // Anonymous by design — nobody is signed in while registering. These routes carry no
        // ICurrentUser guard, unlike every board route.
        group.MapPost("/register", async (RegisterRequest req, UserManager<WendUser> users,
            IAuthEmailSender email, HttpRequest http, ILoggerFactory loggerFactory) =>
        {
            var address = req.Email?.Trim() ?? "";
            var displayName = DisplayNames.Clean(req.DisplayName);
            var password = req.Password ?? "";

            // ─── Everything that depends ONLY on the caller's own input is checked first. ───
            // Refusing malformed input is safe: it says nothing about who is registered. But it
            // must happen BEFORE the existence lookup, or the 400 becomes an oracle — a caller
            // sending a deliberately weak password would get 400 for a free address (the policy
            // ran) and 204 for a taken one (it did not). One request per address, whole user
            // table enumerated. AuthRegisterTests guards this ordering; do not reorder.
            if (address.Length is 0 or > MaxEmailLength) return Results.BadRequest();
            if (!new EmailAddressAttribute().IsValid(address)) return Results.BadRequest();
            if (displayName.Length is 0 or > DisplayNames.MaxLength) return Results.BadRequest();

            var candidate = new WendUser { UserName = address, Email = address, DisplayName = displayName };
            foreach (var validator in users.PasswordValidators)
            {
                if (!(await validator.ValidateAsync(users, candidate, password)).Succeeded)
                    return Results.BadRequest();
            }

            // ─── From here on, every outcome answers 204. ───
            if (await users.FindByEmailAsync(address) is { } existing)
            {
                // An unconfirmed account gets a fresh link — that is also how a real owner
                // reclaims an address a bot registered first. A confirmed one is left alone.
                // Both answer exactly as a brand-new registration does.
                if (!existing.EmailConfirmed)
                    await SendConfirmationAsync(existing, users, email, http, publicBaseUrl);
                return Results.NoContent();
            }

            var result = await users.CreateAsync(candidate, password);
            if (!result.Succeeded)
            {
                // Past the validation above, the only failures left are a uniqueness race and
                // Identity's AllowedUserNameCharacters rule — and answering either with a 400
                // would leak existence. So: same 204, and a log line (error CODES only, never the
                // address) so a legitimately-unusable address is still diagnosable from the server.
                loggerFactory.CreateLogger("Wend.Api.AuthEndpoints")
                    .LogWarning("Registration rejected after validation: {Errors}",
                        string.Join("; ", result.Errors.Select(e => e.Code)));
                return Results.NoContent();
            }

            await SendConfirmationAsync(candidate, users, email, http, publicBaseUrl);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// Mints a confirmation token and emails a link to the SPA's /verify screen. The token is
    /// Base64Url-encoded because Identity's raw token is not URL-safe.
    ///
    /// The origin comes from configuration, NOT from the request. Building it from http.Host would
    /// mean an attacker who can set the Host header gets Wend to email a victim a genuine-looking
    /// link pointing at the attacker's server — handing over a live confirmation token. Development
    /// falls back to the request host because there is no configured origin on localhost.
    /// </summary>
    private static async Task SendConfirmationAsync(WendUser user, UserManager<WendUser> users,
        IAuthEmailSender email, HttpRequest http, string? publicBaseUrl)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var origin = publicBaseUrl?.TrimEnd('/') ?? $"{http.Scheme}://{http.Host}";
        var link = $"{origin}/verify" +
                   $"?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(code)}";
        await email.SendEmailConfirmationAsync(user.Email!, link);
    }
}

public record RegisterRequest(string Email, string Password, string DisplayName);
