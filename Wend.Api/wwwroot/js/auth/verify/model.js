import { api } from "../../api.js";

// Maps the endpoint's three status codes onto the three states the screen renders.
export function createVerifyModel() {
  let state = { status: "checking" };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    // Arrived at /verify with no link parameters — a reload after confirming, or a hand-typed
    // URL. Deliberately NOT routed through confirm(), which would post empty values, collect a
    // 400 and tell the user their link expired when they never presented one.
    noLink() {
      state = { status: "nothing" };
      notify();
    },
    async confirm({ userId, code }) {
      try {
        await api("/api/auth/verify", {
          method: "POST",
          body: JSON.stringify({ userId, code }),
        });
        state = { status: "confirmed" };
      } catch (error) {
        if (error?.status === 409) state = { status: "already" };
        else if (error?.status === 400) state = { status: "expired" };
        else state = { status: "failed" };
      }
      notify();
    },
  };
}
