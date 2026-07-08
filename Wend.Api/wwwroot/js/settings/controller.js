// Wires the settings view: flips a pref and announces the result.
const NAMES = {
  showCardDone: "Card Done checkboxes",
  alwaysShowDeleteCard: "Always show Delete card",
};

export function createSettingsController(model, view, announce, { onBack } = {}) {
  view.bindActions({
    back: () => onBack?.(),
    toggle: (key, value) => {
      model.set(key, value);   // notify → view.render rebuilds the checkbox
      view.focusPref(key);      // return focus to the flipped checkbox
      announce(`${NAMES[key] ?? "Setting"} ${value ? "on" : "off"}.`);
    },
  });
  model.subscribe((prefs) => view.render(prefs));
}
