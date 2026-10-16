// Wires the Account screen. Each half repaints, focuses and announces on its own. The `what` the
// model passes is what keeps one form's outcome out of the other form's business.
export function createAccountController(model, view, announce, { onBack } = {}) {
  view.bindActions({
    back: () => onBack?.(),
    changePassword: (fields) => model.changePassword(fields),
    changeEmail: (fields) => model.changeEmail(fields),
  });

  model.subscribe((state, what) => {
    if (what === "all") {
      view.render(state);
      // Every whole-shell render refocuses the heading, not just the first. There are exactly two
      // (mount, then load resolving), and the second rebuilds the element the first focused, so
      // without this, focus lands on <body> a moment after arriving.
      view.focusHeading();
      return;
    }

    if (what === "password") {
      if (state.password.status === "sending") {
        view.setPasswordBusy(true);
        announce("Changing your password…");
        return;
      }
      // Success clears both password fields by rebuilding the form, which is why focus has to be
      // placed deliberately afterwards. A failure repaints just the status region and keeps the
      // form (see renderPassword).
      view.renderPassword(state.password, state.email);
      view.setPasswordBusy(false);
      view.focusPasswordOutcome();
      if (state.password.status === "done") {
        announce("Your password has been changed. Your other devices have been signed out.");
      } else if (state.password.errors?.length) {
        announce(state.password.errors[0]);
      }
      return;
    }

    if (what === "email") {
      if (state.emailChange.status === "sending") {
        view.setEmailBusy(true);
        announce("Sending the confirmation link…");
        return;
      }
      view.renderEmail(state.emailChange);
      // Re-enabled on success too: somebody who mistyped the new address learns nothing from the
      // response, so retrying must cost nothing but typing.
      view.setEmailBusy(false);
      view.focusEmailOutcome();
      if (state.emailChange.status === "sent") {
        announce("If that address is free, we've sent it a confirmation link. Check that inbox.");
      } else if (state.emailChange.errors?.length) {
        announce(state.emailChange.errors[0]);
      }
    }
  });
}
