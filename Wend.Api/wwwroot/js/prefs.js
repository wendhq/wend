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

// Theme. Stored under the design system's own "theme" key rather than inside wend.prefs, because
// the inline script in index.html reads it before any module loads - that script is what stops a
// dark-mode user seeing a flash of light on first paint, and it cannot wait for an import.
// Unknown or hand-edited values fall back to dark, which is this project's default.
const THEME_KEY = "theme";
const THEMES = ["dark", "light"];

export function getTheme() {
  const stored = localStorage.getItem(THEME_KEY);
  return THEMES.includes(stored) ? stored : "dark";
}

export function setTheme(theme) {
  if (!THEMES.includes(theme)) return; // unknown value -> ignore, same as setPref
  localStorage.setItem(THEME_KEY, theme);
}

// Remembered "currently viewed" list per board (mobile single-list switcher). Stored as a
// { boardId: listId } map under its own key. Reads validate to a number; anything else → null,
// so the caller falls back to the first list.
const SELECTION_KEY = "wend.board.selection";

export function getSelectedListId(boardId) {
  let map = null;
  try {
    map = JSON.parse(localStorage.getItem(SELECTION_KEY) ?? "null");
  } catch {
    // corrupted value → treat as no selection
  }
  const v = map && typeof map === "object" ? map[boardId] : null;
  return typeof v === "number" ? v : null;
}

export function setSelectedListId(boardId, listId) {
  let map = null;
  try {
    map = JSON.parse(localStorage.getItem(SELECTION_KEY) ?? "null");
  } catch {
    // corrupted value → start fresh
  }
  if (!map || typeof map !== "object") map = {};
  map[boardId] = listId;
  localStorage.setItem(SELECTION_KEY, JSON.stringify(map));
}
