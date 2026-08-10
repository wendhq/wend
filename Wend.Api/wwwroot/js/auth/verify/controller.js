import { api } from "../../api.js";

const ANNOUNCEMENTS = {
  checking: "Confirming your address.",
  confirmed: "Your email address is confirmed.",
  already: "This address was already confirmed. There's nothing to do.",
  nothing: "Nothing to confirm. Open the link from your email, or request a new one below.",
  expired: "This link has expired. Request a new one below.",
  failed: "We couldn't confirm your address. Request a new link below.",
};

// Wires the verify screen: reads the link, confirms once, announces the outcome, offers a resend.
export function createVerifyController(model, view, announce, { userId, code } = {}) {
  view.bindActions({
    resend: async (email) => {
      view.setBusy(true);
      try {
        await api("/api/auth/resend-verification", {
          method: "POST",
          body: JSON.stringify({ email }),
        });
        // Not the expired screen with a note bolted on — a resend that worked deserves its own
        // heading, and "This link has expired" above a success message reads as a failure.
        view.render({ status: "sent" });
        view.focusHeading();
        announce("If that address needs confirming, we've sent a new link.");
      } catch {
        announce("Couldn't send a new link — please try again.");
        view.setBusy(false);
      }
    },
  });

  // Settle the no-link case BEFORE subscribing, so that arrival renders and announces once
  // instead of flashing "Confirming…" at a user who presented nothing to confirm.
  if (!userId || !code) model.noLink();

  model.subscribe((state) => {
    view.render(state);
    // EVERY state moves focus to its heading and says what happened — including "checking".
    // This screen is reached by clicking a link in an email specifically to receive an async
    // result, so the house "first paint does not force focus" rule is wrong here: without this
    // a screen-reader user gets silence, with focus nowhere, until the request settles.
    view.focusHeading();
    announce(ANNOUNCEMENTS[state.status] ?? ANNOUNCEMENTS.failed);
  });

  if (userId && code) model.confirm({ userId, code });
}
