# Wend — reviewer's guide

- **Date:** 2026-08-13
- **For:** Henry, reviewing and merging
- **Working split, confirmed 2026-08-13:** Malin writes the code, Henry reviews and merges.

Wend has two equal owners and nothing lands on `main` without a second pair of eyes. With one of us
writing all of it, the review *is* the safety net rather than a formality — so this file is about what
to attack, not what to admire.

Skip to [Plan 6 review checklist](#plan-6-review-checklist) when that PR arrives. The rest is the
standing routine.

---

## The routine

**1. Read the PR body first, then the spec it implements.** Every Wend PR body carries its own
deviations from the plan — that is the house habit, and it means the body is the honest account of
what happened, not a summary of the diff. If a PR body says nothing about deviations, that itself is
worth a question.

**2. Check the test count moved.** The baseline is stated in each PR body. A feature PR that adds
endpoints and leaves the count unchanged has either no tests or tests that replaced others. A
paste-driven edit once silently dropped three tests behind a green suite, and nobody noticed until the
totals were compared against the plan — so compare them.

**3. Read the tests before the implementation.** In this codebase the tests are where the reasoning
lives; several of them exist specifically because a plausible-looking implementation passes everything
else. If a test's name does not tell you what would break without it, that is a finding.

**4. Then read the implementation, against the checklist for that plan.**

**5. Ask rather than assume.** You did not write it, so anything that reads as odd is either a trap
you have not met or a mistake. Both are worth a comment.

---

## What this codebase punishes

Worth knowing whether or not you run the code locally.

- **A generic response is load-bearing.** Register, forgot-password and login deliberately answer the
  same thing for known and unknown addresses. Anywhere a PR makes one of those responses *more*
  informative, the question is whether it just became a user-enumeration oracle. This is the single
  most common way to break Wend's security posture while improving its error messages.
- **Input validation goes before the existence lookup.** Same reason. Found in Plan 3's stress test,
  when register validated the password inside `CreateAsync` — which only runs for a *free* address —
  and so answered 400-free / 204-taken with every enumeration test still green.
- **404, never 403.** A board another user owns must not be confirmed to exist.
- **`escapeHtml` at every interpolation of user content**, no exceptions. Views render through
  template literals into `innerHTML`.
- **`#status` lives outside `#app`** so a re-render cannot wipe the live region.
- **Focus must never land on `<body>`.** After a full-`innerHTML` repaint, the acted-on control has to
  be refocused explicitly. This is the most-repeated frontend bug here.
- **A bare `<button>` is 28px** and fails the 44×44 minimum — controls use `.btn` plus a variant.
- **Don't accept changes under `Wend.Api/wwwroot/design-system/`.** It is a vendored copy of a shared
  library; edits there are overwritten by the next `sync-design-system.ps1` run. Fixes belong upstream.

If you do run it locally: `Start-Service postgresql-x64-17` first (the service is Manual start, so a
stopped one looks like a code bug), and **hard-reload every time** — `UseStaticFiles` sends no
`Cache-Control`, so stale JS and CSS survive across sessions and a normal reload does not revalidate
ES-module imports.

---

## Merge mechanics

- **Squash** when the branch is single-author, which it now normally will be. Merge-not-squash only
  when the branch carries commits from both of us, because squashing that adds a co-author trailer.
- **No AI attribution** in commits or PR bodies — no `Co-Authored-By`, no "Generated with" trailers.
  If you see one in a PR, that is a defect to flag.
- **Delete the remote branch after merging, and check it actually went.** The auto-delete has silently
  no-op'd once.
- **`Build & test` is a strict required check**, so a PR that has fallen behind `main` needs its branch
  updated before it can merge — GitHub's *Update branch* button. Merging several PRs in a row means
  each later one may need it.
- **If a required check never runs at all, suspect the webhook, not the branch.** An Actions outage
  once throttled webhooks so pushes created no runs, leaving a PR unmergeable behind the strict check.
  Close/reopen does not help — push an empty commit, or run `ci.yml` manually via `workflow_dispatch`.

---

## Plan 6 review checklist

Plan 6 is account settings: change password, change email. Spec:
[`2026-08-13-wend-slice2a-plan6-account-settings-design.md`](2026-08-13-wend-slice2a-plan6-account-settings-design.md).

### The four that a wrong implementation passes anyway

These are the ones worth your time. Each was found by reading framework source or by the stress test,
and each has a wrong version that looks right and goes green.

1. **`SetUserNameAsync` must sit alongside `ChangeEmailAsync`**, on the same `user` instance.
   `ChangeEmailAsync` does not touch `UserName`, and Wend sets `UserName = Email` at registration — so
   without it, the old address stays occupied as a username forever, and the next person to register it
   fails `DuplicateUserName`, which register answers with **204 and a log line**. Silent success, no
   mail, and the victim is a stranger.
   **The test to look for:** after a completed change-email, the *old* address can be registered fresh
   and receives a confirmation mail. If that test is missing, nothing else in the suite covers this.

2. **`/change-email` answers a generic `204` for an address another account holds** — not `409`. The
   endpoint is authenticated and feels private, which is exactly why a conflict response looks
   reasonable. It would let any account holder walk the user table from their own settings page.
   **Check:** the test asserts no mail was sent on that branch, not just that the status was 204.
   Also check that **self is excluded** from the lookup — the caller finding their own address is a
   real state, and it is the repair path out of a half-changed account.

3. **`/confirm-email-change` returns `200 { email }`, read back after both writes**, and the success
   screen renders that value — never the `newEmail` from the query string. That query value is
   caller-controlled on an anonymous page, so rendering it is reflected XSS.
   **Check:** neither `code` nor `newEmail` appears anywhere in the DOM after the screen mounts.

4. **`/change-password` does its own lockout accounting.** `ChangePasswordAsync` does none, so without
   it a stolen session gets unlimited guesses at the current password — and a correct guess turns a
   session that dies on its own into permanent takeover. Look for `AccessFailedAsync` on a wrong
   current password, `ResetAccessFailedCountAsync` on success, and a locked-out account refused
   **without the password being checked**.

### Also verify

- **`RefreshSignInAsync` after the password change**, and **both** halves asserted: the acting session
  survives *and* another session for the same user is refused. Either assertion alone is satisfied by
  the wrong implementation.
- **Persistence survives a password change** — sign in with remember-me, change the password, confirm
  the reissued cookie still carries `expires`. This must run on `useTestAuth: false`; the `Test` auth
  scheme issues no cookie, so a test-scheme version passes while testing nothing.
- **The `400` / `409` split on confirm is not collapsed.** A bad token means "get a new link"; a taken
  address means "pick a different one". One code for both produces a screen that says the link expired
  when the link was fine.
- **Two forms on one screen, three rules:** each form owns its error region and never clears the
  other's; focus after any submit stays inside the submitting form; each form has its own `<h3>` and is
  tied to it with `aria-labelledby`.
- **The Account screen has no route.** Only `/confirm-email-change` joins the `switch` in `main.js`,
  and it POSTs on mount — a `GET` endpoint would let a mail scanner complete the change before the
  human clicks.
- **An older change-email token still works after a newer one is issued.** Requesting a second link
  does not revoke the first; that is documented behaviour and has a test.

### Do not ask for this one

The stress test found that **`/change-email` requires no password**, so a stolen session is a
permanent takeover with no self-service recovery. Malin decided to leave the fix out of Plan 6. It is
recorded in [`backlog.md`](backlog.md) as a Plan 8 launch gate with the full path written out. It is a
deliberate deferral, so a PR that quietly closes it inside Plan 6 would be harder to review, not
better — and a PR that leaves it open is correct.

---

## Where to read

- **[`docs/backlog.md`](backlog.md)** — every consciously-deferred decision with its reason and the
  trigger for revisiting, plus *resolved* entries where a fixed trap's reasoning survives. Read it
  before asking for a change; there is a good chance the answer is already written down.
- **PR bodies for #41, #43, #44, #47** — Plans 3, 4, 5 and the design-system sync. Each records its own
  deviations, which is why the project's history is readable.

If something in this guide is wrong, say so — the guide being wrong is more useful to know than a
review that quietly worked around it.
