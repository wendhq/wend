import { escapeHtml } from "../escape.js";
import { getPrefs } from "../prefs.js";

// Renders one board's view: back link, title, add-list form, each list with its move/rename/delete
// controls, its ACTIVE cards (a leading done checkbox + label chips + move controls) and an add-card
// form, then a collapsible global Done area (done cards grouped by list, each with an un-check).
// "Done" is a render grouping on completedAt; the collapse state lives here (ui.doneOpen).
export function createBoardView(root) {
  let lastBoard = null;
  const ui = { doneOpen: false };

  function render(board) {
    lastBoard = board;
    paint();
  }

  function paint() {
    const board = lastBoard;
    const lists = board.lists;
    const labelsById = new Map((board.labels ?? []).map((l) => [l.id, l]));
    const prefs = getPrefs();

    // Soft-tint chips for a card's labels (skips ids missing from the palette).
    const labelChips = (ids) =>
        (ids ?? [])
            .map((id) => labelsById.get(id))
            .filter(Boolean)
            .map((l) => `<span class="label-chip label-chip--${l.colour}">${escapeHtml(l.name)}</span>`)
            .join("");

    const cardAria = (c) => {
      const names = (c.labelIds ?? []).map((id) => labelsById.get(id)).filter(Boolean).map((l) => l.name);
      const progress = c.checklistTotal ? `, ${c.checklistDone} of ${c.checklistTotal} done` : "";
      return `Open card: ${c.title}${names.length ? `, labels: ${names.join(", ")}` : ""}${progress}`;
    };

    const items = lists.length
        ? lists
            .map((l, i) => {
              const first = i === 0;
              const last = i === lists.length - 1;
              const activeCards = (l.cards ?? []).filter((c) => !c.completedAt);
              const otherLists = lists.filter((t) => t.id !== l.id);
              const moveOptions = otherLists
                  .map((t) => `<option value="${t.id}">${escapeHtml(t.title)}</option>`)
                  .join("");
              const cards = activeCards
                  .map((c, ci) => {
                    const chips = labelChips(c.labelIds);
                    const firstCard = ci === 0;
                    const lastCard = ci === activeCards.length - 1;
                    return `
            <li class="card-item" data-card-id="${c.id}" data-list-id="${l.id}">
              <div class="card-row">
                ${prefs.showCardDone ? `<input type="checkbox" class="card-done-toggle" data-action="toggle-done" data-card-id="${c.id}"
                  aria-label="Mark done: ${escapeHtml(c.title)}" />` : ""}
                <button class="card-chip" data-action="open-card" data-card-id="${c.id}"
                  aria-label="${escapeHtml(cardAria(c))}">
                  ${chips ? `<span class="card-chip-labels">${chips}</span>` : ""}
                  <span class="card-title">${escapeHtml(c.title)}</span>
                  ${c.dueDate ? `<span class="card-due">${escapeHtml(c.dueDate)}</span>` : ""}
                  ${c.checklistTotal ? `<span class="card-checklist">☑ ${c.checklistDone}/${c.checklistTotal}</span>
                  <span class="card-progress" aria-hidden="true"><span class="card-progress-fill" style="width:${Math.round((c.checklistDone / c.checklistTotal) * 100)}%"></span></span>` : ""}
                </button>
              </div>
              <div class="card-actions">
                <button data-action="card-up" data-card-id="${c.id}" ${firstCard ? "disabled" : ""}
                  aria-label="Move card up: ${escapeHtml(c.title)}">▲</button>
                <button data-action="card-down" data-card-id="${c.id}" ${lastCard ? "disabled" : ""}
                  aria-label="Move card down: ${escapeHtml(c.title)}">▼</button>
                <select class="card-move-to" data-action="card-move-to" data-card-id="${c.id}"
                  aria-label="Move card to another list: ${escapeHtml(c.title)}" ${otherLists.length ? "" : "disabled"}>
                  <option value="" selected disabled>Move to…</option>
                  ${moveOptions}
                </select>
              </div>
            </li>`;
                  })
                  .join("");
              return `
        <li class="list-card" data-list-id="${l.id}" role="group" aria-labelledby="list-${l.id}-title">
          <h3 id="list-${l.id}-title" class="list-title">${escapeHtml(l.title)}</h3>
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
          <ul class="card-list">${cards}</ul>
          <form class="card-form" data-action="create-card" data-list-id="${l.id}">
            <input name="title" aria-label="Add a card to ${escapeHtml(l.title)}" placeholder="Add a card…" required />
            <button type="submit">Add</button>
          </form>
        </li>`;
            })
            .join("")
        : `<li class="empty">No lists yet — add one above.</li>`;

    // Done area: done cards grouped under their list. Hidden entirely when nothing is done.
    const doneGroups = lists
        .map((l) => ({ title: l.title, cards: (l.cards ?? []).filter((c) => c.completedAt) }))
        .filter((g) => g.cards.length);
    const doneCount = doneGroups.reduce((n, g) => n + g.cards.length, 0);
    const doneArea = doneCount === 0 ? "" : `
        <section class="done-area" aria-label="Done">
          <button class="done-toggle" data-action="toggle-done-section" aria-expanded="${ui.doneOpen ? "true" : "false"}">
            ✓ Done (${doneCount})
          </button>
          ${ui.doneOpen ? `<div class="done-body">${doneGroups
              .map((g) => `
            <div class="done-group">
              <h3 class="done-group-title">${escapeHtml(g.title)}</h3>
              <ul class="done-list">${g.cards
                  .map((c) => `
                <li class="done-row" data-card-id="${c.id}">
                  <label class="done-row-label">
                    <input type="checkbox" class="card-done-toggle" data-action="toggle-done" data-card-id="${c.id}" checked
                      aria-label="Mark not done: ${escapeHtml(c.title)}" />
                    <span class="done-row-title">${escapeHtml(c.title)}</span>
                  </label>
                </li>`)
                  .join("")}</ul>
            </div>`)
              .join("")}</div>` : ""}
        </section>`;

    root.innerHTML = `
      <div class="board-view">
        <button class="back-link" data-action="back">← Boards</button>
        <h2 class="board-heading" tabindex="-1">${escapeHtml(board.title)}</h2>
        <form class="list-form" data-action="create">
          <input name="title" aria-label="New list name" placeholder="New list…" required />
          <button type="submit">Add list</button>
        </form>
        <ul class="list-columns">${items}</ul>
        ${doneArea}
      </div>`;
  }

  function focusHeading() {
    root.querySelector(".board-heading")?.focus();
  }
  function focusNewListInput() {
    root.querySelector(".list-form input")?.focus();
  }
  function focusNewCardInput(listId) {
    root.querySelector(`.card-form[data-list-id="${listId}"] input`)?.focus();
  }
  function focusCard(cardId) {
    root.querySelector(`.card-chip[data-card-id="${cardId}"]`)?.focus();
  }
  function focusDoneToggle() {
    const t = root.querySelector(".done-toggle");
    if (t) t.focus();
    else focusHeading(); // fallback so a vanished toggle can't strand focus on <body>
  }

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

  // After a card move, return focus to the moved card so a keyboard user can keep going.
  function focusCardAction(cardId, preferred) {
    const item = root.querySelector(`.card-item[data-card-id="${cardId}"]`);
    if (!item) return;
    const order = preferred === "card-up" ? ["card-up", "card-down"] : ["card-down", "card-up"];
    for (const action of order) {
      const btn = item.querySelector(`button[data-action="${action}"]`);
      if (btn && !btn.disabled) { btn.focus(); return; }
    }
    const select = item.querySelector('select[data-action="card-move-to"]');
    if (select && !select.disabled) { select.focus(); return; }
    item.querySelector(".card-chip")?.focus();
  }

  function bindActions(handlers) {
    root.addEventListener("submit", async (e) => {
      const action = e.target.dataset.action;
      if (action !== "create" && action !== "create-card") return;
      e.preventDefault();
      const title = e.target.title.value.trim();
      const submit = e.target.querySelector("button[type=submit]");
      submit.disabled = true;
      try {
        if (action === "create") await handlers.create(title);
        else await handlers.createCard(Number(e.target.dataset.listId), title);
      } finally {
        submit.disabled = false;
      }
    });
    root.addEventListener("click", (e) => {
      const btn = e.target.closest("button[data-action]");
      if (!btn || btn.dataset.action === "create" || btn.dataset.action === "create-card") return;
      const action = btn.dataset.action;
      if (action === "toggle-done-section") { ui.doneOpen = !ui.doneOpen; paint(); focusDoneToggle(); return; }
      if (action === "back") return handlers.back();
      if (action === "open-card") return handlers.openCard(Number(btn.dataset.cardId));
      if (action === "card-up") return handlers.cardUp(Number(btn.dataset.cardId));
      if (action === "card-down") return handlers.cardDown(Number(btn.dataset.cardId));
      const id = Number(btn.dataset.id);
      if (action === "rename") handlers.rename(id);
      else if (action === "delete") handlers.delete(id);
      else if (action === "move-left") handlers.moveLeft(id);
      else if (action === "move-right") handlers.moveRight(id);
    });
    root.addEventListener("change", (e) => {
      const done = e.target.closest('input[data-action="toggle-done"]');
      if (done) return handlers.toggleDone(Number(done.dataset.cardId), done.checked);
      const sel = e.target.closest('select[data-action="card-move-to"]');
      if (!sel) return;
      const listId = Number(sel.value);
      if (!listId) return;
      handlers.moveCardTo(Number(sel.dataset.cardId), listId);
    });
  }

  return { render, focusHeading, focusNewListInput, focusNewCardInput, focusCard, focusDoneToggle, focusListAction, focusCardAction, bindActions };
}
