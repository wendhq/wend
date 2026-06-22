import { createBoardsModel } from "./boards/model.js";
import { createBoardsView } from "./boards/view.js";
import { createBoardsController } from "./boards/controller.js";
import { createAnnouncer } from "./announce.js";

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

// Placeholder board view — Task 10 swaps in the real lists module.
function showBoard(boardId) {
  mount((root) => {
    root.innerHTML = `
      <div class="board-view">
        <button class="back-link" data-action="back">← Boards</button>
        <h2 class="board-heading" tabindex="-1">Board #${boardId}</h2>
        <p class="empty">Lists coming in the next step…</p>
      </div>`;
    root.querySelector('[data-action="back"]')
        .addEventListener("click", () => showOverview(boardId));
    root.querySelector(".board-heading").focus();
  });
}

showOverview(); // first paint: no forced focus, skip link is available
