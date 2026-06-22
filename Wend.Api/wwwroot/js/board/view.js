// Renders one board's view: back link, title, add-list form, and the lists with
// move/rename/delete controls. Forwards events via data-action. No fetch, no logic.
export function createBoardView(root) {
  function render(board) {
    const lists = board.lists;
    const items = lists.length
      ? lists
          .map((l, i) => {
            const first = i === 0;
            const last = i === lists.length - 1;
            return `
<li class="list-card" data-list-id="${l.id}">
    <span class="list-title">${escapeHtml(l.title)}</span>
<div class="list-actions">
    <button data-action="move-left" data-id="${l.id}" ${first ? "disabled" : ""}
            aria-label="Move list left: ${escapeHtml(l.title)}">◀</button>
    <button data-action="move-right" data-id="${l.id}" ${last ? "disabled" : ""}
            aria-label="Move list right: ${escapeHtml(l.title)}">▶</button>
    <button data-action="rename" data-id="${l.id}"
            aria-label="Rename list: ${escapeHtml(l.title)}">Rename</button>
    <button data-action="delete" data-id="${l.id}"
            aria-label="Delete list: ${escapeHtml(l.title)}">Delete</button>
</div>
</li>`;
          })
          .join("")
      : `<li class="empty">No lists yet — add one above.</li>`;

    root.innerHTML = `
<div class="board-view">
    <button class="back-link" data-action="back">← Boards</button>
<h2 class="board-heading" tabindex="-1">${escapeHtml(board.title)}</h2>
<form class="list-form" data-action="create">
    <input name="title" aria-label="New list name" placeholder="New list…" required />
    <button type="submit">Add list</button>
</form>
<ul class="list-columns">${items}</ul>
</div>`;
  }

  function focusHeading() {
    root.querySelector(".board-heading")?.focus();
  }
  function focusNewListInput() {
    root.querySelector(".list-form input")?.focus();
  }

  // After a move, land focus on a sensible enabled control in the moved list (the move
  // button just pressed may now be disabled at an end), so keyboard users keep their place.
  function focusListAction(id, preferred) {
    const card = root.querySelector(`.list-card[data-list-id="${id}"]`);
    if (!card) return;
    const order = preferred === "move-left"
      ? ["move-left", "move-right", "rename"]
      : ["move-right", "move-left", "rename"];
    for (const action of order) {
      const btn = card.querySelector(`[data-action="${action}"]`);
      if (btn && !btn.disabled) { btn.focus(); return; }
    }
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
      const action = btn.dataset.action;
      if (action === "back") return handlers.back();
      const id = Number(btn.dataset.id);
      if (action === "rename") handlers.rename(id);
      else if (action === "delete") handlers.delete(id);
      else if (action === "move-left") handlers.moveLeft(id);
      else if (action === "move-right") handlers.moveRight(id);
    });
  }

  return { render, focusHeading, focusNewListInput, focusListAction, bindActions };
}

function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]
  );
}