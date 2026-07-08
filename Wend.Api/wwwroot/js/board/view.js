import { escapeHtml } from "../escape.js";
import { getPrefs, getSelectedListId } from "../prefs.js";

// Renders one board's view: back link, title, add-list form, each list with its move/rename/delete
// controls, its ACTIVE cards (a leading done checkbox + label chips + move controls), a collapsible
// per-list Done strip (that list's completed cards, each with an un-check), and an add-card form.
// "Done" is a render grouping on completedAt; per-list collapse state lives here (ui.doneOpenLists).
export function createBoardView(root) {
  let lastBoard = null;
  const ui = { doneOpenLists: new Set(), renamingId: null, selectedListId: null };

  function render(board) {
    lastBoard = board;
    paint();
  }

  function paint() {
    const board = lastBoard;
    const lists = board.lists;
    // Resolve the mobile switcher's current list: keep a valid selection, else the remembered
    // one (validated), else the first list. Self-heals when the selected list is deleted.
    const validIds = new Set(lists.map((l) => l.id));
    if (!validIds.has(ui.selectedListId)) {
      const remembered = getSelectedListId(board.id);
      ui.selectedListId = validIds.has(remembered) ? remembered : (lists[0]?.id ?? null);
    }
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
              const doneCards = (l.cards ?? []).filter((c) => c.completedAt);
              const doneOpen = ui.doneOpenLists.has(l.id);
              const doneStrip = doneCards.length ? `
          <div class="done-strip">
            <button type="button" class="done-toggle" data-action="toggle-list-done" data-list-id="${l.id}"
              aria-expanded="${doneOpen ? "true" : "false"}">✓ Done (${doneCards.length})</button>
            ${doneOpen ? `<ul class="done-items">${doneCards
                .map((c) => `
              <li class="done-item-row" data-card-id="${c.id}">
                <label class="checklist-done-label">
                  <input type="checkbox" data-action="toggle-done" data-card-id="${c.id}" checked
                    aria-label="Mark not done: ${escapeHtml(c.title)}" />
                  <span class="done-item-text">${escapeHtml(c.title)}</span>
                </label>
              </li>`)
                .join("")}</ul>` : ""}
          </div>` : "";
              return `
        <li class="list-card${l.id === ui.selectedListId ? " is-current" : ""}" data-list-id="${l.id}" role="group" aria-labelledby="list-${l.id}-title">
          <h3 id="list-${l.id}-title" class="list-title">${escapeHtml(l.title)}</h3>
          ${ui.renamingId === l.id
            ? `<form class="rename-form" data-action="save-list-rename" data-id="${l.id}">
                <input name="text" value="${escapeHtml(l.title)}" aria-label="List name" maxlength="200" required />
                <button type="submit">Save</button>
              </form>`
            : `<div class="list-actions">
            <button data-action="move-left" data-id="${l.id}" ${first ? "disabled" : ""}
              aria-label="Move list left: ${escapeHtml(l.title)}">◀</button>
            <button data-action="move-right" data-id="${l.id}" ${last ? "disabled" : ""}
              aria-label="Move list right: ${escapeHtml(l.title)}">▶</button>
            <button data-action="rename" data-id="${l.id}"
              aria-label="Rename list: ${escapeHtml(l.title)}">Rename</button>
            <button data-action="delete" data-id="${l.id}"
              aria-label="Delete list: ${escapeHtml(l.title)}">Delete</button>
          </div>`}
          <ul class="card-list">${cards}</ul>
          ${doneStrip}
          <form class="card-form" data-action="create-card" data-list-id="${l.id}">
            <input name="title" aria-label="Add a card to ${escapeHtml(l.title)}" placeholder="Add a card…" required />
            <button type="submit">Add</button>
          </form>
        </li>`;
            })
            .join("")
        : `<li class="empty">No lists yet — add one above.</li>`;

    const switcher = lists.length ? `
        <div class="list-switcher-row">
          <label class="list-switcher-label" for="list-switcher">List</label>
          <select class="list-switcher" id="list-switcher" data-action="switch-list">
            ${lists.map((l) => `<option value="${l.id}"${l.id === ui.selectedListId ? " selected" : ""}>${escapeHtml(l.title)}</option>`).join("")}
          </select>
        </div>` : "";

    root.innerHTML = `
      <div class="board-view">
        <button class="back-link" data-action="back">← Boards</button>
        <h2 class="board-heading" tabindex="-1">${escapeHtml(board.title)}</h2>
        <form class="list-form" data-action="create">
          <input name="title" aria-label="New list name" placeholder="New list…" required />
          <button type="submit">Add list</button>
        </form>
        ${switcher}
        <ul class="list-columns">${items}</ul>
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
  function focusListDoneToggle(listId) {
    const t = root.querySelector(`.list-card[data-list-id="${listId}"] .done-toggle`);
    if (t) t.focus();
    else focusHeading(); // never strand focus on <body>
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

  function focusListRenameTrigger(id) {
    root.querySelector(`[data-action="rename"][data-id="${id}"]`)?.focus();
  }
  // Enter / leave inline list rename (no server): flip ui + repaint + place focus.
  function startListRename(id) {
    ui.renamingId = id;
    paint();
    root.querySelector(".rename-form input")?.select();
  }
  function cancelListRename() {
    const id = ui.renamingId;
    ui.renamingId = null;
    paint();
    if (id != null) focusListRenameTrigger(id);
  }

  function bindActions(handlers) {
    root.addEventListener("submit", async (e) => {
      const action = e.target.dataset.action;
      if (action === "save-list-rename") {
        e.preventDefault();
        const text = e.target.elements["text"].value.trim();
        if (!text) return;
        ui.renamingId = null;
        await handlers.rename(Number(e.target.dataset.id), text);
        return;
      }
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
      if (action === "toggle-list-done") {
        const lid = Number(btn.dataset.listId);
        ui.doneOpenLists.has(lid) ? ui.doneOpenLists.delete(lid) : ui.doneOpenLists.add(lid);
        paint();
        focusListDoneToggle(lid);
        return;
      }
      if (action === "back") return handlers.back();
      if (action === "open-card") return handlers.openCard(Number(btn.dataset.cardId));
      if (action === "card-up") return handlers.cardUp(Number(btn.dataset.cardId));
      if (action === "card-down") return handlers.cardDown(Number(btn.dataset.cardId));
      const id = Number(btn.dataset.id);
      if (action === "rename") startListRename(id);
      else if (action === "delete") handlers.delete(id);
      else if (action === "move-left") handlers.moveLeft(id);
      else if (action === "move-right") handlers.moveRight(id);
    });
    root.addEventListener("change", (e) => {
      const done = e.target.closest('input[data-action="toggle-done"]');
      if (done) return handlers.toggleDone(Number(done.dataset.cardId), done.checked);
      const sw = e.target.closest('select[data-action="switch-list"]');
      if (sw) {
        ui.selectedListId = Number(sw.value);
        paint();
        root.querySelector(".list-switcher")?.focus(); // repaint re-creates the select — refocus it
        return handlers.selectList(ui.selectedListId);
      }
      const sel = e.target.closest('select[data-action="card-move-to"]');
      if (!sel) return;
      const listId = Number(sel.value);
      if (!listId) return;
      handlers.moveCardTo(Number(sel.dataset.cardId), listId);
    });
    root.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && ui.renamingId != null) { e.stopPropagation(); cancelListRename(); }
    });
  }

  return { render, focusHeading, focusNewListInput, focusNewCardInput, focusCard, focusListDoneToggle, focusListAction, focusCardAction, focusListRenameTrigger, bindActions };
}
