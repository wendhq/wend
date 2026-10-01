import { escapeHtml } from "../../escape.js";

// Renders the five states. Every one is a real screen with a heading — never a raw error.
//
// This view is never given userId, newEmail or code, and must never be: they come off the query
// string of an anonymous page anybody can link to, and everything here goes through a template
// literal into innerHTML. The success heading interpolates the address the SERVER returned, which
// is a stored value, and escapes it anyway.
export function createConfirmEmailView(root) {
  const BODIES = {
    checking: `
      <h2 class="auth-heading" tabindex="-1">Confirming your new address…</h2>
      <p>One moment.</p>`,
    // A link with no parameters is not a broken one — most often it is a reload after confirming.
    nolink: `
      <h2 class="auth-heading" tabindex="-1">Nothing to confirm</h2>
      <p>Open the link from your email to finish changing your address. Links last one hour.</p>
      <p class="auth-links"><a href="/login">Back to sign in</a>.</p>`,
    // A used link answers exactly like an expired one (the first use rotated the stamp), so the
    // most common way to land here is opening the link a second time after it WORKED. The copy
    // says so, or that person starts a change that is already done.
    expired: `
      <h2 class="auth-heading" tabindex="-1">This link has expired or was already used</h2>
      <p>Links last one hour and each one works once. If you've opened this link before, your
        address may already have changed: try signing in with the new one.</p>
      <p>Otherwise, sign in and start the change again from Settings → Account.</p>
      <p class="auth-links"><a href="/login">Sign in</a>.</p>`,
    taken: `
      <h2 class="auth-heading" tabindex="-1">That address is now in use</h2>
      <p>Somebody claimed it after you asked for this link. Sign in and try a different address from
        Settings → Account.</p>
      <p class="auth-links"><a href="/login">Sign in</a>.</p>`,
    failed: `
      <h2 class="auth-heading" tabindex="-1">Something went wrong</h2>
      <p>We couldn't change your address just now. Open the link from your email again; it works
        for one hour. If it has run out, sign in and start again from Settings → Account.</p>
      <p class="auth-links"><a href="/login">Sign in</a>.</p>`,
  };

  function render(state) {
    const body = state.status === "done"
      ? `
        <h2 class="auth-heading address-wrap" tabindex="-1">Your sign-in address is now ${escapeHtml(state.email ?? "")}</h2>
        <p>Use it the next time you sign in. Every device you were signed in on has been signed
          out.</p>
        <p class="auth-links"><a href="/login">Sign in</a>.</p>`
      : BODIES[state.status] ?? BODIES.failed;

    root.innerHTML = `<div class="auth-view">${body}</div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  return { render, focusHeading };
}
