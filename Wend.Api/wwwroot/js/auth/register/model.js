import { api } from "../../api.js";
import { capitaliseFirst } from "../../text.js";

// State only: what was submitted, what came back. No DOM, no timers.
export function createRegisterModel() {
  let state = { status: "editing", errors: [] };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    async submit({ email, password, displayName }) {
      state = { status: "sending", errors: [] };
      notify();
      try {
        // The display name is content and gets the same treatment as a board title. The address
        // and the password are NOT touched: one is a credential, the other is matched literally.
        await api("/api/auth/register", {
          method: "POST",
          body: JSON.stringify({ email, password, displayName: capitaliseFirst(displayName) }),
        });
        // 204 for a new account AND for one that already exists — the server refuses to say
        // which, so the screen must not claim "account created" either.
        state = { status: "sent", errors: [], email };
      } catch (error) {
        state = {
          status: "editing",
          errors: error?.status === 400
            ? ["Check the form: we need a valid email address, a display name, and a password of at least 12 characters."]
            : ["Something went wrong. Please try again."],
        };
      }
      notify();
    },
  };
}
