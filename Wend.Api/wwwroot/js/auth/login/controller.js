// Wires the login view: submits, announces every outcome, moves focus deliberately.
export function createLoginController(model, view, announce, { onSignedIn }) {
  let seenFirstRender = false;

  view.bindActions({ submit: (fields) => model.submit(fields) });

  model.subscribe((state) => {
    if (state.status === "sending") {
      view.setBusy(true);
      announce("Signing in…");
      return;
    }

    if (state.status === "signedIn") {
      announce("Signed in.");
      onSignedIn();
      return;
    }

    view.render(state);
    view.setBusy(false);

    // The first render is the empty form: announce nothing, and leave focus where the caller put
    // it (the heading, with its own reason announced if there was one). Every later render is a
    // submit result and gets both.
    if (!seenFirstRender) {
      seenFirstRender = true;
      return;
    }

    if (state.failures === 3) {
      // The help block only just appeared. Focus it rather than the summary the user has already
      // read twice, and say why it is there.
      view.focusHelp();
      announce("Sign-in failed again. Some things to check are now shown below the form.");
    } else if (state.errors?.length) {
      view.focusError();
      announce(state.errors[0]);
    }
  });
}
