import { getPrefs, setPref } from "../prefs.js";

// Wraps the stored prefs in the house subscribe/notify shape. Synchronous — localStorage.
export function createSettingsModel() {
  const subscribers = [];
  const notify = () => subscribers.forEach((fn) => fn(getPrefs()));
  return {
    subscribe(fn) {
      subscribers.push(fn);
      fn(getPrefs());
    },
    set(key, value) {
      setPref(key, value);
      notify();
    },
  };
}
