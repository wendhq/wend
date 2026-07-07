import { escapeHtml } from "../escape.js";
import { renderLabels } from "./labels.js";
import { renderChecklist } from "./checklist.js";
import { getPrefs } from "../prefs.js";

// Task view for one card: back link, heading, the Labels section + picker, the edit form, and
// delete. Holds only transient picker UI state; all data comes from the model. The view owns the
// purely-visual picker transitions (open / create / edit); the controller owns anything that
// touches the server. Events via data-action.
export function createCardView(root) {
  let lastCard = null;
  let lastPalette = [];
  const ui = { pickerOpen: false, mode: "list", editingId: null, editMode: false, doneOpen: false, renamingId: null };
  let h = {};

  function render(card, palette) {
    lastCard = card;
    lastPalette = palette ?? [];
    paint();
  }

  function paint() {
    const card = lastCard;
    const prefs = getPrefs();
    root.innerHTML = `
      <div class="card-view">
        <div class="card-view-top">
          <button class="back-link" data-action="back">← Board</button>
          <button type="button" class="edit-toggle" data-action="toggle-edit"
            aria-pressed="${ui.editMode ? "true" : "false"}">${ui.editMode ? "Editing…" : "Edit"}</button>
        </div>
        <h2 class="card-heading" tabindex="-1">${
          ui.renamingId === "title"
            ? `<form class="rename-form" data-action="save-title">
                <input name="text" value="${escapeHtml(card.title)}" aria-label="Card title" maxlength="200" required />
                <button type="submit">Save</button>
              </form>`
            : `<button type="button" class="rename-trigger" data-action="rename-title"
                aria-label="Rename: ${escapeHtml(card.title)}">${escapeHtml(card.title)}</button>`
        }</h2>
        <p class="card-list-name">In list: <strong>${escapeHtml(card.listTitle)}</strong></p>
        ${prefs.showCardDone ? `<div class="card-done">
          <label class="card-done-label">
            <input type="checkbox" data-action="toggle-done" ${card.completedAt ? "checked" : ""} />
            <span>Done</span>
          </label>
        </div>` : ""}
        ${renderLabels(card, lastPalette, ui)}
        ${renderChecklist(card, ui)}
        ${ui.editMode ? `
        <form class="card-detail" data-action="save">
          <label class="field">
            <span>Due date</span>
            <input name="dueDate" type="date" value="${card.dueDate ?? ""}" aria-label="Due date" />
          </label>
          <label class="field">
            <span>Notes</span>
            <textarea name="description" aria-label="Notes">${escapeHtml(card.description ?? "")}</textarea>
          </label>
          <button type="submit">Save changes</button>
        </form>` : `
        ${card.dueDate ? `<p class="card-meta">Due: <strong>${escapeHtml(card.dueDate)}</strong></p>` : ""}
        ${card.description ? `<p class="card-notes">${escapeHtml(card.description)}</p>` : ""}`}
        ${ui.editMode || prefs.alwaysShowDeleteCard ? `<button class="card-delete" data-action="delete">Delete card</button>` : ""}
      </div>`;
  }

  // Focus helpers.
  function focusHeading() { root.querySelector(".card-heading")?.focus(); }
  function focusDoneToggle() { root.querySelector('input[data-action="toggle-done"]')?.focus(); }
  function focusPickerTrigger() { root.querySelector(".labels-toggle")?.focus(); }
  function focusToggle(labelId) {
    root.querySelector(`input[data-action="toggle-label"][data-label-id="${labelId}"]`)?.focus();
  }
  function focusPicker() { root.querySelector(".label-picker input, .label-picker button")?.focus(); }
  function focusLabelName() { root.querySelector(".label-form input[name=name]")?.focus(); }
  function focusAddInput() { root.querySelector(".item-form input")?.focus(); }
  function focusDoneStripToggle() { root.querySelector(".done-strip .done-toggle")?.focus(); }
  function focusRenameTrigger(id) { root.querySelector(`[data-action="rename-item"][data-item-id="${id}"]`)?.focus(); }
  function focusItem(id) {
    const item = (lastCard.items ?? []).find((i) => i.id === id);
    if (item?.checkedAt && !ui.doneOpen) { ui.doneOpen = true; paint(); } // open the strip first
    root.querySelector(`input[data-action="toggle-item"][data-item-id="${id}"]`)?.focus();
  }

  // Purely-visual rename transitions (no server): flip ui + repaint + place focus.
  function startItemRename(id) { ui.renamingId = id; paint(); root.querySelector(".rename-form input")?.select(); }
  function cancelRename() {
    const id = ui.renamingId;
    ui.renamingId = null;
    paint();
    if (id !== null && id !== "title") focusRenameTrigger(id);
    else focusHeading();
  }

  function focusEditToggle() { root.querySelector(".edit-toggle")?.focus(); }
  // Flips Edit mode, repaints, and parks focus on the toggle — so focus never dies with a
  // control that just disappeared. Returns the new state for the controller's announcement.
  function toggleEditMode() {
    ui.editMode = !ui.editMode;
    paint();
    focusEditToggle();
    return ui.editMode;
  }

  function focusTitleTrigger() { root.querySelector('[data-action="rename-title"]')?.focus(); }
  // After a move, refocus the pressed button — or its opposite when the item reached an end
  // and the pressed one went disabled (focusCardAction's fallback, or focus dies on <body>).
  function focusItemAction(id, action) {
    const row = root.querySelector(`.checklist-row[data-item-id="${id}"]`);
    if (!row) return;
    const order = action === "item-up" ? ["item-up", "item-down"] : ["item-down", "item-up"];
    for (const a of order) {
      const btn = row.querySelector(`[data-action="${a}"]`);
      if (btn && !btn.disabled) { btn.focus(); return; }
    }
  }

  // Purely-visual picker transitions (no server): flip ui + repaint + place focus.
  function openPicker() { ui.pickerOpen = true; ui.mode = "list"; paint(); focusPicker(); }
  function closePicker() { ui.pickerOpen = false; ui.mode = "list"; ui.editingId = null; paint(); focusPickerTrigger(); }
  function toCreate() { ui.mode = "create"; paint(); focusLabelName(); }
  function toEdit(id) { ui.mode = "edit"; ui.editingId = id; paint(); focusLabelName(); }
  function toList() { ui.mode = "list"; ui.editingId = null; paint(); focusPicker(); }

  function labelName(id) { return (lastPalette.find((l) => l.id === id) || {}).name || ""; }

  function bindActions(handlers) {
    h = handlers;

    root.addEventListener("submit", async (e) => {
      const action = e.target.dataset.action;
      if (action === "save") {
        e.preventDefault();
        const f = e.target;
        const submit = f.querySelector("button[type=submit]");
        submit.disabled = true;
        try {
          await h.save({ title: lastCard.title, description: f.description.value, dueDate: f.dueDate.value || null });
        } finally {
          submit.disabled = false;
        }
      } else if (action === "add-label" || action === "save-label") {
        e.preventDefault();
        const f = e.target;
        const name = f.elements["name"].value.trim(); // f.name would be the form's name attr
        const colour = f.elements["colour"].value;
        if (!name) return;
        const submit = f.querySelector("button[type=submit]");
        submit.disabled = true;
        try {
          if (action === "add-label") await h.createLabel(name, colour);
          else await h.editLabel(Number(f.dataset.labelId), name, colour);
          toList();
        } finally {
          submit.disabled = false;
        }
      } else if (action === "add-item") {
        e.preventDefault();
        const f = e.target;
        const text = f.elements["text"].value.trim(); // f.text would shadow the form's name attr
        if (!text) return;
        const submit = f.querySelector("button[type=submit]");
        submit.disabled = true;
        try { await h.addItem(text); } finally { submit.disabled = false; }
      } else if (action === "save-item-rename") {
        e.preventDefault();
        const f = e.target;
        const text = f.elements["text"].value.trim();
        if (!text) return;
        ui.renamingId = null;
        await h.renameItem(Number(f.dataset.itemId), text);
      } else if (action === "save-title") {
        e.preventDefault();
        const text = e.target.elements["text"].value.trim();
        if (!text) return;
        ui.renamingId = null;
        await h.saveTitle(text);
      }
    });

    root.addEventListener("change", async (e) => {
      const done = e.target.closest('input[data-action="toggle-done"]');
      if (done) return h.toggleDone(done.checked);
      const item = e.target.closest('input[data-action="toggle-item"]');
      if (item) return h.toggleItem(Number(item.dataset.itemId), item.checked);
      const cb = e.target.closest('input[data-action="toggle-label"]');
      if (!cb) return;
      const id = Number(cb.dataset.labelId);
      if (cb.checked) await h.attachLabel(id);
      else await h.detachLabel(id);
    });

    root.addEventListener("keydown", (e) => {
      if (e.key !== "Escape") return;
      if (ui.renamingId !== null) { e.stopPropagation(); cancelRename(); return; }
      if (ui.pickerOpen) { e.stopPropagation(); closePicker(); return; }
      if (ui.editMode) { e.stopPropagation(); h.toggleEditMode(); }
    });

    root.addEventListener("click", (e) => {
      const btn = e.target.closest("[data-action]");
      if (!btn) return;
      const a = btn.dataset.action;
      if (["save", "add-label", "save-label", "toggle-label", "add-item", "save-item-rename", "toggle-item"].includes(a)) return; // handled by submit/change
      if (a === "rename-item") return startItemRename(Number(btn.dataset.itemId));
      if (a === "toggle-item-done-section") { ui.doneOpen = !ui.doneOpen; paint(); focusDoneStripToggle(); return; }
      if (a === "toggle-edit") return h.toggleEditMode();
      if (a === "rename-title") { ui.renamingId = "title"; paint(); root.querySelector(".rename-form input")?.select(); return; }
      if (a === "item-up") return h.moveItemUp(Number(btn.dataset.itemId));
      if (a === "item-down") return h.moveItemDown(Number(btn.dataset.itemId));
      if (a === "delete-item") return h.deleteItem(Number(btn.dataset.itemId));
      if (a === "back") return h.back();
      if (a === "delete") return h.delete();
      if (a === "toggle-picker") return ui.pickerOpen ? closePicker() : openPicker();
      if (a === "create-label-open") return toCreate();
      if (a === "cancel-label") return toList();
      if (a === "edit-label") return toEdit(Number(btn.dataset.labelId));
      if (a === "delete-label") {
        const id = Number(btn.dataset.labelId);
        return h.deleteLabel(id, labelName(id));
      }
    });
  }

  return { render, focusHeading, focusDoneToggle, focusPickerTrigger, focusToggle, focusPicker, bindActions,
    focusAddInput, focusDoneStripToggle, focusRenameTrigger, focusItem,
    focusEditToggle, focusTitleTrigger, focusItemAction, toggleEditMode };
}
