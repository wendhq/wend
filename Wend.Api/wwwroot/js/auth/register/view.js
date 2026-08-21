import { escapeHtml } from "../../escape.js";

// Renders the registration form and its confirmation state. No logic; events via data-action.
export function createRegisterView(root) {
  let h = {};

  function render(state) {
    if (state.status === "sent") {
      root.innerHTML = `
        <div class="auth-view">
          <h2 class="auth-heading" tabindex="-1">Check your email</h2>
          <p>If <strong>${escapeHtml(state.email ?? "")}</strong> can be registered, we've sent it a link to confirm the address. The link lasts 24 hours.</p>
          <p>Nothing arrived? Check spam, then <button type="button" class="btn btn-ghost" data-action="resend">send it again</button>.</p>
          <p class="auth-links"><a href="/login">Back to sign in</a>.</p>
        </div>`;
      return;
    }

    const errors = state.errors ?? [];
    root.innerHTML = `
      <div class="auth-view">
        <h2 class="auth-heading" tabindex="-1">Create your Wend account</h2>
        ${errors.length ? `
        <div class="auth-errors alert alert-danger" tabindex="-1">
          <p>We couldn't create your account:</p>
          <ul>${errors.map((e) => `<li>${escapeHtml(e)}</li>`).join("")}</ul>
        </div>` : ""}
        <form class="auth-form" data-action="submit">
          <label for="reg-name">Display name</label>
          <input class="input" id="reg-name" name="displayName" type="text" autocomplete="nickname"
            maxlength="100" required aria-describedby="hint-reg-name" />
          <p class="field-hint" id="hint-reg-name">What other people will see. You can change it later.</p>

          <label for="reg-email">Email</label>
          <input class="input" id="reg-email" name="email" type="email" autocomplete="email"
            maxlength="254" required aria-describedby="hint-reg-email" />
          <p class="field-hint" id="hint-reg-email">You'll sign in with this, and we'll send a confirmation link to it.</p>

          <!-- minlength mirrors the server's policy so the browser gives native, per-field,
               accessible feedback. The server's 400 is a lumped message with no field attribution,
               which is the one accessibility commitment this screen would otherwise miss. -->
          <label for="reg-password">Password</label>
          <div class="password-field">
            <input class="input" id="reg-password" name="password" type="password" autocomplete="new-password"
              minlength="12" required aria-describedby="hint-reg-password" />
            <button type="button" class="btn btn-ghost" data-action="reveal"
              aria-label="Show password" aria-controls="reg-password">Show</button>
          </div>
          <p class="field-hint" id="hint-reg-password">At least 12 characters. A memorable phrase beats a short tangle of symbols.</p>

          <!-- .btn carries the design system's min-height: 2.75rem, which is what keeps this
               control at the 44x44 minimum target size. A bare <button> here measures 28px high. -->
          <button type="submit" class="btn btn-primary" data-role="submit">Create account</button>
        </form>
        <p class="auth-links">Already have an account? <a href="/login">Sign in</a>.</p>
      </div>`;
  }

  function focusHeading() { root.querySelector(".auth-heading")?.focus(); }

  // Presentation only, and deliberately NOT model state: whether a password is on screen has no
  // business surviving a re-render, and every re-render here follows a failed submit.
  function setPasswordVisible(visible) {
    const field = root.querySelector("#reg-password");
    const button = root.querySelector('[data-action="reveal"]');
    if (!field || !button) return;
    field.type = visible ? "text" : "password";
    button.textContent = visible ? "Hide" : "Show";
    button.setAttribute("aria-label", visible ? "Hide password" : "Show password");
  }

  // After a failed submit, focus lands on the error summary — not back at the top of the form,
  // and never on <body>. Note the summary is NOT role="alert": focus moves to it and the
  // controller announces it through #status, and doing all three makes most screen readers read
  // the same message twice.
  function focusFirstError() {
    const summary = root.querySelector(".auth-errors");
    if (summary) summary.focus();
    else focusHeading();
  }

  // Disabled while a request is in flight, so a double-clicked button can't send two
  // confirmation emails.
  function setBusy(busy) {
    const button = root.querySelector('[data-role="submit"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Creating account…" : "Create account";
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("submit", (e) => {
      if (!e.target.closest('form[data-action="submit"]')) return;
      e.preventDefault();
      const data = new FormData(e.target);
      h.submit({
        displayName: data.get("displayName") ?? "",
        email: data.get("email") ?? "",
        password: data.get("password") ?? "",
      });
    });
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-action="resend"]')) h.resend();
      if (e.target.closest('[data-action="reveal"]')) h.reveal();
    });
  }

  return { render, focusHeading, focusFirstError, setBusy, setPasswordVisible, bindActions };
}
