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

const announce = createAnnouncer(document.getElementById("status"));
const toast = createToast(document.getElementById("toast-region"));
const app = document.getElementById("app");

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
    });
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
    });
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
        });
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

showOverview(); // first paint: no forced focus, skip link is available
