import { api } from "../../api.js";

// State only. Two sub-states that never touch each other: a failed password change must not
// disturb whatever the email form is showing, and vice versa. `what` tells the controller which
// half changed, so it can repaint and focus that half alone.
export function createAccountModel() {
  let state = {
    email: "",
    password: { status: "editing", errors: [] },
    emailChange: { status: "editing", errors: [], newEmail: "" },
  };
  const subscribers = [];
  const notify = (what) => subscribers.forEach((fn) => fn(state, what));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state, "all");
    },

    // Called once, at mount. The address comes from the server rather than from anything cached,
    // so a screen opened after a change in another tab still shows the truth.
    async load() {
      const me = await api("/api/auth/me");
      state = { ...state, email: me?.email ?? "" };
      notify("all");
    },

    async changePassword({ currentPassword, newPassword }) {
      if (state.password.status === "sending") return;
      state = { ...state, password: { status: "sending", errors: [] } };
      notify("password");
      try {
        await api("/api/auth/change-password", {
          method: "POST",
          body: JSON.stringify({ currentPassword, newPassword }),
        });
        state = { ...state, password: { status: "done", errors: [] } };
      } catch (error) {
        state = { ...state, password: { status: "editing", errors: [passwordError(error)] } };
      }
      notify("password");
    },

    async changeEmail({ newEmail }) {
      if (state.emailChange.status === "sending") return;
      state = { ...state, emailChange: { status: "sending", errors: [], newEmail } };
      notify("email");
      try {
        await api("/api/auth/change-email", {
          method: "POST",
          body: JSON.stringify({ newEmail }),
        });
        // 204 for a free address and for one somebody else holds. The screen must not claim a link
        // went to THIS address, because it has no idea. Same discipline as the forgot screen.
        state = { ...state, emailChange: { status: "sent", errors: [], newEmail } };
      } catch (error) {
        state = {
          ...state,
          emailChange: { status: "editing", errors: [emailError(error)], newEmail },
        };
      }
      notify("email");
    },
  };
}

function passwordError(error) {
  const reason = error?.status === 400 ? error?.body?.error : null;
  if (reason === "password") return "That password is too short. Use at least 12 characters.";
  if (reason === "current") return "That isn't your current password.";
  // 401 is either a session that ended or an account locked by five wrong attempts, and the
  // endpoint deliberately does not say which. One message that is true of both, and no bounce to
  // the login screen: a locked-out user's cookie still works, so signing them out would be a lie
  // that also loses whatever they had typed. The message names BOTH ways out. Lockout advice alone
  // sends somebody whose session died (a password change on another device) round a
  // wait-and-retry loop that can never succeed.
  if (error?.status === 401) {
    return "We can't change your password right now. If you've had five wrong tries, wait fifteen "
      + "minutes and try again. Otherwise, sign out and sign in again.";
  }
  return "Something went wrong. Please try again.";
}

function emailError(error) {
  const reason = error?.status === 400 ? error?.body?.error : null;
  if (reason === "same") return "That's already your sign-in address.";
  // The bare 400 also covers an address the browser accepts but Wend cannot hold: o'brien@ passes
  // type="email", then fails the AllowedUserNameCharacters check /change-email runs. "That doesn't
  // look like an email address" would be false for them, so the message names the rule instead.
  if (error?.status === 400) {
    return "We can't use that address. Check it for typos. Wend only accepts the letters a to z, "
      + "digits and . _ - + @ in an address.";
  }
  if (error?.status === 401) {
    return "We can't change your address right now. Sign out and sign in again.";
  }
  return "Something went wrong. Please try again.";
}
