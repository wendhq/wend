import { escapeHtml } from "../escape.js";
import { renderLabels } from "./labels.js";

// Task view for one card: back link, heading, the Labels section + picker, the edit form, and
// delete. Holds only transient picker UI state; all data comes from the model. The view owns the
// purely-visual picker transitions (open / create / edit); the controller owns anything that
// touches the server. Events via data-action.
export function createCardView(root) {
  let lastCard = null;
  let lastPalette = [];
  const ui = { pickerOpen: false, mode: "list", editingId: null };
  let h = {};

  function render(card, palette) {
    lastCard = card;
    lastPalette = palette ?? [];
    paint();
  }

  function paint() {
    const card = lastCard;
    root.innerHTML = `
      <div class="card-view">
        <button class="back-link" data-action="back">← Board</button>
        <h2 class="card-heading" tabindex="-1">${escapeHtml(card.title)}</h2>
        <p class="card-list-name">In list: <strong>${escapeHtml(card.listTitle)}</strong></p>
        <div class="card-done">
          <label class="card-done-label">
            <input type="checkbox" data-action="toggle-done" ${card.completedAt ? "checked" : ""} />
            <span>Done</span>
          </label>
        </div>
        ${renderLabels(card, lastPalette, ui)}
        <form class="card-detail" data-action="save">
          <label class="field">
            <span>Title</span>
            <input name="title" value="${escapeHtml(card.title)}" aria-label="Card title" required />
          </label>
          <label class="field">
            <span>Due date</span>
            <input name="dueDate" type="date" value="${card.dueDate ?? ""}" aria-label="Due date" />
          </label>
          <label class="field">
            <span>Notes</span>
            <textarea name="description" aria-label="Notes">${escapeHtml(card.description ?? "")}</textarea>
          </label>
          <button type="submit">Save changes</button>
        </form>
        <button class="card-delete" data-action="delete">Delete card</button>
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
          await h.save({ title: f.title.value.trim(), description: f.description.value, dueDate: f.dueDate.value || null });
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
      }
    });

    root.addEventListener("change", async (e) => {
      const done = e.target.closest('input[data-action="toggle-done"]');
      if (done) return h.toggleDone(done.checked);
      const cb = e.target.closest('input[data-action="toggle-label"]');
      if (!cb) return;
      const id = Number(cb.dataset.labelId);
      if (cb.checked) await h.attachLabel(id);
      else await h.detachLabel(id);
    });

    root.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && ui.pickerOpen) { e.stopPropagation(); closePicker(); }
    });

    root.addEventListener("click", (e) => {
      const btn = e.target.closest("[data-action]");
      if (!btn) return;
      const a = btn.dataset.action;
      if (["save", "add-label", "save-label", "toggle-label"].includes(a)) return; // handled by submit/change
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

  return { render, focusHeading, focusPickerTrigger, focusToggle, focusPicker, bindActions };
}
