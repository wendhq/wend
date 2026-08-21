import { escapeHtml } from "../../escape.js";

// Renders the new-password form and the two dead-end states. No logic; events via data-action.
//
// userId and code are NEVER passed to this view and never rendered — no hidden inputs, nothing.
// They come off the query string of an anonymous page anybody can link to, and every view here
// renders through a template literal into innerHTML, so `value="${code}"` would be reflected XSS.
// The controller holds them and merges them on submit.
export function createResetView(root) {
  let h = {};

  function render(state) {
    if (state.status === "nolink") {
      root.innerHTML = `
        <div class="auth-view">
          <h2 class="auth-heading" tabindex="-1">Nothing to reset</h2>
          <p>Open the link from your email to set a new password. Links last one hour.</p>
          <p class="auth-links"><a href="/forgot-password">Request a new link</a>.</p>
        </div>`;
      return;
    }

    if (state.status === "expired") {
      root.innerHTML = `
        <div class="auth-view">
          <h2 class="auth-heading" tabindex="-1">This link has expired or was already used</h2>
          <p>Reset links last one hour, and each one works once.</p>
          <p class="auth-links"><a href="/forgot-password">Request a new link</a>.</p>
        </div>`;
      return;
    }

    const errors = state.errors ?? [];
    root.innerHTML = `
      <div class="auth-view">
        <h2 class="auth-heading" tabindex="-1">Set a new password</h2>
        ${errors.length ? `
        <div class="auth-errors alert alert-danger" tabindex="-1">
          <p>${escapeHtml(errors[0])}</p>
        </div>` : ""}
        <form class="auth-form" data-action="submit">
          <!-- minlength mirrors the server's policy so the browser gives native, per-field,
               accessible feedback before the request goes out. -->
          <label for="reset-password">New password</label>
          <div class="password-field">
            <input class="input" id="reset-password" name="password" type="password" autocomplete="new-password"
              minlength="12" required aria-describedby="hint-reset-password" />
            <button type="button" class="btn btn-ghost" data-action="reveal"
              aria-label="Show password" aria-controls="reset-password">Show</button>
          </div>
          <p class="field-hint" id="hint-reset-password">At least 12 characters. A memorable phrase beats a short tangle of symbols.</p>

          <button type="submit" class="btn btn-primary" data-role="submit">Set the password</button>
        </form>
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  // Presentation only, and deliberately NOT model state: whether a password is on screen has no
  // business surviving a re-render, and every re-render here follows a failed submit.
  function setPasswordVisible(visible) {
    const field = root.querySelector("#reset-password");
    const button = root.querySelector('[data-action="reveal"]');
    if (!field || !button) return;
    field.type = visible ? "text" : "password";
    button.textContent = visible ? "Hide" : "Show";
    button.setAttribute("aria-label", visible ? "Hide password" : "Show password");
  }

  // A server-side error belongs to no field, so focus goes to the summary. Per-field errors are
  // left to native validation, which focuses the offending input itself.
  function focusFirstError() {
    const summary = root.querySelector(".auth-errors");
    if (summary) summary.focus();
    else focusHeading();
  }

  function setBusy(busy) {
    const button = root.querySelector('[data-role="submit"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Setting…" : "Set the password";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="submit"]')) return;
      e.preventDefault();
      const data = new FormData(e.target);
      h.submit({ password: data.get("password") ?? "" });
    });
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-action="reveal"]')) h.reveal();
    });
  }

  return { render, focusHeading, focusFirstError, setBusy, setPasswordVisible, bindActions };
}
