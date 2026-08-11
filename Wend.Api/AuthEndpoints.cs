using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Wend.Core;

namespace Wend.Api;

public static class AuthEndpoints
{
    // RFC 5321's maximum path length — an outer bound before anything else looks at the value.
    private const int MaxEmailLength = 254;

    // An unknown address must cost the same as a real one. Without this, "no such user" returns in
    // the time of a database read while a real user costs a full password hash — a timing oracle
    // that enumerates the user table however generic the response body is.
    //
    // Built from a directly-constructed hasher rather than the container's: IPasswordHasher<TUser>
    // is registered SCOPED, so a singleton holding one fails startup DI validation. The verify call
    // in the handler still uses the injected hasher; only this fixed hash is built here.
    private static readonly WendUser DummyUser = new()
    {
        UserName = "unknown@example.invalid",
        Email = "unknown@example.invalid",
    };

    private static readonly string DummyPasswordHash =
        new PasswordHasher<WendUser>().HashPassword(DummyUser, "the password of an account that does not exist");

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

        // POST, not GET, even though this arrives from an emailed link. Corporate mail scanners and
        // link-preview bots follow GET links automatically, so a GET that confirms would be fired by
        // a robot before the human ever clicked — and could confirm an address nobody opened. The
        // link therefore points at the SPA shell, which posts the code back from the browser.
        group.MapPost("/verify", async (VerifyRequest req, UserManager<WendUser> users) =>
        {
            if (req.UserId is not { Length: > 0 } id) return Results.BadRequest();
            if (await users.FindByIdAsync(id) is not { } user) return Results.BadRequest();

            // Identity's data-protector tokens are time-limited and stamp-bound, NOT single-use.
            // This check is what makes a replayed link resolve to "already verified" rather than
            // silently re-confirming — the user-visible single-use guarantee.
            if (user.EmailConfirmed) return Results.Conflict();

            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Code ?? ""));
            }
            catch (FormatException)
            {
                return Results.BadRequest();
            }

            var result = await users.ConfirmEmailAsync(user, token);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest();
        });

        // Every branch — unconfirmed, confirmed, unknown, malformed — falls out of the same 204.
        // Note there is no `return BadRequest` for a bad address: telling the caller their input
        // was malformed is fine, but it is not worth a second response shape on an endpoint whose
        // whole job is to look identical from outside.
        group.MapPost("/resend-verification", async (ResendRequest req, UserManager<WendUser> users,
            IAuthEmailSender email, HttpRequest http) =>
        {
            var address = req.Email?.Trim() ?? "";
            if (address.Length is > 0 and <= MaxEmailLength &&
                await users.FindByEmailAsync(address) is { EmailConfirmed: false } user)
            {
                await SendConfirmationAsync(user, users, email, http, publicBaseUrl);
            }

            return Results.NoContent();
        });

        // Anonymous, like the rest of this group bar /me and /logout. Every failure below answers
        // with the same empty 401 — unknown address, wrong password, unconfirmed account and
        // locked-out account alike. A response that distinguishes them enumerates the user table.
        group.MapPost("/login", async (LoginRequest req, SignInManager<WendUser> signIn,
            UserManager<WendUser> users, IPasswordHasher<WendUser> hasher, IAuthEmailSender email,
            HttpRequest http, HttpResponse response) =>
        {
            var address = req.Email?.Trim() ?? "";
            var password = req.Password ?? "";

            if (await users.FindByEmailAsync(address) is not { } user)
            {
                // Do the work anyway, then answer as everyone else does.
                hasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, password);
                return Results.Unauthorized();
            }

            var result = await signIn.PasswordSignInAsync(
                user, password, isPersistent: false, lockoutOnFailure: true);

            // isPersistent: false — the cookie dies with the browser session. Remember-me is Plan 6,
            // and until it exists an opt-in nobody asked for is not the safe default.
            if (result.Succeeded) return Results.NoContent();

            // NotAllowed is the unconfirmed case. Identity returns it from PreSignInCheck BEFORE it
            // looks at the password AND before it evaluates lockout, which has two consequences and
            // both are load-bearing:
            //
            //   * SignInManager.CheckPasswordSignInAsync runs that same PreSignInCheck, so it would
            //     answer NotAllowed forever and never verify anything. UserManager.CheckPasswordAsync
            //     is the pure password check and the only one that works here.
            //   * Lockout never increments on this path, so without the accounting below an
            //     unconfirmed account accepts unlimited password guesses.
            if (result.IsNotAllowed && !await users.IsLockedOutAsync(user))
            {
                if (await users.CheckPasswordAsync(user, password))
                {
                    await users.ResetAccessFailedCountAsync(user);

                    // Build the link now — it needs the scoped UserManager — but send it AFTER the
                    // response. Awaiting a transactional provider inline would make this branch
                    // measurably slower than every other outcome, which is the timing oracle this
                    // endpoint exists to avoid.
                    var link = await BuildConfirmationLinkAsync(user, users, http, publicBaseUrl);
                    response.OnCompleted(async () =>
                        await email.SendEmailConfirmationAsync(user.Email!, link));
                }
                else
                {
                    await users.AccessFailedAsync(user);
                }
            }

            return Results.Unauthorized();
        });

        // The SPA's boot check. Its 401 is an ordinary expected answer — the signal to mount the
        // login screen — not a failure to report to the user.
        group.MapGet("/me", async (UserManager<WendUser> users, ClaimsPrincipal principal) =>
            await users.GetUserAsync(principal) is { } user
                ? Results.Ok(new { displayName = user.DisplayName, email = user.Email })
                : Results.Unauthorized())
            .RequireAuthorization();

        group.MapPost("/logout", async (SignInManager<WendUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        return group;
    }

    /// <summary>
    /// Mints a confirmation token and builds the link to the SPA's /verify screen. The token is
    /// Base64Url-encoded because Identity's raw token is not URL-safe.
    ///
    /// The origin comes from configuration, NOT from the request. Building it from http.Host would
    /// mean an attacker who can set the Host header gets Wend to email a victim a genuine-looking
    /// link pointing at the attacker's server — handing over a live confirmation token. Development
    /// falls back to the request host because there is no configured origin on localhost.
    /// </summary>
    private static async Task<string> BuildConfirmationLinkAsync(WendUser user,
        UserManager<WendUser> users, HttpRequest http, string? publicBaseUrl)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var origin = publicBaseUrl?.TrimEnd('/') ?? $"{http.Scheme}://{http.Host}";
        return $"{origin}/verify" +
               $"?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(code)}";
    }

    /// <summary>Builds the link and sends it. Used by register and resend, which have no timing
    /// commitment to keep — register's side channel is Plan 8's, recorded in the backlog.</summary>
    private static async Task SendConfirmationAsync(WendUser user, UserManager<WendUser> users,
        IAuthEmailSender email, HttpRequest http, string? publicBaseUrl)
    {
        var link = await BuildConfirmationLinkAsync(user, users, http, publicBaseUrl);
        await email.SendEmailConfirmationAsync(user.Email!, link);
    }
}

public record RegisterRequest(string Email, string Password, string DisplayName);

public record VerifyRequest(string UserId, string Code);

public record ResendRequest(string Email);

public record LoginRequest(string Email, string Password);
