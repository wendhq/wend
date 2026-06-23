import { escapeHtml } from "../escape.js";

export const LABEL_COLOURS = ["mint", "cyan", "amber", "rose", "lilac", "slate"];

// Returns the Labels-section HTML from (card, palette, ui). Pure — no state, no fetch.
// ui = { pickerOpen, mode: "list" | "create" | "edit", editingId }.
export function renderLabels(card, palette, ui) {
  const attached = card.labels ?? [];
  const attachedChips = attached.length
    ? attached.map((l) => `<span class="label-chip label-chip--${l.colour}">${escapeHtml(l.name)}</span>`).join("")
    : `<span class="labels-empty">No labels yet</span>`;

  let body = "";
  if (ui.pickerOpen) {
    if (ui.mode === "create" || ui.mode === "edit") {
      body = labelForm(ui, palette);
    } else {
      const rows = palette.length
        ? palette
            .map((l) => {
              const on = attached.some((a) => a.id === l.id);
              return `
            <li class="label-row">
              <label class="label-toggle">
                <input type="checkbox" data-action="toggle-label" data-label-id="${l.id}" ${on ? "checked" : ""} />
                <span class="label-chip label-chip--${l.colour}">${escapeHtml(l.name)}</span>
              </label>
              <span class="label-row-actions">
                <button type="button" data-action="edit-label" data-label-id="${l.id}"
                  aria-label="Edit label ${escapeHtml(l.name)}">Edit</button>
                <button type="button" data-action="delete-label" data-label-id="${l.id}"
                  aria-label="Delete label ${escapeHtml(l.name)}">Delete</button>
              </span>
            </li>`;
            })
            .join("")
        : `<li class="labels-empty">No labels on this board yet — create one.</li>`;
      body = `
        <ul class="label-list">${rows}</ul>
        <button type="button" class="label-create-open" data-action="create-label-open">＋ Create a new label</button>`;
    }
  }

  return `
    <section class="labels-section" aria-label="Labels">
      <div class="labels-attached">${attachedChips}</div>
      <button type="button" class="labels-toggle" data-action="toggle-picker"
        aria-haspopup="true" aria-expanded="${ui.pickerOpen ? "true" : "false"}">＋ Labels</button>
      ${ui.pickerOpen ? `<div class="label-picker" role="group" aria-label="Choose labels">${body}</div>` : ""}
    </section>`;
}

// The create / edit form (name + six colour swatches), prefilled when editing.
function labelForm(ui, palette) {
  const editing = ui.mode === "edit" ? palette.find((l) => l.id === ui.editingId) : null;
  const name = editing ? editing.name : "";
  const chosen = editing ? editing.colour : LABEL_COLOURS[0];
  const swatches = LABEL_COLOURS
    .map(
      (key) => `
      <label class="swatch">
        <input type="radio" name="colour" value="${key}" ${key === chosen ? "checked" : ""} />
        <span class="swatch-dot label-chip--${key}" aria-hidden="true"></span>
        <span class="swatch-name">${key}</span>
      </label>`
    )
    .join("");
  return `
    <form class="label-form" data-action="${editing ? "save-label" : "add-label"}" ${editing ? `data-label-id="${editing.id}"` : ""}>
      <label class="field">
        <span>Label name</span>
        <input name="name" value="${escapeHtml(name)}" aria-label="Label name" maxlength="50" required />
      </label>
      <fieldset class="swatches">
        <legend>Colour</legend>
        ${swatches}
      </fieldset>
      <div class="label-form-actions">
        <button type="submit">${editing ? "Save label" : "Add label"}</button>
        <button type="button" data-action="cancel-label">Cancel</button>
      </div>
    </form>`;
}
