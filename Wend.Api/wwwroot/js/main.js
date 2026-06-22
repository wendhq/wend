import { createBoardsModel } from "./boards/model.js";
import { createBoardsView } from "./boards/view.js";
import { createBoardsController } from "./boards/controller.js";
import { createAnnouncer } from "./announce.js";
import { createBoardModel } from "./board/model.js";
import { createBoardView } from "./board/view.js";
import { createBoardController } from "./board/controller.js";

const announce = createAnnouncer(document.getElementById("status"));
const app = document.getElementById("app");

// Each navigation mounts its module on a FRESH root element. The previous module's
// delegated listeners are discarded with the old element — no cross-talk, no leaks.
function mount(build) {
  app.replaceChildren();
  const root = document.createElement("div");
  app.append(root);
  build(root);
}

function showOverview(focusBoardId) {
  mount((root) => {
    const model = createBoardsModel();
    const view = createBoardsView(root);
    createBoardsController(model, view, announce, { onOpen: showBoard });
    // After (re)load, return focus to the board we came back from — but not on first paint.
    model.load().then(() => {
      if (focusBoardId) view.focusOpen(focusBoardId);
    });
  });
}

function showBoard(boardId) {
  mount((root) => {
    const model = createBoardModel(boardId);
    const view = createBoardView(root);
    createBoardController(model, view, announce, { onBack: () => showOverview(boardId) });
    model.load().then(() => view.focusHeading());
  });
}

showOverview(); // first paint: no forced focus, skip link is available
