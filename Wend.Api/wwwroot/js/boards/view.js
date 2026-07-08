// Renders boards (labelled controls) and forwards events via data-action. No fetch, no logic.
// Holds only transient rename UI state — the card/checklist inline-rename pattern (replaces prompt).
import { escapeHtml } from "../escape.js";

export function createBoardsView(root) {
  let lastBoards = [];
  const ui = { renamingId: null }; // board id being renamed, or null
  let h = {};

  function render(boards) {
    lastBoards = boards;
    paint();
  }

  function paint() {
    const items = lastBoards.length
      ? lastBoards
          .map((b) =>
            ui.renamingId === b.id
              ? `
        <li>
          <form class="rename-form" data-action="save-rename" data-id="${b.id}">
            <input name="text" value="${escapeHtml(b.title)}" aria-label="Board name" maxlength="200" required />
            <button type="submit">Save</button>
          </form>
        </li>`
              : `
        <li>
          <button class="board-open" data-action="open" data-id="${b.id}"
            aria-label="Open board: ${escapeHtml(b.title)}">${escapeHtml(b.title)}</button>
          <button data-action="rename" data-id="${b.id}"
            aria-label="Rename board: ${escapeHtml(b.title)}">Rename</button>
          <button data-action="delete" data-id="${b.id}"
            aria-label="Delete board: ${escapeHtml(b.title)}">Delete</button>
        </li>`
          )
          .join("")
      : `<li class="empty">No boards yet — add one above.</li>`;

    root.innerHTML = `
      <form class="board-form" data-action="create">
        <input name="title" aria-label="New board name" placeholder="New board…" required />
        <button type="submit">Add board</button>
      </form>
      <ul class="board-list">${items}</ul>`;
  }

  function focusNewBoardInput() {
    root.querySelector(".board-form input")?.focus();
  }

  // Send focus to a specific board's Open button (used when returning from its view).
  function focusOpen(id) {
    root.querySelector(`[data-action="open"][data-id="${id}"]`)?.focus();
  }

  function focusRenameTrigger(id) {
    root.querySelector(`[data-action="rename"][data-id="${id}"]`)?.focus();
  }

  // Enter / leave inline rename (no server): flip ui + repaint + place focus.
  function startRename(id) {
    ui.renamingId = id;
    paint();
    root.querySelector(".rename-form input")?.select();
  }
  function cancelRename() {
    const id = ui.renamingId;
    ui.renamingId = null;
    paint();
    if (id != null) focusRenameTrigger(id);
    else focusNewBoardInput();
  }

  function bindActions(handlers) {
    h = handlers;

    root.addEventListener("submit", async (e) => {
      const action = e.target.dataset.action;
      e.preventDefault();
      if (action === "create") {
        const title = e.target.title.value.trim();
        const submit = e.target.querySelector("button[type=submit]");
        submit.disabled = true;
        try {
          await h.create(title);
        } finally {
          submit.disabled = false;
        }
      } else if (action === "save-rename") {
        const text = e.target.elements["text"].value.trim(); // elements[] — f.text/f.name would hit form attrs
        if (!text) return;
        ui.renamingId = null;
        await h.rename(Number(e.target.dataset.id), text);
      }
    });

    root.addEventListener("click", (e) => {
      const btn = e.target.closest("button[data-action]");
      if (!btn || btn.dataset.action === "create" || btn.dataset.action === "save-rename") return;
      const id = Number(btn.dataset.id);
      if (btn.dataset.action === "open") h.open(id);
      else if (btn.dataset.action === "rename") startRename(id);
      else if (btn.dataset.action === "delete") h.delete(id);
    });

    root.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && ui.renamingId != null) {
        e.stopPropagation();
        cancelRename();
      }
    });
  }

  return { render, focusNewBoardInput, focusOpen, focusRenameTrigger, bindActions };
}
