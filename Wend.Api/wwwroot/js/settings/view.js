// Renders the Settings screen: back link, heading, two labelled native toggles.
// No logic; events via data-action.
export function createSettingsView(root) {
  let h = {};

  function render(prefs) {
    root.innerHTML = `
      <div class="settings-view">
        <button class="back-link" data-action="back">← Boards</button>
        <h2 class="settings-heading" tabindex="-1">Settings</h2>
        <label class="setting-row">
          <input type="checkbox" data-action="toggle-pref" data-pref="showCardDone"
            ${prefs.showCardDone ? "checked" : ""} />
          <span>Show card Done checkboxes</span>
        </label>
        <p class="setting-hint">Adds a done checkbox to every card, so cards can be tucked into the board's Done area.</p>
        <label class="setting-row">
          <input type="checkbox" data-action="toggle-pref" data-pref="alwaysShowDeleteCard"
            ${prefs.alwaysShowDeleteCard ? "checked" : ""} />
          <span>Always show the Delete card button</span>
        </label>
        <p class="setting-hint">Otherwise Delete card only appears in a card's Edit mode.</p>
      </div>`;
  }

  function focusHeading() { root.querySelector(".settings-heading")?.focus(); }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-action="back"]')) h.back();
    });
    root.addEventListener("change", (e) => {
      const cb = e.target.closest('input[data-action="toggle-pref"]');
      if (cb) h.toggle(cb.dataset.pref, cb.checked);
    });
  }

  return { render, focusHeading, bindActions };
}
