// Client-only preferences in localStorage. Reads pick the known keys explicitly with type
// checks — never spread parsed JSON (localStorage is hand-editable). Parse failure → defaults.
const KEY = "wend.prefs";

export function getPrefs() {
  let parsed = null;
  try {
    parsed = JSON.parse(localStorage.getItem(KEY) ?? "null");
  } catch {
    // corrupted value → fall through to defaults
  }
  return {
    showCardDone: parsed?.showCardDone === true,
    alwaysShowDeleteCard: parsed?.alwaysShowDeleteCard === true,
  };
}

export function setPref(key, value) {
  const prefs = getPrefs();
  if (!(key in prefs)) return; // unknown key → ignore
  localStorage.setItem(KEY, JSON.stringify({ ...prefs, [key]: value === true }));
}
