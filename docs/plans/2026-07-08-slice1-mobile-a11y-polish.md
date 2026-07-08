# Plan 8 — Mobile + A11y Polish Implementation Plan

> **Build mode — Claude solo** (2026-07-07 delegation split). Claude drives every task, commits per task **as Malin** (no AI attribution), and self-verifies each in a real browser via the Chrome extension (the same sweep that found these). Malin & Henry review the PR and run the **keyboard + screen-reader acceptance** before merge. No coached turn-based handoff this plan.
>
> **Verification is browser-first.** Every task ends with a concrete in-browser check (measure the size, read the contrast, inspect `activeElement`, read the a11y tree). **Hard-reload / disable cache before each check** — `UseStaticFiles` serves stale JS otherwise (it bit the sweep on 2026-07-08).

**Goal:** Ship the final Slice-1 increment — the ten accessibility findings from the 2026-07-08 live sweep (Phase 1, ready now) plus the mobile single-list switcher and per-list Done strip (Phase 2, after the shared brainstorm).

**Architecture:** Frontend-only. No backend, no API, no schema, no NUnit change — every fix lives in `Wend.Api/wwwroot/` (CSS + the vanilla-JS MVC views/controllers). Fixes follow patterns already in the codebase (the card/checklist inline-rename, the label-toggle refocus contract, the escape-at-every-`innerHTML` rule).

**Tech Stack:** vanilla-JS MVC (no build step) · CSS (mobile-first, design-system tokens) · ASP.NET Core static hosting (untouched).

**Spec:** [`docs/2026-07-08-wend-mobile-a11y-polish-design.md`](../2026-07-08-wend-mobile-a11y-polish-design.md) (stress-tested; 8 fixes folded).

**Test count:** stays **147 green, 0 warnings** throughout — backend is untouched. Run `dotnet test` once at the end to confirm nothing regressed. (Stop `Wend.Api` before the test run — the DLL lock, not a test failure.)

---

## Before Task 1 — branch + docs commit

```powershell
git switch main; git pull
git switch -c feature/mobile-a11y-polish
git add docs/2026-07-08-wend-mobile-a11y-polish-design.md docs/plans/2026-07-08-slice1-mobile-a11y-polish.md
git commit -m "Plan 8 docs — mobile + a11y polish spec + build plan"
git push -u origin feature/mobile-a11y-polish
```

No `data.db` reset this plan (frontend-only). Keep the seeded sweep board around — it exercises every finding.

---

## Phase 1 — a11y findings (ready to build)

### Task 1 — CSS: target sizes (F2), Done-strip contrast (F3), confirm string (F7)

**Files:** `css/app.css`, `js/card/checklist.js`, `js/card/controller.js`

- **F2** — grow the two sub-24px checkboxes to a ≥24px box (rows are already 44px tall):
  ```css
  .card-done-toggle { width: 1.5rem; height: 1.5rem; }               /* was 1.15rem (18px) */
  .checklist-row input[type="checkbox"],
  .done-item-row input[type="checkbox"] { width: 1.5rem; height: 1.5rem; }  /* was 13px default */
  ```
- **F3** — rename **only** the checklist strip's colliding class in `checklist.js` (`class="done-row-label"` → `class="checklist-done-label"`) and add a matching CSS rule; **delete** the `.done-row-label` opacity from the strip's chain and dim with a **single** step measured ≥4.5:1 (mirror the board area's `0.7`, proven 5.36:1). Leave `board/view.js` + `app.css:314`'s `.done-row-label` untouched.
- **F7** — collapse the label-delete `confirm()` string in `card/controller.js` to one line.

**Verify (browser):** re-measure — both checkboxes report ≥24px; the Done-strip text composites to ≥4.5:1 against `#0D1117`; the board Done area still measures 5.36:1 (unbroken); the confirm dialog reads cleanly.

**Commit:** `Plan 8 Task 1 — a11y CSS: 24px targets, Done-strip contrast, confirm string`

---

### Task 2 — Focus management (F1, F10)

**Files:** `js/board/view.js`, `js/settings/view.js`, `js/settings/controller.js`

- **F1** — in the `toggle-done-section` click branch, call `focusDoneToggle()` after `paint()` (helper exists).
- **F10** — add a `focusPref(key)` helper to the settings view (`root.querySelector(\`input[data-pref="${key}"]\`)?.focus()`) and call it from the controller's `toggle` handler after `model.set`.
- **Both** — if the refocus target is ever absent, fall back to the screen heading rather than letting `?.focus()` drop to `<body>`.

**Verify (browser):** after toggling the board Done area and after flipping each Settings checkbox, `document.activeElement` is the toggle/checkbox (not `BODY`) — the exact check that caught F1/F10.

**Commit:** `Plan 8 Task 2 — focus stays on the toggle after Done-area / settings re-render`

---

### Task 3 — Structure & semantics (F6, F8)

**Files:** `js/board/view.js`, `js/card/view.js`, `css/app.css`

- **F6** — each list becomes a labelled region with the heading as its label (one announcement, both landmark + heading nav):
  ```html
  <li class="list-card" data-list-id="${l.id}" role="group" aria-labelledby="list-${l.id}-title">
    <h3 id="list-${l.id}-title" class="list-title">${escapeHtml(l.title)}</h3>
    …
  ```
  (If a screen reader objects to `role="group"` on an `<li>` inside `<ul>`, wrap the content in a `<section>` instead — decide at build from the a11y tree.)
- **F8** — restructure the card heading so the `<h2>` **keeps its name** and the rename form is a sibling: normal mode → `<h2>` holds the rename-trigger button; rename mode → `<h2>` holds the title as text, with the `<form>` rendered *after* the `</h2>`, never inside it.

**Verify (browser):** the a11y tree shows each list as a `group`/`region` with its title; heading-jump reaches every list. While renaming a card title, no `<form>` is a descendant of the `<h2>` and the heading still exposes the title as its name.

**Commit:** `Plan 8 Task 3 — lists as labelled regions; card rename form out of the h2`

---

### Task 4 — ARIA & announcements (F9)

**Files:** `js/card/labels.js`, `js/settings/view.js`, `js/announce.js`

- `aria-haspopup="true"` → `"dialog"` on the Labels toggle (or drop it — `aria-expanded` already conveys the disclosure).
- Give each `.setting-hint` an `id` and each Settings checkbox `aria-describedby` pointing at it.
- `announce.js`: swap the rAF for a short `setTimeout` so the message fires regardless of tab visibility:
  ```js
  return (message) => {
    region.textContent = "";
    setTimeout(() => { region.textContent = message; }, 120);
  };
  ```

**Verify (browser):** picker toggle reports `aria-haspopup="dialog"`; each settings checkbox has `aria-describedby` resolving to its hint; `#status` receives the message without relying on rAF (the hidden-tab drop no longer happens).

**Commit:** `Plan 8 Task 4 — picker haspopup, settings describedby, reliable announcer`

---

### Task 5 — Toast timing (F4) — *after Task 4*

**Files:** `js/toast.js`

- Lengthen the auto-dismiss window (`TIMEOUT_MS` 8000 → **12000**), keeping the pause-on-hover/focus. (Alternative considered: don't start the timer until first interaction — heavier, and a mouse user who ignores it never sees it dismiss. Bump-and-pause is the pragmatic call; revisit if acceptance says it's still tight.)
- Depends on Task 4: the reliable "Undo available" announcement is what a screen-reader user hears first.

**Verify (browser):** delete a card, confirm the window is ≥12s and still pauses while focus is inside; the Undo→restore round-trip still lands focus on the restored chip.

**Commit:** `Plan 8 Task 5 — longer undo-toast window for AT users`

---

### Task 6 — Inline rename for boards & lists (F5)

**Files:** `js/boards/view.js`, `js/boards/controller.js`, `js/board/view.js`, `js/board/controller.js`

Replace the two `prompt()` calls with the card/checklist inline-rename pattern. Per trio:

- View gains `ui = { renamingId: null }` (survives `paint()`); the title renders as a rename-trigger button (`aria-label="Rename: ${escapeHtml(title)}"`), swapping to a `<form>` with `<input value="${escapeHtml(title)}">` when `renamingId` matches.
- Enter/Save commits (→ `model.rename`), Esc cancels and returns focus to the trigger, **blur leaves it open** (matches cards — no silent auto-commit).
- `escapeHtml` at both the input `value` and the trigger `aria-label` (new interpolation sites).
- Delete the `prompt()` branches in both controllers.

**Verify (browser):** rename a board and a list inline — text→input swap, Enter saves, Esc cancels with focus back on the trigger; a title containing `"` / `<` round-trips without breaking markup (escape check).

**Commit:** `Plan 8 Task 6 — inline rename for boards and lists (replaces prompt)`

---

## Phase 2 — mobile layout (after the shared brainstorm)

Design-pending — do **not** start these until the switcher decisions in the spec's "Mobile layout" open questions are settled with Henry.

### Task 7 — Per-list Done strip
Each list gains a collapsible `Done (n)` disclosure of *its* completed cards, mirroring the checklist Done strip. Depends on Task 3 (list regions). *Verify:* per-list strip expands/collapses, focus stays on its toggle (F1 lesson), contrast ≥4.5:1 (F3 lesson).

### Task 8 — Single-list switcher
Narrow-screen (`< 768px`) single-list view + the switcher control chosen in the brainstorm; desktop columns unchanged (mobile-first). *Verify:* one list shown on mobile, switching announces the new list, keyboard-operable; resize to ≥768px restores columns.

### Task 9 — Acceptance + README + backlog
Full-suite `dotnet test` (147, backend untouched); README Slice-1 status → Plan 8 done / Slice 1 complete; note in `backlog.md` that the mobile layout shipped. Malin & Henry run the final keyboard/SR acceptance.

**Merge:** single-author branch → squash-merge is clean (the merge-not-squash rule only bites when Henry has commits on the branch). Delete `feature/mobile-a11y-polish`; confirm the remote branch actually went (the checklist merge silently didn't).

---

## Gotchas carried forward

- **Hard-reload / disable cache before every browser check** — stale static JS (no `Cache-Control`) served the sweep old modules on 2026-07-08.
- Stop **`Wend.Api`** (not `Wend`) before `dotnet build`/`test`.
- No `data.db` reset — frontend-only, no schema change.
- Phase 2 is blocked on the mobile brainstorm; Phase 1 is not.
