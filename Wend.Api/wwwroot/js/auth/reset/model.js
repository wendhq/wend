import { api } from "../../api.js";

// State only. The token pair is NOT held here — the controller owns it and passes it on each
// submit, so it can never reach the view and never reach the DOM.
export function createResetModel() {
  let state = { status: "editing", errors: [] };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    // Arrived with nothing to redeem — a reload, a bookmark, or a back-navigation after
    // replaceState stripped the query string.
    noLink() {
      state = { status: "nolink", errors: [] };
      notify();
    },
    async submit({ userId, code, password }) {
      // A second submit after a successful one would send a token the first submit has already
      // killed, and the screen would announce "this link has expired" to somebody whose password
      // had just been changed. The view's in-flight disable loses this race when both clicks land
      // before the first response.
      if (state.status === "sending" || state.status === "done") return;

      state = { status: "sending", errors: [] };
      notify();
      try {
        await api("/api/auth/reset-password", {
          method: "POST",
          body: JSON.stringify({ userId, code, password }),
        });
        state = { status: "done", errors: [] };
      } catch (error) {
        const reason = error?.status === 400 ? error?.body?.error : null;
        if (reason === "token") {
          state = { status: "expired", errors: [] };
        } else if (reason === "password") {
          state = {
            status: "editing",
            errors: ["That password is too short. Use at least 12 characters."],
          };
        } else {
          state = { status: "editing", errors: ["Something went wrong. Please try again."] };
        }
      }
      notify();
    },
  };
}
