import { escapeHtml } from "../../escape.js";

// Renders the forgot-password form and its one success state. No logic; events via data-action.
// The success message does NOT echo the address back as confirmation — the server will not say
// whether it has one, so neither can this screen.
export function createForgotView(root) {
  let h = {};

  function render(state) {
    const errors = state.errors ?? [];
    root.innerHTML = `
      <div class="auth-view">
        <h2 class="auth-heading" tabindex="-1">Reset your password</h2>
        ${state.status === "sent" ? `
        <div class="auth-sent alert alert-success" tabindex="-1">
          <p>If that address has an account, we've sent it a link. The link lasts one hour.</p>
        </div>` : ""}
        ${errors.length ? `
        <div class="auth-errors alert alert-danger" tabindex="-1">
          <p>${escapeHtml(errors[0])}</p>
        </div>` : ""}
        <form class="auth-form" data-action="submit">
          <label for="forgot-email">Email</label>
          <input class="input" id="forgot-email" name="email" type="email" autocomplete="email"
            maxlength="254" required value="${escapeHtml(state.email ?? "")}"
            aria-describedby="hint-forgot-email" />
          <p class="field-hint" id="hint-forgot-email">The address you signed up with.</p>

          <!-- .btn carries the design system's min-height: 2.75rem. A bare <button> is 28px. -->
          <button type="submit" class="btn btn-primary" data-role="submit">Send the link</button>
        </form>
        <p class="auth-links">Remembered it? <a href="/login">Sign in</a>.</p>
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  // Written long-hand on purpose: `el?.focus() ?? focusHeading()` looks equivalent and is not —
  // focus() returns undefined, so the fallback would fire every time and drag focus off the
  // message it had just landed on.
  function focusSent() {
    const sent = root.querySelector(".auth-sent");
    if (sent) sent.focus();
    else focusHeading();
  }

  function focusFirstError() {
    const summary = root.querySelector(".auth-errors");
    if (summary) summary.focus();
    else focusHeading();
  }

  function setBusy(busy) {
    const button = root.querySelector('[data-role="submit"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Sending…" : "Send the link";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="submit"]')) return;
      e.preventDefault();
      const data = new FormData(e.target);
      h.submit({ email: data.get("email") ?? "" });
    });
  }

  return { render, focusHeading, focusSent, focusFirstError, setBusy, bindActions };
}
