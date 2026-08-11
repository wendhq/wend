import { api } from "../../api.js";

// State only: what was submitted, what came back. No DOM, no timers.
export function createForgotModel() {
  let state = { status: "editing", errors: [], email: "" };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    async submit({ email }) {
      state = { status: "sending", errors: [], email };
      notify();
      try {
        await api("/api/auth/forgot-password", {
          method: "POST",
          body: JSON.stringify({ email }),
        });
        // 204 for an address the server knows and one it has never seen. The screen must not claim
        // a link went to THIS address, because it has no idea.
        state = { status: "sent", errors: [], email };
      } catch {
        // The endpoint has no 400 branch, so anything that lands here is a transport failure.
        state = { status: "editing", errors: ["Something went wrong. Please try again."], email };
      }
      notify();
    },
  };
}
