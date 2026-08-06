# Wend — Slice 2a Plan 2 design: Identity schema & per-user ownership

- **Date:** 2026-08-06
- **Status:** **Signed off by both owners 2026-08-06** — all four locked decisions confirmed by Henry at review
- **Owners:** Malin & Henry (equal ownership)
- **Repo:** `github.com/wendhq/wend`
- **Parent spec:** [`2026-07-08-wend-slice2a-accounts-design.md`](2026-07-08-wend-slice2a-accounts-design.md) (signed off, stress-tested)
- **Follows:** Plan 1 — native PostgreSQL & EF migrations (PR #31, merged `5071a63`)

---

## Context — what this plan inherits

The parent spec's sequencing listed a single "Foundation" plan covering Postgres, EF migrations,
`WendUser`, `IdentityDbContext`, `Board.OwnerId` and per-user scoping. Plan 1 shipped only the
first half: persistence moved to native PostgreSQL, migrations were adopted, and the suite settled
at **147 green**.

Plan 2 takes the second half — the identity **schema** and the **ownership boundary**. It is the
plan where Wend stops being a single-user application at the data layer, which is the prerequisite
for every auth flow that follows.

One ordering problem shapes the whole design: **per-user scoping needs a current user, and login
does not exist until Plan 3.** Everything below follows from resolving that without either
deferring the security boundary or dragging authentication forward.

---

## Goals & non-goals

**Goals**

- `WendUser`, Identity's schema, and a required `Board.OwnerId` with a cascade to owned data.
- Every repository query scoped to an owner, so a row belonging to another user is **not found**
  rather than forbidden — 404, not 403, exactly as the parent spec requires.
- The isolation test set the parent spec calls "the highest-value test set in the slice", written
  against the schema that enforces it rather than retrofitted a plan later.

**Non-goals** (explicitly later plans)

- Identity *services* — no `UserManager`, `SignInManager`, cookie scheme, or `RequireAuthorization()`.
- Registration, verification, login, logout, `/me`, reset, account settings, deletion endpoints.
- Any frontend work. No `js/auth/`, no auth gate, no CSRF handling.
- `DisplayName` validation and escaping. The column exists; nothing writes to it until Plan 3
  introduces registration, and the validation belongs with the write path.

---

## Decisions locked (brainstorm, 2026-08-06)

| Decision | Choice | Why |
|---|---|---|
| **Scoping timing** | Lands in this plan, behind an `ICurrentUser` seam | The security boundary ships with the schema that enforces it, never as a retrofit |
| **Enforcement point** | Explicit `string ownerId` parameter on every repository method | Ownership becomes part of the contract; a method added in a later plan cannot silently skip it |
| **Existing dev data** | Clean slate — the migration empties `Boards` | An ownerless board should not be a state the system can represent (Henry, review 2026-08-06); no placeholder account row is invented, and none can leak into production |
| **Identity depth** | Schema only; services in Plan 3 | Keeps this plan "data model + boundary" and Plan 3 "auth machinery" |

**Accepted cost of the clean slate:** current dev boards are lost, and Wend is unusable in the
browser between this plan and Plan 3 — every `/api/*` call returns 401 because no current user can
exist yet. The test suite is the only consumer during that window. This was chosen deliberately
over seeding a placeholder owner.

---

## Data model

```
WendUser ──1:*── Board ──1:*── List ──1:*── Card ──*:*── Label
   │                                          │
   │  (Identity tables: AspNetUsers,          └──1:*── ChecklistItem
   │   AspNetUserTokens, AspNetUserClaims, …)
```

| Entity | Change |
|---|---|
| **WendUser** | *New.* `IdentityUser` subclass — Id (string PK), Email, `DisplayName`, plus Identity's own fields (password hash, security stamp, lockout, email-confirmed). |
| **Board** | Gains a required `OwnerId` FK → `WendUser`, **cascade delete**. |
| **List** | Gains a `Board` navigation. No schema change. |
| **Card** | Gains a `List` navigation. No schema change. |
| **Label** | Gains a `Board` navigation. No schema change. |
| **ChecklistItem** | Gains a `Card` navigation. No schema change. |

`WendDbContext` becomes `IdentityDbContext<WendUser>`. `OnModelCreating` calls `base` first so
Identity's mapping is applied, then keeps the existing soft-delete query filters and `CardLabel`
configuration unchanged.

**Why the four navigations.** None of the child entities currently has an upward navigation —
`List` carries `BoardId` but no `Board`, `Card` carries `ListId` but no `List`. Ownership is
therefore inexpressible in a query today. Each new navigation pairs with a collection that already
exists on the principal (`Board.Lists`, `List.Cards`, `Board.Labels`, `Card.ChecklistItems`), so
EF pairs them by convention and **no column is added**. They exist solely to make ownership
traversable.

**Ownership cascade.** Deleting a `WendUser` removes their boards, and the existing required FKs
cascade onward to lists, cards, labels, join rows and checklist items. This is the mechanism that
makes account deletion a clean GDPR erasure in a later plan; this plan proves it with a test.

---

## The ownership boundary

Each EF repository gains one private helper — the single place ownership is expressed:

```csharp
private IQueryable<Card> Owned(string ownerId) =>
    db.Cards.Where(c => c.List.Board.OwnerId == ownerId);
```

Every method then starts from `Owned(ownerId)` rather than `db.Cards`. A row belonging to another
user is absent from the query, so existing "missing → false / null" paths already produce the
correct 404 without new branching.

**Two consequences.**

- **`FindAsync` cannot carry a predicate**, so its **22 call sites** across the five EF
  repositories become `Owned(ownerId).FirstOrDefaultAsync(...)`. This is the same class of change
  as the Plan 7 restore bug, where a `FindAsync` read behaved differently across contexts.
- **The `IgnoreQueryFilters()` trap is designed out.** `RestoreCardAsync` and
  `RestoreItemAsync` still need `IgnoreQueryFilters()` to reach soft-deleted rows. Because
  ownership lives in a `Where` clause rather than a global query filter, it survives that call. A
  global-filter implementation would have silently dropped the ownership predicate on exactly the
  paths that resurrect data — this is why that option was rejected.

**Interface surface.** All **34 repository methods** across the five interfaces gain an explicit
`string ownerId` parameter.

| Interface | Methods |
|---|---|
| `IBoardRepository` | 5 |
| `IListRepository` | 6 |
| `ICardRepository` | 8 |
| `ILabelRepository` | 7 |
| `IChecklistItemRepository` | 8 |

---

## `ICurrentUser` and the API layer

`ICurrentUser` exposes a nullable `string? UserId` and lives in **`Wend.Api`, not `Wend.Core`** —
the domain takes an owner id as data and stays ignorant of the notion of a *current* user, which is
an HTTP concern.

This plan registers a `NullCurrentUser` returning `null`, because nothing can authenticate yet.
Plan 3 replaces the registration with an `HttpContext`-backed implementation; no other code changes.

Every endpoint opens with one guard:

```csharp
if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
```

One explicit line, no middleware magic, and it survives Plan 3 as defence in depth behind
`RequireAuthorization()`. Until Plan 3 this is what makes the whole API return 401 — the honest,
visible consequence of the clean-slate decision.

---

## Migration

A single migration adds Identity's tables, `Board.OwnerId`, and the FK.

Because `OwnerId` is **required** and existing boards have no owner, the migration deletes all rows
from `Boards` before adding the column; the existing cascades clear lists, cards, labels, join rows
and checklist items. On a fresh database — CI, every test, and any future production database —
this is a no-op against an empty table.

The migration must apply cleanly from an empty database; that is asserted by test, as in Plan 1.

---

## Testing

The two-tier engine from Plan 1 is unchanged: repository unit tests on in-memory SQLite, API
integration tests against a per-test throwaway PostgreSQL database via `WendApiFactory`.

**Test seam.** `WendApiFactory` overrides `ICurrentUser` with a mutable test double, so a single
test can act as user A, switch to user B, and assert the boundary. Repository tests insert
`WendUser` rows directly through the context — no `UserManager` exists in this plan, and none is
needed.

**New coverage**

- **Per-user isolation** — for every entity and every verb, a request made as user B against user
  A's data returns 404. Boards, lists, cards, labels, checklist items; read, create-into, edit,
  move, complete, delete, restore.
- **Ownership cascade** — deleting a `WendUser` removes their boards, lists, cards, labels, join
  rows and checklist items.
- **No current user → 401** on every endpoint group.
- **Restore under ownership** — a soft-deleted card and checklist item restore for their owner and
  are 404 for a non-owner, proving `IgnoreQueryFilters()` did not widen the boundary.
- **Migration applies cleanly** from an empty database.

**Existing coverage.** All **147 existing tests** change mechanically to create a user and pass an
owner id. The count is asserted against each task's expected total, because a paste-driven edit of
exactly this shape once silently dropped three tests behind a green suite.

---

## Task breakdown (detail is the writing-plans step)

1. **Schema** — `WendUser`, `IdentityDbContext`, `Board.OwnerId`, the four navigations, the migration.
2. **Board repository** — `Owned` helper, `ownerId` on 5 methods, isolation tests.
3. **List repository** — 6 methods.
4. **Card repository** — 8 methods, including the `IgnoreQueryFilters()` restore path.
5. **Label repository** — 7 methods, including the `CardLabel` join queries.
6. **Checklist-item repository** — 8 methods, including the second `IgnoreQueryFilters()` restore path.
7. **Endpoints** — `ICurrentUser`, `NullCurrentUser`, the 401 guard across all five endpoint files.
8. **Isolation suite** — cross-user coverage, cascade, and the 401 sweep as a single reviewable set.

---

## Risks

- **Mechanical breadth.** 34 signatures, 22 `FindAsync` rewrites and 147 test call sites in one
  plan. Mitigated by one task per repository and a per-task test-count assertion.
- **Query filters through navigations.** Whether traversing `i.Card.List.Board` applies `Card`'s
  soft-delete filter mid-traversal is subtle. It gets a test rather than an assumption, and the
  answer is recorded in the plan.
- **`IdentityDbContext` and existing filters.** `base.OnModelCreating` must run before Wend's own
  configuration; getting the order wrong silently drops the soft-delete filters. Covered by the
  existing soft-delete tests, which must stay green.

---

## Key decisions (and why)

- **Scoping ships with the schema** — a security boundary added a plan after the model it protects
  is a boundary someone has to remember to add.
- **Explicit `ownerId` parameters, not an ambient current user inside the repositories** —
  ownership is visible in the type system, so the next person to add a repository method is forced
  to answer "whose?". It also leaves the repositories usable by non-owner callers, which Slice 2b
  sharing and the unverified-account purge job will both need.
- **Not EF global query filters** — the undo/restore paths already call `IgnoreQueryFilters()`,
  which would have silently dropped ownership on the exact paths that bring data back.
- **`ICurrentUser` in `Wend.Api`** — "current user" is an HTTP concept; the domain receives an
  owner id as an ordinary argument.
- **Clean slate over a seeded placeholder** — **an ownerless board should not be a state the system
  can represent** (Henry's framing at review, 2026-08-06, and the load-bearing reason). Creating a
  board should require an account. This is also why `OwnerId` is **required rather than nullable**:
  a nullable column would keep ownerless boards legal indefinitely, and every query would carry a
  null branch meaning "belongs to nobody" — precisely the state the slice exists to abolish. The
  secondary argument still holds: a passwordless placeholder account exists only to paper over a
  two-plan gap, and someone has to remember to delete it. Losing disposable dev boards is cheaper
  than either.
- **Identity schema without Identity services** — the .NET 10 headless-Identity wiring is a flagged
  gotcha; it belongs in the plan that actually signs users in, not in a schema change.

---

## Open items — to confirm at plan time

- Whether EF applies `Card`'s soft-delete filter when `Card` is traversed as a navigation inside a
  `ChecklistItem` ownership predicate (test, then record).
- Whether adding the four navigations produces a genuinely empty schema diff, or whether EF emits
  incidental model changes that need folding into the migration.
- `DisplayName` column constraints (length cap, nullability) — set here, validated in Plan 3.
- Whether the 401 guard is repeated per endpoint or lifted into a route-group endpoint filter, once
  the repetition is visible across all five endpoint files.

---

*Draft 2026-08-06. Brainstormed against the signed-off Slice 2a spec. Next: review, then the
implementation plan.*
