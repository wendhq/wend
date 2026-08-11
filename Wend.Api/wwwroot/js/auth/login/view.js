// Renders the login form, its error summary and the after-three-tries help. No logic; events
// via data-action. Nothing here interpolates user content, so there is no escapeHtml call — the
// only dynamic strings are the model's own fixed messages.
export function createLoginView(root) {
  let h = {};

  // Shown only after three consecutive failures. The server cannot tell the user they are locked
  // out or unverified without confirming the account exists, so this says all three things at
  // once, to everyone, and leaks nothing.
  const HELP = `
    <div class="auth-help" tabindex="-1">
      <p>Still not working? One of these usually explains it:</p>
      <ul>
        <li>Several wrong tries in a row pause sign-in for about fifteen minutes.</li>
        <li>A new account needs its email address confirmed first — check your inbox for the link.</li>
        <li>Forgotten the password? Password reset arrives in the next release.</li>
      </ul>
    </div>`;

  function render(state) {
    const errors = state.errors ?? [];
    root.innerHTML = `
      <div class="auth-view">
        <h2 class="auth-heading" tabindex="-1">Sign in to Wend</h2>
        ${errors.length ? `
        <div class="auth-errors alert alert-danger" tabindex="-1">
          <p>${errors[0]}</p>
        </div>` : ""}
        <form class="auth-form" data-action="submit">
          <label for="login-email">Email</label>
          <input id="login-email" name="email" type="email" autocomplete="email"
            maxlength="254" required />

          <!-- current-password, not new-password: this is what tells a password manager to offer
               the saved credential rather than generate a fresh one. -->
          <label for="login-password">Password</label>
          <input id="login-password" name="password" type="password" autocomplete="current-password"
            required />

          <!-- .btn carries the design system's min-height: 2.75rem, which is what keeps this
               control at the 44x44 minimum target size. A bare <button> here measures 28px high. -->
          <button type="submit" class="btn btn-primary" data-role="submit">Sign in</button>
        </form>
        ${(state.failures ?? 0) >= 3 ? HELP : ""}
        <p class="auth-links">No account yet? <a href="/register">Create one</a>.</p>
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  // A server-side 401 belongs to no field, so focus goes to the summary. Per-field errors are
  // left to native validation, which focuses the offending input itself — sending focus to the
  // email box on a generic failure would land a screen-reader user mid-form having heard nothing.
  function focusError() {
    const summary = root.querySelector(".auth-errors");
    if (summary) summary.focus();
    else focusHeading();
  }

  function focusHelp() { root.querySelector(".auth-help")?.focus(); }

  // Disabled while a request is in flight, so a double-clicked button cannot burn two of the five
  // lockout attempts.
  function setBusy(busy) {
    const button = root.querySelector('[data-role="submit"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Signing in…" : "Sign in";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="submit"]')) return;
      e.preventDefault();
      const data = new FormData(e.target);
      h.submit({ email: data.get("email") ?? "", password: data.get("password") ?? "" });
    });
  }

  return { render, focusHeading, focusError, focusHelp, setBusy, bindActions };
}
