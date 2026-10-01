import { api } from "../../api.js";

// Maps the endpoint's three status codes onto the states the screen renders. The token trio is NOT
// held here. The controller owns it and passes it on the one submit, so it can never reach the
// view and never reach the DOM.
export function createConfirmEmailModel() {
  let state = { status: "checking" };
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(state));

  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(state);
    },
    // Arrived with nothing to confirm: a reload, a bookmark, or a back-navigation after
    // replaceState stripped the query string. Deliberately NOT routed through confirm(), which
    // would post empty values, collect a 400, and tell the user their link expired when they never
    // presented one.
    noLink() {
      state = { status: "nolink" };
      notify();
    },
    async confirm({ userId, newEmail, code }) {
      try {
        const body = await api("/api/auth/confirm-email-change", {
          method: "POST",
          body: JSON.stringify({ userId, newEmail, code }),
        });
        // The address comes from the RESPONSE, never from the query string the controller is
        // holding: the response value is what the database actually stores, and the query-string
        // value is a caller-controlled string on a page anybody can link to.
        state = { status: "done", email: body?.email ?? "" };
      } catch (error) {
        if (error?.status === 409) state = { status: "taken" };
        else if (error?.status === 400) state = { status: "expired" };
        else state = { status: "failed" };
      }
      notify();
    },
  };
}
