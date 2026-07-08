# Wend — Plan 8: mobile + a11y polish design spec

- **Date:** 2026-07-08
- **Status:** Phase 1 (a11y) built & merged 2026-07-08 (PR #25). Phase 2 (mobile) design brainstormed & **locked with Malin 2026-07-08** — the six open questions below are decided; the global Done area is retired (revises Slice-1 "Done = both"), pending **Henry's review**.
- **Owners:** Malin & Henry
- **Builds on:** [Slice 1 design](2026-06-15-wend-slice1-design.md) — the final Slice-1 increment
- **Build mode:** **Claude solo** (per the 2026-07-07 delegation split — big plans go to Claude; the spec, review, and keyboard/screen-reader acceptance stay shared with Henry)

---

## Context

Plan 8 is the last Slice-1 increment: mobile layout + accessibility polish. It is fed by two things.

1. **The first live accessibility sweep** (browser-driven, 2026-07-08). Every prior increment was verified headlessly — backend live + node-rendered HTML — and the checklist increment shipped without an interactive pass at all. This session drove the *running* app in a real browser (the Claude Chrome extension) against a seeded board and **measured** ten findings — pixel sizes, contrast ratios, focus destinations, ARIA — rather than inferring them. Those are the a11y half of this plan, and each carries its measured evidence below.
2. **Two mobile features carried over** from Plan 6: the **single-list switcher** and the **per-list Done strip**, both deferred because they assume a mobile single-list focus that didn't exist yet.

The a11y findings are ready to build now. The mobile layout still wants the shared brainstorm — it is scoped here with a proposed approach and open questions, flagged for that session, not decided solo.

> **A caught pitfall, recorded once:** the sweep first ran against **stale cached JS** the browser was serving from earlier dev sessions on `127.0.0.1:5174` — two "bugs" were cache ghosts. `UseStaticFiles` sends no `Cache-Control`, so a normal reload doesn't revalidate module imports. Every verification (this plan's and the human acceptance) must **hard-reload / disable cache** first. See Gotchas.

## Scope

**In — a11y findings (verified, ready to build):**

- **F1** Board Done-area toggle drops keyboard focus on expand/collapse.
- **F2** Sub-24px checkboxes: checklist item (13×13px) and board Done toggle (18×18px).
- **F3** Checklist Done-strip text fails contrast (2.68:1) via a CSS class collision.
- **F4** Delete-undo toast auto-dismisses in 8s and is the only restore path.
- **F5** Board/list rename use native `prompt()` while everything else renames inline.
- **F6** List columns aren't navigable by heading or landmark.
- **F7** Malformed label-delete `confirm()` string (a stray newline + indentation).
- **F8** Card-title rename nests a `<form>` inside the `<h2>`.
- **F9** Picker `aria-haspopup="true"` (menu semantics for a checkbox group); Settings hints not linked via `aria-describedby`; `announce()`'s rAF drops messages in hidden/throttled tabs.
- **F10** Settings toggle drops keyboard focus on re-render (same family as F1).

**In — mobile layout (decided 2026-07-08 — brainstormed & locked with Malin):**

- Single-list switcher on narrow screens — native `<select>`, one list at a time, selection remembered per board.
- Per-list Done strip (each list's completed cards, mirroring the checklist Done strip).
- **Retire the global Done area** — replaced by the per-list strips (revises Slice-1 "Done = both"; see the Mobile layout § below). Frontend-only, no data change.

**Out of scope:**

- Label chips-vs-bars display toggle — its Settings home exists now, but it's a distinct feature; stays in [`backlog.md`](backlog.md).
- Multi-card batch undo, Trash/Archive screens — later slices.
- **Any backend / API / schema / test change.** Plan 8 is **frontend-only**: no `data.db` reset, no new endpoints, no NUnit changes — the suite stays at **147 green**. Verification is in-browser (now automatable via the sweep) plus human screen-reader acceptance.

## Findings & fixes (grouped by theme)

Every measurement below was read off the live app on 2026-07-08 against a seeded board (page background `#0D1117`).

### A. Focus management — F1, F10

Both drop focus to `<body>`: the board's global Done-area toggle ([board/view.js:205](../Wend.Api/wwwroot/js/board/view.js:205)) and every Settings toggle ([settings/controller.js:10](../Wend.Api/wwwroot/js/settings/controller.js:10) → `model.set` → `view.render`). The root cause is the house pattern — a full `innerHTML` repaint — without the matching refocus that every *other* control already does (the checklist Done-strip toggle refocuses itself at [card/view.js:207](../Wend.Api/wwwroot/js/card/view.js:207); that's the reference).

- **F1 fix:** in the `toggle-done-section` branch, call `view.focusDoneToggle()` after `paint()` — the helper already exists at [board/view.js:156](../Wend.Api/wwwroot/js/board/view.js:156).
- **F10 fix:** after a Settings toggle re-renders, return focus to the checkbox that was flipped. Add a `focusPref(key)` helper to the settings view and call it from the controller's `toggle` handler (mirrors the label-toggle refocus contract).
- **Both:** the refocus helpers use `?.focus()`, which silently drops to `<body>` if the target is ever absent. The normal path is safe (the toggle/checkbox persists), but specify a stable fallback (the screen's heading) so a future edge case can't strand focus.

### B. Target size — F2 (WCAG 2.5.8, AA)

Measured **13×13px** for checklist item checkboxes ([checklist.js:25](../Wend.Api/wwwroot/js/card/checklist.js:25)) — both active and Done-strip — and **18×18px** for the board Done toggle (`.card-done-toggle`, [board/view.js:55](../Wend.Api/wwwroot/js/board/view.js:55)). Both are below the 24×24 CSS-px minimum, and both are the controls a thumb hits most on mobile.

- **Fix (CSS only, [app.css](../Wend.Api/wwwroot/css/app.css)):** give both a ≥24px box and a ≥44px interactive row/hit area. The checklist checkbox **can't** be label-wrapped to borrow a larger target — its adjacent text is a rename `<button>`, not a label — so grow the input itself (`width/height` + padding on `.checklist-row`). The board `.card-done-toggle` grows from `1.15rem` likewise.

### C. Contrast — F3 (WCAG 1.4.3, AA)

Checklist Done-strip text composites to **2.68:1** at **0.42 effective opacity** — a clear fail. Root cause: `.done-row-label` is defined **twice** ([app.css:314](../Wend.Api/wwwroot/css/app.css:314) for the board Done area, [app.css:405](../Wend.Api/wwwroot/css/app.css:405) for the checklist strip), so the strip label keeps the first rule's `opacity: 0.7` **inside** `.done-item-row { opacity: 0.6 }` ([app.css:403](../Wend.Api/wwwroot/css/app.css:403)) → 0.42 stacked. For contrast, the board Done area is **fine** (0.7 → 5.36:1 measured) — so this is scoped precisely to the strip's doubled opacity.

- **Fix:** rename **only the checklist strip's** `.done-row-label` — in **both** the CSS rule ([app.css:405](../Wend.Api/wwwroot/css/app.css:405)) **and** its emitter ([checklist.js:46](../Wend.Api/wwwroot/js/card/checklist.js:46)); both `checklist.js` and `board/view.js` emit `.done-row-label`, so a CSS-only rename leaves the strip unstyled and renaming the *shared* class breaks the board area. Leave the board Done area's `.done-row-label` ([app.css:314](../Wend.Api/wwwroot/css/app.css:314)) **untouched** — it's fine at 5.36:1. Stop stacking opacity, and dim the strip text to a **measured ≥4.5:1** — match the board area's single `0.7` step (proven 5.36:1) or a fixed dim colour; **re-measure, don't assume `0.6` passes** (`0.6` alone composites to roughly the AA borderline). Keep the line-through (redundant, not sole, signal).

### D. Structure & semantics — F6, F8

- **F6:** the board title is a `heading`, but the three list columns serialise as `generic` text (`.list-title` is a `<span>`, [board/view.js:82](../Wend.Api/wwwroot/js/board/view.js:82)). The a11y tree confirmed only "Sprint Board" (`h2`) and — ironically — the *Done* area (`region`) are reachable; a screen-reader heading-jump skips every working column. **Fix:** wrap each list in `<section role="group" aria-labelledby="list-{id}-title">` with an `<h3 id="list-{id}-title" class="list-title">{title}</h3>` — the heading *labels* the region, so a screen reader announces it **once** (not a separate `aria-label` **and** a heading, which would double-announce). This is the structural hook the mobile switcher selects on, so it lands first.
- **F8:** renaming the card title nests a `<form>` inside the `<h2>` ([card/view.js:32](../Wend.Api/wwwroot/js/card/view.js:32)) — invalid content model (a heading takes phrasing content), confirmed live. **Fix:** render the rename form as a **sibling** after the heading, not inside it — but the `<h2>` must **never go nameless**: in normal mode it holds the rename *trigger* button (its text = the title), and during rename it keeps the title as static text (or the trigger) with the input/form as the following sibling.

### E. Native dialogs → inline — F5, F7

- **F5:** board rename ([boards/controller.js:17](../Wend.Api/wwwroot/js/boards/controller.js:17)) and list rename ([board/controller.js:31](../Wend.Api/wwwroot/js/board/controller.js:31)) call `prompt()`, while card titles and checklist items rename **inline** (text-as-button → input swap, Enter saves, Esc cancels, focus returns). `prompt()` is jarring, mobile-hostile, and inconsistent. **Fix:** give boards and lists the same inline rename pattern; delete the `prompt()` calls. Match the card/checklist contract exactly: rename state lives in the **view** and survives a `paint()`; the `<input value="…">` and rename-trigger `aria-label` are **`escapeHtml`-escaped** (new interpolation sites); Enter/Save commits, Esc cancels and returns focus to the trigger; blur = **leave-open** (matches cards — no silent auto-commit). The largest single fix (new view state + handlers on two trios), but a direct copy of a pattern already in the codebase.
- **F7:** the label-delete `confirm()` template literal spans two source lines, rendering `Delete\n            'name'?…` ([card/controller.js:146](../Wend.Api/wwwroot/js/card/controller.js:146)). **Fix:** collapse to one line. (Trivial; folds in wherever we touch that file. Native `confirm()` for the destructive board/list/label deletes stays — it's accessible; only the string is broken.)

### F. Toast timing — F4 (WCAG 2.2.1)

The toast itself is well-built (measured live: `role="group"`, correct message, Undo + "Dismiss", sits **before `#app`** so it's one Shift+Tab from the board heading, focus lands on the heading after delete and on the restored chip after undo — the restore round-trip works). The risk is timing: **8s auto-dismiss** ([toast.js:6](../Wend.Api/wwwroot/js/toast.js:6)), pausing only once focus/hover enters, and it's currently the **only** restore path (Trash is a later slice). A keyboard/screen-reader user racing to the Undo can lose the window.

- **Fix:** lengthen the window (proposed **12–15s**) **and/or** don't start the auto-dismiss until first hover/focus has occurred (show-until-interacted, then time out). Confirm the announce→Shift+Tab→Undo path feels reachable in the human acceptance. **Dependency:** a screen-reader user only learns Undo exists from the "Undo available" announcement, so **land F9's `announce()` fix before/with F4** — a dropped announcement plus a short window strands them.

### G. ARIA & announcements — F9

- **`aria-haspopup="true"`** on the Labels toggle ([labels.js:47](../Wend.Api/wwwroot/js/card/labels.js:47)) advertises a *menu*; the popup is a checkbox group. **Fix:** `aria-haspopup="dialog"` or drop it (`aria-expanded` already conveys the disclosure).
- **Settings hints not linked:** both `.setting-hint` paragraphs return `aria-describedby: null` ([settings/view.js:16](../Wend.Api/wwwroot/js/settings/view.js:16)), so a screen reader doesn't tie the hint to its checkbox. **Fix:** give each hint an `id` and each checkbox an `aria-describedby` pointing at it.
- **`announce()` uses rAF:** it clears then sets `#status` across a `requestAnimationFrame` ([announce.js:4](../Wend.Api/wwwroot/js/announce.js:4)). The sweep demonstrated this **drops the message entirely in a hidden/throttled tab** (rAF is paused there). Normal foreground use works, but it's needlessly fragile. **Fix:** swap the rAF for a small `setTimeout` (~120ms), which fires regardless of tab visibility.

## Mobile layout (decided — brainstormed & locked with Malin 2026-07-08)

**Requirement.** Below the tablet breakpoint (`< 768px`) the board currently stacks all lists vertically — a long scroll. Show **one list at a time** with a switcher, and give each list its own **Done strip**. Mobile-first: single-list + switcher is the *baseline*; the horizontal columns layer up unchanged at `min-width: 768px` (the board already flips to columns there — [app.css:41](../Wend.Api/wwwroot/css/app.css:41)).

**Decisions (the six open questions, now answered):**

1. **Switcher control → native `<select>`.** A labelled `<select>` at the top of the board on narrow screens ("List: To Do ▾"). Matches Wend's native-first ethos (already used for "Move to…"), gives a large self-announcing touch target, and scales to any number of lists with zero custom ARIA. *Rejected:* an APG `role="tablist"` (roving tabindex + arrow keys + `aria-selected`/`aria-controls`; wraps badly on a narrow screen). Hidden at `min-width: 768px`, where all columns show.

2. **Done area → per-list strip only; the global Done area is retired.** Each list gains a collapsible `Done (n)` strip of *its* completed cards, mirroring the checklist Done strip ([checklist.js:39](../Wend.Api/wwwroot/js/card/checklist.js:39)) — same collapse/label/contrast pattern; un-check returns the card to the active list. **This revises the Slice-1 "Done = both" decision** ([Slice 1 design](2026-06-15-wend-slice1-design.md)): with per-list strips, the global grouped-by-list area would render every done card a *second* time. Per-list-only = one card, one place, identical on mobile and desktop, consistent with the checklist strip already shipped. Removes the global `doneArea` ([board/view.js:108](../Wend.Api/wwwroot/js/board/view.js:108)) + its `ui.doneOpen`/`focusDoneToggle` (Phase-1 F1's refocus lesson carries to the per-list toggles) and the now-dead `.done-area`/`.done-group`/`.done-row-*` CSS ([app.css:300](../Wend.Api/wwwroot/css/app.css:300)). *Henry to confirm in review — the one Slice-1 decision Phase 2 changes.*

3. **Selected-list persistence → remembered per board.** The selected list is stored per board in `localStorage` (validated on read, in the spirit of [`prefs.js`](../Wend.Api/wwwroot/js/prefs.js)) so tapping a card and coming back returns to the same list instead of resetting to the first — the fresh-mount house pattern otherwise loses the user's place on every card round-trip.

4. **Swipe gestures → no.** Keyboard/SR-first; the `<select>` and buttons are the only way between lists (swipe fights SR gestures and isn't discoverable).

5. **Stale selection → fall back to the first list.** If the remembered/active list id is gone (deleted), the switcher falls back to the first list; the board's empty state shows when there are no lists.

6. **Change announcement → `announce()` on switch.** Switching lists fires the existing `announce()` with the new list name + active-card count ("Showing In Progress, 4 cards"); the native `<select>` also speaks the option itself. (A tabs pattern would have needed explicit `aria-controls`/live wiring — another point for `<select>`.)

**Structure.** Sits on **F6** (Phase 1): each list is already a labelled `role="group"` / `<h3>` region ([board/view.js:81](../Wend.Api/wwwroot/js/board/view.js:81)) — the switcher selects on those. Baseline (mobile) renders the `<select>` and shows only the selected `.list-card`; `min-width: 768px` hides the `<select>` and shows every column (current behaviour, unchanged).

## Frontend (files touched)

All changes are in `Wend.Api/wwwroot/`. No backend, no `js/api.js`, no models beyond view state. **Escape discipline:** every new site that interpolates a user title into `innerHTML` — F5 rename inputs + trigger `aria-label`, F6 region label/heading, the switcher `<option>`s — is `escapeHtml`-escaped (the card/checklist pattern already does this; the new sites must too).

- **`css/app.css`** — F2 (checkbox sizes), F3 (rename the colliding class, single-step dim), plus the mobile switcher / per-list strip styles (mobile-first, `min-width: 768px` for columns).
- **`js/board/view.js`** — F1 refocus, F6 list regions/headings, per-list Done strip + switcher markup, F5 inline list rename.
- **`js/board/controller.js`** — F5 inline list rename handlers (replacing `prompt`), switcher wiring.
- **`js/boards/view.js` / `js/boards/controller.js`** — F5 inline board rename (replacing `prompt`).
- **`js/card/view.js`** — F8 (rename form out of the `<h2>`).
- **`js/card/labels.js`** — F9 `aria-haspopup`.
- **`js/card/controller.js`** — F7 (one-line the confirm string).
- **`js/settings/view.js` / `js/settings/controller.js`** — F9 `aria-describedby`, F10 refocus.
- **`js/announce.js`** — F9 rAF → `setTimeout`.
- **`js/toast.js`** — F4 timing.

## Accessibility (what each change satisfies)

- **WCAG 2.5.8** Target Size (Minimum) — F2.
- **WCAG 1.4.3** Contrast (Minimum) — F3.
- **WCAG 2.4.1 / 1.3.1** landmarks & structure — F6; **4.1.2** name/role/value — F8, F9.
- **WCAG 2.2.1** Timing Adjustable — F4.
- **Focus management** (2.4.3 Focus Order / 2.4.7 Focus Visible in spirit) — F1, F10, F5's inline rename returning focus.
- Colour never the only signal (F3 keeps line-through); reduced-motion unaffected (instant re-renders); forced-colors already handled for the progress bar.

## Testing

- **No automated test change** — Plan 8 is frontend; the suite stays **147 green, 0 warnings** (I'll run it once at the end to confirm the backend is untouched).
- **Automated browser verification (new capability):** re-run the live sweep against each fix — the same measured checks that found them (checkbox ≥24px, Done-strip contrast ≥4.5:1, focus destinations after F1/F10, `aria-describedby` present, `aria-haspopup` corrected, list regions in the a11y tree). This is the acceptance gate I can drive myself.
- **Human keyboard + screen-reader acceptance (Malin & Henry):** the parts a driver can't measure — real NVDA/VoiceOver announcement quality, the toast reach under time pressure, the inline-rename feel on a phone. **Hard-reload / disable cache first** (see Gotchas).

## Build shape (Claude solo)

Grouped so each task is independently verifiable in the browser. Tasks describe intent + files + the verification check (not keystroke-level code — Claude writes the code and self-verifies via the sweep, then Malin & Henry review the PR + run acceptance).

**Phase 1 — a11y findings (ready now):**

1. **CSS fixes** — F2 (checkbox sizes ≥24/44px), F3 (rename the colliding `.done-row-label`, single-step dim ≥4.5:1), F7 (one-line confirm string). *Verify:* measured sizes + contrast in-browser.
2. **Focus management** — F1 (board Done toggle) + F10 (settings toggle) refocus. *Verify:* `activeElement` is the toggle, not `<body>`, after each.
3. **Structure** — F6 (list regions/headings) + F8 (rename form out of `<h2>`). *Verify:* a11y tree shows list regions; no `<form>` descendant of `<h2>` while renaming.
4. **ARIA & announcements** — F9 (`aria-haspopup`, `aria-describedby`, rAF → `setTimeout`). *Verify:* attributes present/corrected; `#status` updates without rAF.
5. **Toast timing** — F4. *Verify:* window ≥12s and/or not-until-interacted.
6. **Inline rename** — F5 (boards + lists, replacing `prompt()`). *Verify:* text-as-button → input swap, Enter/Esc, focus returns — matching the card/item pattern.

**Phase 2 — mobile layout (decided 2026-07-08):**

7. Per-list Done strip + **retire the global Done area** (revises "Done = both"; removes the global `doneArea` block + dead `.done-area`/`.done-row-*` CSS; per-list toggles inherit the F1 refocus).
8. Single-list switcher — native `<select>` (mobile baseline; hidden at `min-width: 768px`), selected list remembered per board in `localStorage` (validated; stale → first).
9. Acceptance + README status (Slice 1 complete) + backlog note (mobile shipped).

Branch `feature/mobile-a11y-polish` → PR → review + keyboard/SR acceptance → merge. Single-author (Claude as Malin) → squash-merge is clean (the merge-not-squash rule only bites when Henry has commits on the branch).

## Gotchas carried forward

- **Dev static-file cache** — `UseStaticFiles` sends no `Cache-Control`, so the browser serves stale JS from earlier localhost sessions and a normal reload won't revalidate module imports. **Hard-reload / disable cache** (or `fetch(url, {cache:'reload'})` each module) before every browser check. Bit the sweep on 2026-07-08 before it was caught. *(Optional, out of scope: a dev-only `no-cache` header on static files would end this — note for a future housekeeping pass, not Plan 8.)*
- Stop any running `Wend.exe` / `dotnet run` before `dotnet build`/`test` — the process is **`Wend.Api`**, not `Wend` (`Get-Process Wend.Api ... | Stop-Process`).
- No `data.db` reset this plan — frontend-only, no schema change.
- Squash-merging a multi-author branch adds a co-author trailer — but Plan 8 is single-author (Claude solo), so squash is clean.
