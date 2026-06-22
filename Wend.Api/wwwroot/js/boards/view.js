// Renders boards (labelled controls) and forwards events via data-action. No fetch, no logic.
import { escapeHtml } from "../escape.js";
export function createBoardsView(root) {
  function render(boards) {
    const items = boards.length
      ? boards
          .map(
            (b) => `
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

  function bindActions(handlers) {
    root.addEventListener("submit", async (e) => {
      if (e.target.dataset.action !== "create") return;
      e.preventDefault();
      const title = e.target.title.value.trim();
      const submit = e.target.querySelector("button[type=submit]");
      submit.disabled = true;
      try {
        await handlers.create(title);
      } finally {
        submit.disabled = false;
      }
    });
    root.addEventListener("click", (e) => {
      const btn = e.target.closest("button[data-action]");
      if (!btn || btn.dataset.action === "create") return;
      const id = Number(btn.dataset.id);
      if (btn.dataset.action === "open") handlers.open(id);
      else if (btn.dataset.action === "rename") handlers.rename(id);
      else if (btn.dataset.action === "delete") handlers.delete(id);
    });
  }

  return { render, focusNewBoardInput, focusOpen, bindActions };
}