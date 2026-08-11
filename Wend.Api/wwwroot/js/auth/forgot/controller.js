// Wires the forgot-password screen: submits, announces every outcome, moves focus deliberately.
export function createForgotController(model, view, announce) {
  let seenFirstRender = false;

  view.bindActions({
    submit: (fields) => model.submit(fields),
  });

  model.subscribe((state) => {
    if (state.status === "sending") {
      view.setBusy(true);
      announce("Sending…");
      return;
    }

    view.render(state);
    view.setBusy(false);

    // The first render is the empty form on page load: announce nothing, leave focus for the skip
    // link. Every later render is a submit result and gets both.
    if (!seenFirstRender) {
      seenFirstRender = true;
      return;
    }

    if (state.status === "sent") {
      view.focusSent();
      announce("If that address has an account, we've sent it a link. Check your inbox.");
    } else if (state.errors?.length) {
      view.focusFirstError();
      announce(state.errors[0]);
    }
  });
}
