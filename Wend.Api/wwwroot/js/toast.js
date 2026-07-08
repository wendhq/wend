// A single transient toast in the shell's #toast-region: a message + one action (Undo) + a
// dismiss button. Auto-dismisses after a timeout that PAUSES while the pointer is over it or
// keyboard focus is inside it, so it can't vanish before a keyboard / screen-reader user
// reaches the action. A new show() replaces the current toast. No business logic — the caller
// supplies the message and the callbacks.
const TIMEOUT_MS = 12000; // long enough for a keyboard / screen-reader user to reach Undo (the only restore path until Trash)

export function createToast(region) {
  let current = null; // { el, remaining, startedAt, timerId }

  function clearTimer() {
    if (current?.timerId) { clearTimeout(current.timerId); current.timerId = null; }
  }
  function startTimer(ms) {
    clearTimer();
    current.startedAt = Date.now();
    current.remaining = ms;
    current.timerId = setTimeout(dismiss, ms);
  }
  function pause() {
    if (!current?.timerId) return;
    clearTimer();
    current.remaining -= Date.now() - current.startedAt; // bank the elapsed time
  }
  function resume() {
    if (!current || current.timerId) return;
    startTimer(Math.max(current.remaining, 0));
  }
  function dismiss() {
    if (!current) return;
    clearTimer();
    current.el.remove();
    current = null;
  }

  function show({ message, actionLabel, onAction, onDismissFocus, ariaLabel = "Deleted card" }) {
    dismiss(); // one toast at a time — replace any current one

    const el = document.createElement("div");
    el.className = "toast toast-info";
    el.setAttribute("role", "group");
    el.setAttribute("aria-label", ariaLabel);

    const text = document.createElement("span");
    text.className = "toast-message";
    text.textContent = message; // user title → textContent, never innerHTML

    const action = document.createElement("button");
    action.type = "button";
    action.className = "toast-action";
    action.textContent = actionLabel;
    action.addEventListener("click", () => { dismiss(); onAction?.(); });

    const close = document.createElement("button");
    close.type = "button";
    close.className = "toast-dismiss";
    close.setAttribute("aria-label", "Dismiss");
    close.textContent = "×";
    close.addEventListener("click", () => { dismiss(); onDismissFocus?.(); });

    el.append(text, action, close);
    el.addEventListener("mouseenter", pause);
    el.addEventListener("mouseleave", resume);
    el.addEventListener("focusin", pause);
    el.addEventListener("focusout", (e) => { if (!el.contains(e.relatedTarget)) resume(); });

    region.append(el);
    current = { el, remaining: TIMEOUT_MS, startedAt: 0, timerId: null };
    startTimer(TIMEOUT_MS);
  }

  return { show, dismiss };
}
