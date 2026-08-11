import { createBoardsModel } from "./boards/model.js";
import { createBoardsView } from "./boards/view.js";
import { createBoardsController } from "./boards/controller.js";
import { createAnnouncer } from "./announce.js";
import { createBoardModel } from "./board/model.js";
import { createBoardView } from "./board/view.js";
import { createBoardController } from "./board/controller.js";
import { createCardModel } from "./card/model.js";
import { createCardView } from "./card/view.js";
import { createCardController } from "./card/controller.js";
import { api } from "./api.js";
import { createToast } from "./toast.js";
import { createSettingsModel } from "./settings/model.js";
import { createSettingsView } from "./settings/view.js";
import { createSettingsController } from "./settings/controller.js";
import { createRegisterModel } from "./auth/register/model.js";
import { createRegisterView } from "./auth/register/view.js";
import { createRegisterController } from "./auth/register/controller.js";
import { createVerifyModel } from "./auth/verify/model.js";
import { createVerifyView } from "./auth/verify/view.js";
import { createVerifyController } from "./auth/verify/controller.js";
import { createLoginModel } from "./auth/login/model.js";
import { createLoginView } from "./auth/login/view.js";
import { createLoginController } from "./auth/login/controller.js";
import { createForgotModel } from "./auth/forgot/model.js";
import { createForgotView } from "./auth/forgot/view.js";
import { createForgotController } from "./auth/forgot/controller.js";

const announce = createAnnouncer(document.getElementById("status"));
const toast = createToast(document.getElementById("toast-region"));
const app = document.getElementById("app");

// A 401 mid-session is not a load failure to report — it is a session that ended, so the user goes
// back to the login screen with the reason announced and focus on the heading. Anything else is a
// genuine failure with no control to return focus to and no state to keep, so it is announced and
// nothing else happens.
function reportLoadFailure(error) {
  if (error?.status === 401) showLogin("Your session expired — please sign in again.");
  else announce("Couldn't load — please try again.");
}

// Each navigation mounts its module on a FRESH root element. The previous module's
// delegated listeners are discarded with the old element — no cross-talk, no leaks.
function mount(build) {
  app.replaceChildren();
  const root = document.createElement("div");
  app.append(root);
  build(root);
}

function showOverview(focusBoardId, focusInput = false) {
  mount((root) => {
    const model = createBoardsModel();
    const view = createBoardsView(root);
    createBoardsController(model, view, announce, { onOpen: showBoard });
    // After (re)load, return focus to the board we came back from — but not on first paint.
    model.load().then(() => {
      if (focusBoardId) view.focusOpen(focusBoardId);
      else if (focusInput) view.focusNewBoardInput();
    }).catch(reportLoadFailure);
  });
}

function showBoard(boardId, focusCardId) {
  mount((root) => {
    const model = createBoardModel(boardId);
    const view = createBoardView(root);
    createBoardController(model, view, announce, {
      onBack: () => showOverview(boardId),
      onOpenCard: (cardId) => showCard(cardId, boardId),
    });
    model.load().then(() => {
      if (focusCardId) view.focusCard(focusCardId);
      else view.focusHeading();
    }).catch(reportLoadFailure);
  });
}

function showCard(cardId, boardId, focusItemId) {
    mount((root) => {
        const model = createCardModel(cardId);
        const view = createCardView(root);
        createCardController(model, view, announce, {
            onBack: () => showBoard(boardId, cardId), // return → focus the card we opened
            onDeleted: (deletedId, title) => {
                showBoard(boardId); // card is gone → back to the board, focus the heading
                toast.show({
                    message: `Deleted: ${title}`,
                    actionLabel: "Undo",
                    onAction: () => undoDelete(deletedId, title, boardId),
                    onDismissFocus: () => document.querySelector(".board-heading")?.focus(),
                });
                announce(`Deleted: ${title}. Undo available.`);
            },
            onItemDeleted: (itemId, text) => {
                toast.show({
                    message: `Deleted: ${text}`,
                    actionLabel: "Undo",
                    onAction: () => undoItemDelete(itemId, text, cardId, boardId),
                    onDismissFocus: () => document.querySelector(".item-form input")?.focus(),
                    ariaLabel: "Deleted checklist item",
                });
                announce(`Deleted: ${text}. Undo available.`);
            },
        });
        model.load().then(() => {
            if (focusItemId) view.focusItem(focusItemId);
            else view.focusHeading();
        }).catch(reportLoadFailure);
    });
}

async function undoDelete(cardId, title, boardId) {
    try {
        await api(`/api/cards/${cardId}/restore`, { method: "POST" });
        announce(`Restored: ${title}.`);
        showBoard(boardId, cardId); // re-mount the board and focus the restored card
    } catch {
        announce("Couldn't restore the card — please try again.");
    }
}

// The toast outlives navigation, so undo RE-MOUNTS the task view from wherever we are
// (mirrors undoDelete's navigate-on-undo) and focuses the restored item — focusItem opens
// the Done strip first if the item came back checked.
async function undoItemDelete(itemId, text, cardId, boardId) {
    try {
        await api(`/api/checklist-items/${itemId}/restore`, { method: "POST" });
        announce(`Restored: ${text}.`);
        showCard(cardId, boardId, itemId);
    } catch {
        announce("Couldn't restore the item — please try again.");
    }
}

function showSettings() {
  mount((root) => {
    const model = createSettingsModel();
    const view = createSettingsView(root);
    createSettingsController(model, view, announce, { onBack: () => showOverview(null, true) });
    view.focusHeading(); // house pattern: mounting focuses the screen's heading
  });
}
document.getElementById("settings-link").addEventListener("click", showSettings);

// index.html's header belongs to the signed-in app. Left visible on an auth screen, Settings is a
// trap: it mounts the boards settings over the auth screen, and its Back goes to the board
// overview, which 401s. Both controls also sit between the skip link and the form the user came
// for. They start hidden in index.html; the gate reveals them and every auth screen re-hides them.
const APP_CHROME = ["settings-link", "logout-link"];

function hideAppChrome() {
  for (const id of APP_CHROME) document.getElementById(id).hidden = true;
}

function showAppChrome() {
  for (const id of APP_CHROME) document.getElementById(id).hidden = false;
}

async function signOut() {
  try {
    await api("/api/auth/logout", { method: "POST" });
  } catch {
    // The session may already be gone — that is the state we were heading for anyway. Moving the
    // user to the login screen is what matters, so this failure changes nothing.
  }
  showLogin("You're signed out.");
}
document.getElementById("logout-link").addEventListener("click", signOut);

function showLogin(reason) {
  hideAppChrome();
  mount((root) => {
    const model = createLoginModel();
    const view = createLoginView(root);
    createLoginController(model, view, announce, {
      onSignedIn: () => {
        showAppChrome();
        showOverview(null, true); // focus the new-board input: the first thing to do here
      },
    });
    // Focus the screen the user has just been moved to, whether they asked to come here or were
    // bounced. Never left on <body>.
    view.focusHeading();
    if (reason) announce(reason);
  });
}

function showForgot() {
  hideAppChrome();
  mount((root) => {
    const model = createForgotModel();
    const view = createForgotView(root);
    createForgotController(model, view, announce);
  });
}

function showRegister() {
  hideAppChrome();
  mount((root) => {
    const model = createRegisterModel();
    const view = createRegisterView(root);
    createRegisterController(model, view, announce);
  });
}

function showVerify() {
  hideAppChrome();
  const params = new URLSearchParams(location.search);
  const userId = params.get("userId") ?? "";
  const code = params.get("code") ?? "";

  // Drop the live token out of the address bar and the history entry as soon as it is read. It
  // still reached the server in the POST body, but it no longer sits in the URL a user might
  // screenshot, bookmark, or paste into a support chat.
  history.replaceState(null, "", "/verify");

  mount((root) => {
    const model = createVerifyModel();
    const view = createVerifyView(root);
    createVerifyController(model, view, announce, { userId, code });
  });
}

// The server renders the SPA shell for every non-API path, so the client owns routing. Auth screens
// are reached by URL because an emailed link has to land somewhere, and because /login has to be
// linkable from the register and verify screens.
async function boot() {
  switch (location.pathname) {
    case "/register": showRegister(); return;
    case "/verify": showVerify(); return;
    case "/login": showLogin(); return;
    case "/forgot-password": showForgot(); return;
  }

  // The gate: one call decides between the app and the login screen. /me answering 401 here is an
  // ordinary expected outcome, not an error to report.
  try {
    await api("/api/auth/me");
    showAppChrome();
    showOverview(); // first paint: no forced focus, skip link is available
  } catch (error) {
    if (error?.status === 401) showLogin();
    else announce("Couldn't load — please try again.");
  }
}

boot();
