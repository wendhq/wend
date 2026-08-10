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

const announce = createAnnouncer(document.getElementById("status"));
const toast = createToast(document.getElementById("toast-region"));
const app = document.getElementById("app");

// A failed first load has no control to return focus to and no state to keep, so it is
// announced and nothing else happens. Without this the rejection is unhandled and the
// screen just stays empty with no explanation.
// Plan 3 replaces the 401 branch with the auth gate: redirect to the login screen.
function reportLoadFailure(error) {
  if (error?.status === 401) announce("You're not signed in, so there's nothing to show.");
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
// overview, which 401s. It is also the first thing after the skip link in the tab order, so a
// keyboard user meets it before the form they came for.
function hideAppChrome() {
  document.getElementById("settings-link").hidden = true;
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

// The server renders the SPA shell for every non-API path, so the client owns routing. Auth
// screens are reached by URL because an emailed link has to land somewhere. Plan 4 replaces this
// with the real auth gate, which decides between the app and the login screen on boot.
switch (location.pathname) {
  case "/register":
    showRegister();
    break;
  case "/verify":
    showVerify();
    break;
  default:
    showOverview(); // first paint: no forced focus, skip link is available
}
