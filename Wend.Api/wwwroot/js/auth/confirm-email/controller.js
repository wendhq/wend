const ANNOUNCEMENTS = {
  checking: "Confirming your new address.",
  nolink: "Nothing to confirm. Open the link from your email.",
  expired: "This link has expired or was already used. If you've opened it before, your address "
    + "may already have changed.",
  taken: "That address is now in use. Try a different one from Account settings.",
  failed: "We couldn't change your address. Open the link from your email again.",
};

// Wires the confirm-email-change screen. Owns userId, newEmail and code for the lifetime of the
// screen and passes them to the one request — the view never sees them.
export function createConfirmEmailController(model, view, announce,
  { userId, newEmail, code } = {}) {
  // Settle the no-link case BEFORE subscribing, so arrival renders and announces once instead of
  // flashing "Confirming…" at somebody who presented nothing. Mirrors the verify screen.
  if (!userId || !newEmail || !code) model.noLink();

  model.subscribe((state) => {
    view.render(state);
    // EVERY state moves focus to its heading and says what happened — including "checking". This
    // screen is reached by clicking a link in an email specifically to receive an async result, so
    // the house "first paint does not force focus" rule is wrong here: without this a
    // screen-reader user gets silence, with focus nowhere, until the request settles.
    view.focusHeading();
    announce(state.status === "done"
      ? `Your sign-in address is now ${state.email}. Please sign in.`
      : ANNOUNCEMENTS[state.status] ?? ANNOUNCEMENTS.failed);
  });

  // POSTs on mount, with no button to press. The endpoint is a POST precisely so a mail scanner
  // following the emailed link cannot complete the change, and this shell-plus-JS shape is what
  // makes that true. Same as /verify.
  if (userId && newEmail && code) model.confirm({ userId, newEmail, code });
}
