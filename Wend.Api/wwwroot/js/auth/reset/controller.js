// Wires the reset screen. Owns userId and code for the lifetime of the screen and merges them into
// each submit — the view never sees them.
export function createResetController(model, view, announce, { userId, code, onDone } = {}) {
  let seenFirstRender = false;

  view.bindActions({
    submit: ({ password }) => model.submit({ userId, code, password }),
  });

  // Settle the no-link case BEFORE subscribing, so that arrival renders and announces once instead
  // of showing a form to somebody with nothing to submit. Mirrors the verify screen.
  if (!userId || !code) model.noLink();

  model.subscribe((state) => {
    if (state.status === "sending") {
      view.setBusy(true);
      announce("Setting your new password…");
      return;
    }

    if (state.status === "done") {
      // The API deliberately does not sign the user in: a link that arrived by email should not
      // become a session. Hand off to login, which moves focus and announces the reason.
      onDone?.();
      return;
    }

    view.render(state);
    view.setBusy(false);

    // Unlike register and login, the FIRST render here is already a result — this screen is reached
    // by clicking a link in an email, and "nothing to reset" is an outcome the user must hear.
    if (state.status === "nolink") {
      view.focusHeading();
      announce("Nothing to reset. Open the link from your email, or request a new one.");
      seenFirstRender = true;
      return;
    }

    if (!seenFirstRender) {
      seenFirstRender = true;
      return;
    }

    if (state.status === "expired") {
      view.focusHeading();
      announce("This link has expired or was already used. Request a new one.");
    } else if (state.errors?.length) {
      view.focusFirstError();
      announce(state.errors[0]);
    }
  });
}
