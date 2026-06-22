import { escapeHtml } from "../escape.js";

// Renders the task view for one card: back link, heading, an edit form (title, due date, notes),
// Save, and Delete. Forwards events via data-action. No fetch, no logic.
export function createCardView(root) {
    function render(card) {
        root.innerHTML = `
      <div class="card-view">
        <button class="back-link" data-action="back">← Board</button>
        <h2 class="card-heading" tabindex="-1">${escapeHtml(card.title)}</h2>
        <p class="card-list-name">In list: <strong>${escapeHtml(card.listTitle)}</strong></p>
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

    function focusHeading() {
        root.querySelector(".card-heading")?.focus();
    }

    function bindActions(handlers) {
        root.addEventListener("submit", async (e) => {
            if (e.target.dataset.action !== "save") return;
            e.preventDefault();
            const form = e.target;
            const title = form.title.value.trim();
            const description = form.description.value;
            const dueDate = form.dueDate.value || null; // "" → null
            const submit = form.querySelector("button[type=submit]");
            submit.disabled = true;
            try {
                await handlers.save({ title, description, dueDate });
            } finally {
                submit.disabled = false;
            }
        });
        root.addEventListener("click", (e) => {
            const btn = e.target.closest("button[data-action]");
            if (!btn || btn.dataset.action === "save") return;
            if (btn.dataset.action === "back") return handlers.back();
            if (btn.dataset.action === "delete") return handlers.delete();
        });
    }

    return { render, focusHeading, bindActions };
}