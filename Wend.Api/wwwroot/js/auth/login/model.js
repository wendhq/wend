import { api } from "../../api.js";

// State only: what was submitted, what came back, how many times in a row it failed. No DOM,
// no timers. The failure count lives here rather than in the controller because it is state.
export function createLoginModel() {
  let state = { status: "editing", errors: [], failures: 0, rememberMe: false };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    async submit({ email, password, rememberMe }) {
      const failures = state.failures;
      state = { status: "sending", errors: [], failures, rememberMe };
      notify();
      try {
        await api("/api/auth/login", {
          method: "POST",
          body: JSON.stringify({ email, password, rememberMe }),
        });
        state = { status: "signedIn", errors: [], failures: 0, rememberMe };
      } catch (error) {
        // The server answers one generic 401 for a wrong password, an unknown address, an
        // unconfirmed account and a locked-out one alike, so this message must not guess which.
        // The help block after three tries is what covers the last two.
        state = {
          status: "editing",
          failures: failures + 1,
          rememberMe,
          errors: [
            error?.status === 401
              ? "That email address and password don't match an account."
              : "Something went wrong. Please try again.",
          ],
        };
      }
      notify();
    },
  };
}
