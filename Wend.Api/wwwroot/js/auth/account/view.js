import { escapeHtml } from "../../escape.js";

// Renders the Account screen. The shell is built once; each form's body is replaced on its own.
// That is what makes the two-form rules structural: a password submit physically cannot clear the
// email form's error, steal its focus, or wipe what was typed into it.
//
// Both forms reuse .auth-form and .field-hint. This is the same form shape the auth screens use,
// and a second copy of that CSS would be a second source of truth for the same spacing.
export function createAccountView(root) {
  let h = {};

  function render(state) {
    root.innerHTML = `
      <div class="account-view">
        <button class="back-link btn btn-ghost" data-action="back">← Settings</button>
        <h2 class="account-heading" tabindex="-1">Account</h2>
        ${state.email ? `
        <p class="account-address address-wrap">Signed in as <strong>${escapeHtml(state.email)}</strong></p>` : ""}

        <section class="account-section" aria-labelledby="account-password-heading">
          <h3 class="account-section-heading" id="account-password-heading" tabindex="-1">Change password</h3>
          <div class="account-password-body"></div>
        </section>

        <section class="account-section" aria-labelledby="account-email-heading">
          <h3 class="account-section-heading" id="account-email-heading" tabindex="-1">Change sign-in address</h3>
          <div class="account-email-body"></div>
        </section>
      </div>`;
    renderPassword(state.password, state.email);
    renderEmail(state.emailChange);
  }

  // The password form is built on a whole-shell render and on success, and is otherwise left
  // alone: a failed change repaints only the status region above it. Two reasons, both
  // load-bearing. A browser's password manager reads "the form vanished after a fetch" as a
  // successful change, so rebuilding the form on a failure gets the REJECTED new password offered
  // for saving, and a generated one saved without asking. And the user keeps what they typed, so a
  // mistyped current password costs one field, not two. Success does replace the form: that is
  // what clears both fields, and it is the signal the password manager should get.
  // Because the inputs now outlive the error, their aria-describedby points at a FIXED error id
  // in the markup (account-password-errors). A reference to an id that is not on the page is
  // ignored until the error renders.
  function renderPassword(password, email) {
    const body = root.querySelector(".account-password-body");
    if (!body) return;
    const status = body.querySelector(".account-password-status");
    if (status && password.status !== "done") {
      status.innerHTML = passwordStatus(password);
      return;
    }
    body.innerHTML = `
      <div class="account-password-status">${passwordStatus(password)}</div>
      <form class="auth-form" data-action="change-password">
        <!-- Tells a password manager WHICH saved login this change belongs to. Hidden from
             everyone and never sent: the submit handler reads only the two password fields. -->
        <input type="email" name="username" autocomplete="username"
          value="${escapeHtml(email ?? "")}" hidden readonly />

        <label for="account-current-password">Current password</label>
        <input class="input" id="account-current-password" name="currentPassword" type="password"
          autocomplete="current-password" required
          aria-describedby="account-password-errors" />

        <label for="account-new-password">New password</label>
        <!-- minlength mirrors the server's policy so the browser gives native, per-field,
             accessible feedback before the request goes out. -->
        <input class="input" id="account-new-password" name="newPassword" type="password"
          autocomplete="new-password" minlength="12" required
          aria-describedby="hint-account-new-password account-password-errors" />
        <p class="field-hint" id="hint-account-new-password">At least 12 characters. A memorable phrase beats a short tangle of symbols.</p>

        <!-- .btn carries the design system's min-height: 2.75rem. A bare <button> is 28px. -->
        <button type="submit" class="btn btn-primary" data-role="change-password">Change password</button>
      </form>`;
  }

  function passwordStatus(password) {
    const errors = password.errors ?? [];
    return `
      ${password.status === "done" ? `
      <div class="account-password-done alert alert-success">
        <p>Your password has been changed. Your other devices have been signed out.</p>
      </div>` : ""}
      ${errors.length ? `
      <div class="account-password-errors alert alert-danger" id="account-password-errors" tabindex="-1">
        <p>${escapeHtml(errors[0])}</p>
      </div>` : ""}`;
  }

  function renderEmail(emailChange) {
    const body = root.querySelector(".account-email-body");
    if (!body) return;
    const errors = emailChange.errors ?? [];
    body.innerHTML = `
      ${emailChange.status === "sent" ? `
      <div class="account-email-sent alert alert-success">
        <p>If that address is free, we've sent it a confirmation link. It lasts one hour, and your
          sign-in address doesn't change until you open it.</p>
      </div>` : ""}
      ${errors.length ? `
      <div class="account-email-errors alert alert-danger" id="account-email-errors" tabindex="-1">
        <p>${escapeHtml(errors[0])}</p>
      </div>` : ""}
      <form class="auth-form" data-action="change-email">
        <label for="account-new-email">New email</label>
        <input class="input" id="account-new-email" name="newEmail" type="email"
          autocomplete="email" maxlength="254" required
          value="${escapeHtml(emailChange.newEmail ?? "")}"
          aria-describedby="hint-account-new-email account-email-errors" />
        <p class="field-hint" id="hint-account-new-email">You'll sign in with this address once you've opened the link we send there.</p>

        <button type="submit" class="btn btn-primary" data-role="change-email">Send the link</button>
      </form>`;
  }

  // Written long-hand throughout: `el?.focus() ?? fallback()` looks equivalent and is not.
  // focus() returns undefined, so the fallback would fire every time and drag focus off the thing
  // it had just landed on.
  function focusHeading() { root.querySelector(".account-heading")?.focus(); }

  // After a SUCCESS, focus goes to the form's own heading, not to the success alert. The alert is
  // announced through the live region, and the heading keeps focus inside the submitting section
  // at a stable place. After a failure it goes to the error alert, which is the thing to read next.
  function focusPasswordOutcome() {
    if (root.querySelector(".account-password-done")) {
      root.querySelector("#account-password-heading")?.focus();
      return;
    }
    const error = root.querySelector(".account-password-errors");
    if (error) { error.focus(); return; }
    // Still inside the submitting form's section: never <body>, and never the other form.
    root.querySelector("#account-password-heading")?.focus();
  }

  function focusEmailOutcome() {
    if (root.querySelector(".account-email-sent")) {
      root.querySelector("#account-email-heading")?.focus();
      return;
    }
    const error = root.querySelector(".account-email-errors");
    if (error) { error.focus(); return; }
    root.querySelector("#account-email-heading")?.focus();
  }

  function setPasswordBusy(busy) {
    const button = root.querySelector('[data-role="change-password"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Changing…" : "Change password";
  }

  function setEmailBusy(busy) {
    const button = root.querySelector('[data-role="change-email"]');
    if (!button) return;
    button.disabled = busy;
    button.textContent = busy ? "Sending…" : "Send the link";
  }

  // Delegated on root, so it survives either section's body being replaced.
  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-action="back"]')) h.back();
    });
    root.addEventListener("submit", (e) => {
      const form = e.target.closest("form[data-action]");
      if (!form) return;
      e.preventDefault();
      const data = new FormData(form);
      if (form.dataset.action === "change-password") {
        h.changePassword({
          currentPassword: data.get("currentPassword") ?? "",
          newPassword: data.get("newPassword") ?? "",
        });
      } else if (form.dataset.action === "change-email") {
        h.changeEmail({ newEmail: data.get("newEmail") ?? "" });
      }
    });
  }

  return {
    render, renderPassword, renderEmail, focusHeading,
    focusPasswordOutcome, focusEmailOutcome, setPasswordBusy, setEmailBusy, bindActions,
  };
}
