import { getPrefs, setPref, getTheme, setTheme as storeTheme } from "../prefs.js";

// Wraps the stored prefs in the house subscribe/notify shape. Synchronous — localStorage.
// The theme lives under its own storage key (see prefs.js) but joins the same state object, so
// the view renders one settings screen rather than reading two sources.
export function createSettingsModel() {
  const subscribers = [];
  const read = () => ({ ...getPrefs(), theme: getTheme() });
  const notify = () => subscribers.forEach((fn) => fn(read()));
  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(read());
    },
    set(key, value) {
      setPref(key, value);
      notify();
    },
    setTheme(theme) {
      // Aliased on import: a bare setTheme() inside a method of the same name resolves to the
      // import, which is right and reads like a bug.
      storeTheme(theme);
      notify();
    },
  };
}
