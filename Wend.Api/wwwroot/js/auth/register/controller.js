import { api } from "../../api.js";

// Wires the registration view: submits, announces every outcome, moves focus deliberately.
export function createRegisterController(model, view, announce) {
  let lastEmail = "";
  let seenFirstRender = false;

  view.bindActions({
    submit: (fields) => {
      lastEmail = fields.email;
      model.submit(fields);
    },
    resend: async () => {
      try {
        await api("/api/auth/resend-verification", {
          method: "POST",
          body: JSON.stringify({ email: lastEmail }),
        });
        announce("If that address needs confirming, we've sent another link.");
      } catch {
        announce("Couldn't send another link — please try again.");
      }
    },
  });

  model.subscribe((state) => {
    if (state.status === "sending") {
      view.setBusy(true);
      announce("Creating your account…");
      return;
    }

    view.render(state);
    view.setBusy(false);

    // The first render is the empty form on page load: announce nothing, and leave focus for the
    // skip link. Every later render is a submit result and gets both.
    if (!seenFirstRender) {
      seenFirstRender = true;
      return;
    }

    if (state.status === "sent") {
      view.focusHeading();
      announce("Check your email for a link to confirm your address.");
    } else if (state.errors?.length) {
      view.focusFirstError();
      announce(state.errors[0]);
    }
  });
}
