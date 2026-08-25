// Renders the Settings screen: back link, heading, a native theme select and two labelled
// native toggles. No logic; events via data-action.
export function createSettingsView(root) {
  let h = {};

  function render(prefs) {
    root.innerHTML = `
      <div class="settings-view">
        <button class="back-link" data-action="back">← Boards</button>
        <h2 class="settings-heading" tabindex="-1">Settings</h2>

        <!-- A native <select> rather than a pair of radios or a fancy switch: two options today,
             more when a palette picker lands, and .select carries the 44px floor. -->
        <div class="setting-row">
          <label class="setting-label" for="setting-theme">Theme</label>
          <select class="select setting-select" id="setting-theme" data-action="set-theme"
            aria-describedby="hint-theme">
            <option value="dark"${prefs.theme === "light" ? "" : " selected"}>Dark</option>
            <option value="light"${prefs.theme === "light" ? " selected" : ""}>Light</option>
          </select>
        </div>
        <p class="setting-hint" id="hint-theme">Wend is dark by default. Light keeps the same brand colours on a light ground. Your choice is remembered on this device.</p>

        <label class="setting-row">
          <input type="checkbox" data-action="toggle-pref" data-pref="showCardDone"
            aria-describedby="hint-showCardDone" ${prefs.showCardDone ? "checked" : ""} />
          <span>Show card Done checkboxes</span>
        </label>
        <p class="setting-hint" id="hint-showCardDone">Adds a done checkbox to every card, so cards can be tucked into the board's Done area.</p>
        <label class="setting-row">
          <input type="checkbox" data-action="toggle-pref" data-pref="alwaysShowDeleteCard"
            aria-describedby="hint-alwaysShowDeleteCard" ${prefs.alwaysShowDeleteCard ? "checked" : ""} />
          <span>Always show the Delete card button</span>
        </label>
        <p class="setting-hint" id="hint-alwaysShowDeleteCard">Otherwise Delete card only appears in a card's Edit mode.</p>
      </div>`;
  }

  function focusHeading() { root.querySelector(".settings-heading")?.focus(); }

  function focusTheme() { root.querySelector("#setting-theme")?.focus(); }

  function focusPref(key) {
    const cb = root.querySelector(`input[data-pref="${key}"]`);
    if (cb) cb.focus();
    else focusHeading(); // fallback if the checkbox is somehow absent
  }

  function bindActions(handlers) {
    h = handlers;
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-action="back"]')) h.back();
    });
    root.addEventListener("change", (e) => {
      const cb = e.target.closest('input[data-action="toggle-pref"]');
      if (cb) h.toggle(cb.dataset.pref, cb.checked);
      const theme = e.target.closest('select[data-action="set-theme"]');
      if (theme) h.setTheme(theme.value);
    });
  }

  return { render, focusHeading, focusPref, focusTheme, bindActions };
}
