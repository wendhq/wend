// Renders the four verify states. Every one is a real screen with a heading — never a raw error.
export function createVerifyView(root) {
  let h = {};

  const BODIES = {
    checking: `
      <h2 class="auth-heading" tabindex="-1">Confirming your address…</h2>
      <p>One moment.</p>`,
    confirmed: `
      <h2 class="auth-heading" tabindex="-1">Address confirmed</h2>
      <p>Your email address is confirmed. <a href="/login">Sign in</a> to start using Wend.</p>`,
    already: `
      <h2 class="auth-heading" tabindex="-1">Already confirmed</h2>
      <p>This address was confirmed already, so this link has done its job.
        <a href="/login">Sign in</a> whenever you're ready.</p>`,
    // A link with no parameters is not the same as a broken one — most often it is someone
    // reloading this page after confirming, and telling them their link expired would be a lie.
    nothing: `
      <h2 class="auth-heading" tabindex="-1">Nothing to confirm here</h2>
      <p>Open the confirmation link from your email. If you no longer have it, request a new one below.</p>`,
    expired: `
      <h2 class="auth-heading" tabindex="-1">This link has expired</h2>
      <p>Confirmation links last 24 hours and can only be used once. Request a new one below.</p>`,
    failed: `
      <h2 class="auth-heading" tabindex="-1">Something went wrong</h2>
      <p>We couldn't confirm your address just now. Request a new link below.</p>`,
    sent: `
      <h2 class="auth-heading" tabindex="-1">Check your email</h2>
      <p>If that address needs confirming, we've sent a new link. It lasts 24 hours.</p>
      <p class="auth-links"><a href="/login">Back to sign in</a>.</p>`,
  };

  // The register link is not decoration. An account left unconfirmed for a week is deleted, so the
  // person most likely to be holding a stale link is the one whose account no longer exists — and
  // for them the resend form silently does nothing, forever. They need the other door.
  //
  // .btn carries the design system's min-height: 2.75rem, which is what keeps this control at the
  // 44x44 minimum target size. A bare <button> here measures 28px high.
  const RESEND_FORM = `
    <form class="auth-form" data-action="resend">
      <label for="verify-email">Email</label>
      <input class="input" id="verify-email" name="email" type="email" autocomplete="email"
        maxlength="254" required />
      <button type="submit" class="btn btn-primary" data-role="resend">Send a new link</button>
    </form>
    <p>Link more than a week old? The account may have been removed —
      <a href="/register">create it again</a>.</p>`;

  function render(state) {
    const needsResend = state.status === "expired" || state.status === "failed"
      || state.status === "nothing";
    root.innerHTML = `
      <div class="auth-view">
        ${BODIES[state.status] ?? BODIES.failed}
        ${needsResend ? RESEND_FORM : ""}
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  function setBusy(busy) {
    const button = root.querySelector('[data-role="resend"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Sending…" : "Send a new link";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="resend"]')) return;
      e.preventDefault();
      h.resend(new FormData(e.target).get("email") ?? "");
    });
  }

  return { render, focusHeading, setBusy, bindActions };
}
