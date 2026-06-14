# Unified Permission Center — Architecture Review & Implementation Plan (v2, single-model)

**Status:** Proposal for review · **Scope:** Backend only · **Mandate:** extend the existing RBAC platform; no parallel authorization model
**Audience:** Part A/B = integration-review guidance; Part C = work items for a separate Codex session

> **Change from v1:** the separate "platform-administration plane" is removed. The Unified Permission Center is modeled as **just another RBAC-managed system** — a single reserved project with its own `rbac.global.*` permission codes — so the existing authorization pipeline, filters, Outbox, ES, Redis, audit, repositories, and management services authorize and drive it unchanged. No new table, repository, cache, filter, or search architecture is introduced.

---

## Part A — Architecture Review

### A.1 The core idea

Treat the Unified Permission Center as a **reserved RBAC project** (default code `__global__`, configurable). Its admin APIs are ordinary RBAC-protected endpoints whose routes are mapped — through the existing `rbac_api_permission_map` — to platform-level permission codes:

- `rbac.global.admin` — umbrella capability for the global console
- `rbac.global.user.manage` — cross-project user administration
- `rbac.global.group.manage` — cross-project group administration
- `rbac.global.menu.manage` — cross-project menu/rule administration

A user becomes a "global administrator" exactly the way anyone becomes any other system's admin: a normal `RbacProjectGrant` into `__global__`, plus membership in a group (within `__global__`) that carries the `rbac.global.*` rules — **or**, more simply, `IsSuper = true` within `__global__`. Both paths already exist in the platform; neither is new.

### A.2 Why this needs no second authorization model

Confirmed against the code: `RoutePatternApiPermissionMapper` resolves a route from `(project, method, path)` where `project` is `CurrentRbacContext.Project`, and the management write service is **parameterized by the entity's project**, not bound to the request context. Therefore the full existing pipeline authorizes the global APIs with no new code:

1. Caller sends `X-Project: __global__` with their JWT.
2. The existing `IRbacProjectResolver` authorizes the user against `__global__` (they hold a normal grant). No grant → existing 403.
3. The existing `RbacAuthorizationFilter` maps `/api/global/...` → a seeded `rbac.global.*` code + action and checks it against the existing permset/Casbin. Unmapped → existing deny-by-default.

No skip-set, no new filter, no new capability lookup. The global console is authorized identically to every other RBAC-managed system.

### A.3 How project isolation is preserved automatically

The platform's `IsSuper` is project-scoped by invariant, and that invariant does the isolation work for free:

- A global admin who is super in `__global__` can use every global API, but has **zero runtime access to any business system** — business APIs are authorized against the business project, where this user holds no grant. Project isolation for business systems is preserved by the existing model, with no new rule.
- A regular project admin (e.g. of `news`) holds no grant in `__global__`, so they cannot reach any global API and cannot cross project boundaries.
- The cross-project power of a global admin is exactly and only the `rbac.global.*` capability — explicit, auditable, and revocable like any other RBAC grant.

The one boundary to state plainly: `rbac.global.*` **intentionally** authorizes acting on other projects' data. That is the product requirement (centralized cross-project administration). The isolation guarantee that remains intact is for *business-system* users — a project admin still cannot touch another project. Only holders of the explicit global capability cross projects, and only through the Global APIs.

### A.4 How cross-project management works without new machinery

The Global controllers take the **target project(s) from the request** (body/query), not from `X-Project` (which is fixed to `__global__`). For each target project they delegate to the **existing** `RbacManagementWriteGuard` + `IRbacManagementWriteService`, passing the target project and `operatorUserid = ctx.Userid`. Because those services are already project-parameterized:

- the existing per-project reload-and-match guard runs for each target project (isolation at the write level),
- the existing Outbox event (`UserChanged` / `GroupChanged` / `MenuChanged` / `ProjectGrantChanged` / `ApiMapChanged`) is written **in the same transaction**, keyed to the target project,
- the existing Redis / ES / Casbin processors consume those events unchanged.

A global "disable user across projects A, B, C" is simply three ordinary per-project writes through the existing service — three normal Outbox events, consumed normally. **The synchronization fabric requires zero changes.**

### A.5 Outbox / ES / Redis / Casbin / audit reuse

- **Outbox:** no new event types, payloads, or processors. Global writes reuse the existing per-project events.
- **Redis & Casbin:** per-project version-increment invalidation and atomic policy reload run unchanged, because the events are unchanged.
- **Elasticsearch (writes):** untouched.
- **Elasticsearch (cross-project reads):** reuse the existing management search service via the `project = "*"` seam already present in the repositories and warmup worker. The one nuance to verify (A.8 #3) is whether the ES query builder omits the project filter when `project = "*"`; if it currently always adds a project term, add a small read-only all-projects variant — additive, no mapping or reindex change, since `project` is already an indexed keyword.
- **Audit:** reuse the existing pipeline. Authorization allow/deny on the global routes is already captured by the existing authorization audit. Each underlying per-project change is already captured by the existing Outbox/audit path with `operatorUserid` = the global admin. No new audit event type or index.

### A.6 Project discovery without a registry table

Derive the project list from existing data, as requested: `SELECT DISTINCT project FROM rbac_project_grant`. Implement as one additive method `GetDistinctProjectsAsync()` on the existing `IProjectGrantRepository` (the warmup worker already does an equivalent derivation from rules, so this pattern is established). No new table, no new registry.

### A.7 Backward compatibility

Everything is additive or seed data. Do **not** modify: `RbacAuthorizationFilter`, `RbacProjectResolver` / `CurrentRbacContext`, `RbacProjectGrant` (the project-scoped `IsSuper` invariant stays), Outbox event types/payloads, the three Outbox processors, Redis keys, ES mappings, the Casbin model, or any existing controller/DTO. The only edits to existing files are: one additive repository method, DI registration for the thin orchestration service, and seed rows in `rbac-bootstrap.sql`. New controllers are picked up automatically by MVC. A short verification phase proves the runtime plane is byte-unchanged.

### A.8 Open decisions to confirm before coding (CLAUDE.md §1)

1. **Reserved project code value.** Default `__global__`. Confirm a value that cannot collide with any real business project (and is excluded from the cross-project target list so global ops can't recurse onto the global system itself unless intended).
2. **Coarse vs. fine global authorization.** Bootstrap the first global admin as `IsSuper` in `__global__` (coarse, simplest) and/or wire the four `rbac.global.*` codes onto a group for fine-grained control. Recommend seeding **both**: super for bootstrap, plus the four codes so least-privilege is available. Confirm.
3. **ES all-projects read.** Confirm whether the existing ES query builder already supports `project = "*"` (omit filter) or needs a small additive all-projects read variant.
4. **Non-atomic cross-project writes.** A multi-project operation is N transactions; partial failure leaves earlier projects committed. Recommend best-effort with a per-project result report + idempotent retry (matches the existing eventual-consistency philosophy). Confirm.
5. **Consolidated operation audit (optional).** With pure reuse, one global action produces N per-project audit rows rather than a single "one action → N projects" record. Recommend accepting the per-project rows for now (no new audit architecture); a consolidated record can be added later if needed. Confirm.

---

## Part B — Implementation Plan (integration guidance)

### B.1 New components (minimal)

```
src/Rbac.Application/Global/RbacGlobalConstants.cs        (reserved project code + the four permission codes)
src/Rbac.Application/Global/IGlobalManagementService.cs   (orchestration contract)
src/Rbac.Application/Global/GlobalManagementService.cs     (per-project fan-out over EXISTING guard + write service)
src/Rbac.Api/Controllers/Global/GlobalUserController.cs    (cross-project user mgmt → rbac.global.user.manage)
src/Rbac.Api/Controllers/Global/GlobalGroupController.cs   (cross-project group mgmt → rbac.global.group.manage)
src/Rbac.Api/Controllers/Global/GlobalMenuController.cs    (cross-project menu/rule mgmt → rbac.global.menu.manage)
src/Rbac.Api/Controllers/Global/GlobalProjectController.cs (project discovery + cross-project read → rbac.global.admin)
```

Everything else is **reused**: `RbacManagementWriteGuard`, `IRbacManagementWriteService`, `IRbacManagementSearchService`, `IProjectGrantRepository`, the authorization filter/pipeline, Outbox, workers, Redis, ES, audit.

### B.2 Additive touches to existing files (registration-level only)

- `IProjectGrantRepository` + `RbacRepositories`: add `GetDistinctProjectsAsync()` (`SELECT DISTINCT project FROM rbac_project_grant`).
- `Program.cs`: register `GlobalManagementService` (DI only).
- `rbac-bootstrap.sql`: seed the `__global__` system (B.4).
- (Conditional, per A.8 #3) ES query builder: add an all-projects read variant if `project = "*"` is not already honored — read-only, additive.

### B.3 Request and delegation flow

Authorization (no new code):
```
GET/POST /api/global/...  with  X-Project: __global__
  → existing resolver authorizes user in __global__
  → existing filter maps route → rbac.global.<area>.manage : <action>
  → existing permset/Casbin check
```

Write delegation (reuses existing services):
```
GlobalManagementService.<Op>(operatorUserid, targetProjects, payload):
  results = []
  for project in resolve(targetProjects):           // "*" → GetDistinctProjectsAsync(), minus reserved code
      try:
          guard.Load<Aggregate>(..., project)        // existing per-project reload + match
          write.<ExistingMethod>(..., operatorUserid, project)  // existing write + in-txn Outbox
          results.add(project, ok)
      catch e:
          results.add(project, failed, e)            // best-effort; no global rollback
  return PerProjectResultReport(results)             // audited via existing pipeline
```

Cross-project read:
```
GlobalProjectController.list      → GetDistinctProjectsAsync()
Global*Controller.search(project) → IRbacManagementSearchService with project = "*" or a subset
```

### B.4 Bootstrap (resolves chicken-and-egg via seed, then self-manages)

Seed in `rbac-bootstrap.sql`, all as ordinary RBAC rows under project `__global__`:

1. Menu/button **rules** for the global console, carrying the four `rbac.global.*` permission codes.
2. A bootstrap **group** in `__global__` membership-linked to those rules.
3. **`rbac_api_permission_map`** rows mapping each `/api/global/*` route + method to the matching `rbac.global.*` code + action, with `project = __global__`.
4. A **`rbac_project_grant`** row for the first global admin `userid` in `__global__` (with `IsSuper = true` for bootstrap convenience).

After seeding, the global system is administered through the **existing** admin controllers pointed at `__global__` — no special-case management path.

### B.5 Compatibility guardrails (must hold)

- `RbacProjectGrant.IsSuper` stays project-scoped; no global-super field anywhere.
- The global capability lives entirely in normal RBAC data (`__global__` rules/groups/grants + api-map rows); no second model.
- Existing runtime-authorization files are unchanged; only additive registration + seed.
- Every global mutation is attributable to a real `operatorUserid` and produces the same per-project Outbox/audit as the equivalent per-project API.

---

## Part C — Task Breakdown (work items for the Codex session)

Conventions match `tasks.md`: `[P]` parallelizable; `[GAx]` story group; `[MVP]`; `[Compat-Blocker]`. Paths follow B.1. Task IDs use a `G` prefix to avoid colliding with the existing `T###` range.

### Phase GA0 — Setup, ADR & decisions

- [ ] G001 Write ADR for the single-model approach (Unified Permission Center as the reserved `__global__` RBAC system) and record why no second authorization model is introduced, in `docs/rbac/adr-002-unified-permission-center.md`
- [ ] G002 Resolve and record the five open decisions from review §A.8 (reserved project code, coarse/fine authorization, ES all-projects read, non-atomic write semantics, audit granularity) in `docs/rbac/upc-decisions.md`

### Phase GA1 — Reserved global system & bootstrap (P1, MVP)

- [ ] G003 [P] [GA1] [MVP] Define `RbacGlobalConstants` (reserved project code default `__global__`; the four `rbac.global.*` permission codes) in `src/Rbac.Application/Global/RbacGlobalConstants.cs`
- [ ] G004 [GA1] [MVP] Seed the `__global__` system in `rbac-bootstrap.sql`: console rules carrying `rbac.global.*` codes, a bootstrap group, `rbac_api_permission_map` rows for every `/api/global/*` route→code, and a `rbac_project_grant` (IsSuper) for the first global admin userid
- [ ] G005 [GA1] [MVP] [Compat-Blocker] Exclude the reserved project code from cross-project target resolution so global ops do not accidentally recurse onto `__global__`; document the rule
- [ ] G006 [GA1] Document the reserved-system model, bootstrap, and self-management-via-existing-controllers in `docs/rbac/global-system-bootstrap.md`

### Phase GA2 — Cross-project write APIs (P1, MVP)

- [ ] G007 [P] [GA2] [MVP] Define `IGlobalManagementService` + `PerProjectResultReport` in `src/Rbac.Application/Global/IGlobalManagementService.cs`
- [ ] G008 [GA2] [MVP] [Compat-Blocker] Implement `GlobalManagementService` as a per-project fan-out that delegates only to the existing `RbacManagementWriteGuard` + `IRbacManagementWriteService` (no new write path, no new Outbox events) in `src/Rbac.Application/Global/GlobalManagementService.cs`
- [ ] G009 [GA2] [MVP] Register `GlobalManagementService` in `src/Rbac.Api/Program.cs` (additive DI only)
- [ ] G010 [GA2] [MVP] Implement `GlobalUserController` (cross-project user administration, target project(s) from request) in `src/Rbac.Api/Controllers/Global/GlobalUserController.cs`
- [ ] G011 [GA2] [MVP] Implement `GlobalGroupController` (cross-project group administration) in `src/Rbac.Api/Controllers/Global/GlobalGroupController.cs`
- [ ] G012 [GA2] Implement `GlobalMenuController` (cross-project menu/rule administration) in `src/Rbac.Api/Controllers/Global/GlobalMenuController.cs`
- [ ] G013 [GA2] Document non-atomic, best-effort, idempotent-retry cross-project write semantics and the per-project result report in `docs/rbac/global-write-delegation.md`

### Phase GA3 — Project discovery & cross-project read (P2)

- [ ] G014 [P] [GA3] Add additive `GetDistinctProjectsAsync()` (`SELECT DISTINCT project FROM rbac_project_grant`) to `IProjectGrantRepository` and implement in `src/Rbac.Infrastructure/DM/Repositories/RbacRepositories.cs`
- [ ] G015 [GA3] Implement `GlobalProjectController` (project discovery via the new method; cross-project list/search delegating to the existing search service with `project="*"` or a subset) in `src/Rbac.Api/Controllers/Global/GlobalProjectController.cs`
- [ ] G016 [GA3] (Conditional per A.8 #3) If the existing ES query builder always applies a project term, add a read-only all-projects variant (no mapping/reindex change); otherwise document that `project="*"` is already honored, in `docs/rbac/global-read-reuse.md`

### Phase GA4 — Audit reuse & compatibility verification (gate to merge)

- [ ] G017 [P] [GA4] Verify global-route authorization (allow/deny) and per-project change events are already captured by the existing audit pipeline with `operatorUserid` = global admin; document any gap in `docs/rbac/global-audit-reuse.md`
- [ ] G018 [P] [GA4] [Compat-Blocker] Verify `RbacProjectGrant.IsSuper` stays project-scoped and no global-super field exists → `docs/rbac/verify-no-global-super.md`
- [ ] G019 [P] [GA4] [Compat-Blocker] Verify `RbacAuthorizationFilter`, `RbacProjectResolver`, `CurrentRbacContext`, Outbox event types/payloads, the three Outbox processors, Redis keys, ES mappings, and the Casbin model are byte-unchanged → `docs/rbac/verify-existing-runtime-untouched.md`
- [ ] G020 [GA4] [Compat-Blocker] Verify each global write produces the same per-project Outbox events as the equivalent per-project API → `docs/rbac/verify-outbox-reuse.md`
- [ ] G021 [GA4] Produce the Unified Permission Center backward-compatibility sign-off checklist for existing business systems → `docs/rbac/upc-signoff-checklist.md`

### Dependencies

```
GA0 → GA1 → GA2        (MVP: seeded global system + audited cross-project writes via existing services)
GA1 → GA3              (read/discovery reuses the reserved system + existing search)
GA2, GA3 → GA4         (verification needs the full surface)
```

### MVP definition

GA0 + GA1 + GA2 and their `[MVP]`/`[Compat-Blocker]` tasks. Acceptance: a seeded global admin authenticates with `X-Project: __global__`, is authorized by the **unmodified** existing pipeline against a `rbac.global.*` code, and performs a cross-project user/group operation that delegates to the existing management services — producing the normal per-project Outbox events consumed by the existing Redis/ES/Casbin workers, audited by the existing pipeline, with **no** change to any runtime-authorization file.
