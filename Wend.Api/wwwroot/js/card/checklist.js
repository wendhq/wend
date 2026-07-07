import { escapeHtml } from "../escape.js";

// Returns the Checklist-section HTML from (card, ui). Pure — no state, no fetch.
// ui = { editMode, doneOpen, renamingId } — renamingId is an item id, "title", or null.
// Unchecked items keep their stored order; checked items render inside a collapsible Done
// strip (same Position sequence, so un-checking drops an item back where it lived).
export function renderChecklist(card, ui) {
  const items = card.items ?? [];
  const unchecked = items.filter((i) => !i.checkedAt);
  const done = items.filter((i) => i.checkedAt);
  const total = items.length;

  const itemText = (i) =>
    ui.renamingId === i.id
      ? `<form class="rename-form" data-action="save-item-rename" data-item-id="${i.id}">
          <input name="text" value="${escapeHtml(i.text)}" aria-label="Item text" maxlength="200" required />
          <button type="submit">Save</button>
        </form>`
      : `<button type="button" class="rename-trigger" data-action="rename-item" data-item-id="${i.id}"
          aria-label="Rename: ${escapeHtml(i.text)}">${escapeHtml(i.text)}</button>`;

  const uncheckedRows = unchecked
    .map((i, idx) => `
      <li class="checklist-row" data-item-id="${i.id}">
        <input type="checkbox" data-action="toggle-item" data-item-id="${i.id}"
          aria-label="Mark done: ${escapeHtml(i.text)}" />
        ${itemText(i)}
        ${ui.editMode ? `<span class="item-actions">
          <button type="button" data-action="item-up" data-item-id="${i.id}" ${idx === 0 ? "disabled" : ""}
            aria-label="Move up: ${escapeHtml(i.text)}">▲</button>
          <button type="button" data-action="item-down" data-item-id="${i.id}" ${idx === unchecked.length - 1 ? "disabled" : ""}
            aria-label="Move down: ${escapeHtml(i.text)}">▼</button>
          <button type="button" data-action="delete-item" data-item-id="${i.id}"
            aria-label="Delete: ${escapeHtml(i.text)}">✕</button>
        </span>` : ""}
      </li>`)
    .join("");

  const doneStrip = done.length ? `
    <div class="done-strip">
      <button type="button" class="done-toggle" data-action="toggle-item-done-section"
        aria-expanded="${ui.doneOpen ? "true" : "false"}">✓ Done (${done.length})</button>
      ${ui.doneOpen ? `<ul class="done-items">${done
        .map((i) => `
        <li class="done-item-row" data-item-id="${i.id}">
          <label class="done-row-label">
            <input type="checkbox" data-action="toggle-item" data-item-id="${i.id}" checked
              aria-label="Mark not done: ${escapeHtml(i.text)}" />
            <span class="done-item-text">${escapeHtml(i.text)}</span>
          </label>
          ${ui.editMode ? `<button type="button" data-action="delete-item" data-item-id="${i.id}"
            aria-label="Delete: ${escapeHtml(i.text)}">✕</button>` : ""}
        </li>`)
        .join("")}</ul>` : ""}
    </div>` : "";

  return `
    <section class="checklist-section" aria-label="Checklist">
      <div class="checklist-header">
        <span class="checklist-title">Checklist</span>
        ${total ? `<span class="card-checklist">☑ ${done.length}/${total}</span>` : ""}
      </div>
      ${total ? `<span class="card-progress" aria-hidden="true"><span class="card-progress-fill" style="width:${Math.round((done.length / total) * 100)}%"></span></span>` : ""}
      <ul class="checklist-items">${uncheckedRows}</ul>
      ${doneStrip}
      <form class="item-form" data-action="add-item">
        <input name="text" aria-label="Add a checklist item" placeholder="Add an item…" maxlength="200" required />
        <button type="submit">Add</button>
      </form>
    </section>`;
}
