<!--
Sync Impact Report
Version change: template -> 1.0.0
Modified principles:
- PRINCIPLE_1_NAME -> I. Layer Boundaries Are Contractual
- PRINCIPLE_2_NAME -> II. MySQL/Casbin Are Permission Truth
- PRINCIPLE_3_NAME -> III. Project Context And Deny-By-Default
- PRINCIPLE_4_NAME -> IV. Compatibility Contracts Are Stable
- PRINCIPLE_5_NAME -> V. Observable, Rebuildable Runtime State
Added sections:
- Technology And Dependency Constraints
- Development Workflow And Agent Handoff
Removed sections:
- Placeholder SECTION_2_NAME
- Placeholder SECTION_3_NAME
Templates requiring updates:
- updated: .specify/templates/plan-template.md
- updated: .specify/templates/spec-template.md
- updated: .specify/templates/tasks-template.md
- not present: .specify/templates/commands/*.md
Follow-up TODOs:
- None
-->

# RBAC Permission Center Constitution

## Core Principles

### I. Layer Boundaries Are Contractual

Rbac.Domain MUST remain dependency-free except for the BCL and MUST contain only
aggregates, value objects, validation rules, and domain concepts. Rbac.Application
MUST depend only on Rbac.Domain and framework abstractions needed for contracts;
it MUST define interfaces and orchestration, and MUST NOT reference
Rbac.Infrastructure.*. Each Rbac.Infrastructure.* project MUST implement
Application contracts for one technology boundary and MUST NOT reference another
Infrastructure project. Rbac.Api and Rbac.Worker are composition roots; they MAY
reference Application and Infrastructure projects for DI, but MUST NOT contain
business rules or bypass Application services.

Rationale: the project relies on strict clean architecture so later agents can
change MySQL, Redis, Elasticsearch, Casbin, or host wiring without leaking those
details into domain and application logic.

### II. MySQL/Casbin Are Permission Truth

MySQL is the management and persistence truth for RBAC configuration. Casbin
policies are loaded from MySQL-derived relations and used for runtime decisions;
Casbin adapters or Redis keys MUST NOT become a separate business truth source.
Redis permset data is a derived hot-path artifact and MUST only be written by
MySQL/Casbin-derived builders with version checks. Elasticsearch is query-only
for management search and audit retrieval; it MUST NOT participate in real-time
authorization and MUST NOT be used as save truth.

Rationale: permissions must remain auditable, rebuildable, and consistent across
write paths, cache invalidation, runtime checks, and management queries.

### III. Project Context And Deny-By-Default

Every protected API request MUST pass through JWT user resolution, centralized
project extraction, project authorization, and CurrentRbacContext creation before
business services run. Business services MUST read project and userid only from
CurrentRbacContext, not from raw headers, query strings, route values, or bodies.
API authorization MUST be deny-by-default unless a route is matched by the
centralized anonymous or allowlist policy. Super authority MUST be scoped to a
project; global super authority MUST NOT exist.

Rationale: scattered project parsing or permissive unmapped APIs create silent
cross-project authorization defects.

### IV. Compatibility Contracts Are Stable

External API responses MUST use the unified envelope `code/msg/data/time`, and
list responses MUST return `data.list` and `data.total`. DxEId/DxE_id values MUST
be serialized as strings in all public DTOs and mapped as keyword values in
Elasticsearch; they MUST NOT be emitted as JSON numbers. DxEId exists for
frontend compatibility, editing, deleting, sorting, and migration tracing; long
term permission decisions MUST use stable `permissionCode`, `ruleCode`, `userid`,
and `project` values. The RBAC center MUST NOT issue refresh tokens, read legacy
PHP batoken, implement siteConfig or terminal login fields, analyze member RBAC,
or copy PHP permission logic as source of truth.

Rationale: existing intranet frontends need predictable contracts, while the new
system must avoid preserving unsafe or obsolete legacy semantics.

### V. Observable, Rebuildable Runtime State

Runtime state MUST be recoverable from truth sources. Elasticsearch MUST support
full reindex with alias switching and Outbox-driven incremental sync. Cache
changes MUST use targeted invalidation, versioning, or lazy rebuilds, and MUST
avoid scanning all user keys or storing all users' permissions under one Redis
key. FusionCache MAY wrap medium-grained objects such as snapshots, API maps,
project grants, and menu trees, but high-frequency `SISMEMBER`, version
increments, locks, and Pub/Sub MUST use StackExchange.Redis directly. Hot
authorization paths MUST emit non-blocking allow, deny, and error audit events.

Rationale: the system is intended to support large projects and operational
recovery without turning caches or indexes into fragile hidden state.

## Technology And Dependency Constraints

The project targets .NET 6 with C# 10, nullable reference types enabled, and
central package management in `Directory.Packages.props`. Package versions MUST
be changed centrally. NEST MUST remain on 7.17.x for Elasticsearch 7
compatibility; agents MUST NOT migrate to `Elastic.Clients.Elasticsearch` unless
the constitution and design documents are amended first. EF Core and Pomelo
major versions MUST remain compatible, and database schema changes MUST be
managed by SQL scripts or DBA-owned process, not by generating EF migrations.

ABP Framework MUST NOT be introduced. Rbac.Domain and Rbac.Application nullable
warnings configured as errors MUST be treated as quality gates, not bypassed.
Redis keys MUST be project and user scoped where applicable, especially
`rbac:permset:{project}:{userid}`. Casbin MUST use the RBAC-with-domains model,
including project/domain-aware grouping policy semantics.

## Development Workflow And Agent Handoff

Agents MUST read this constitution before implementing feature work, then inspect
the affected project files and current design documents under `spec/main` and
`spec/main/plan`. Existing user or agent changes in the worktree MUST be
preserved; unrelated moves or edits MUST NOT be reverted. New work MUST follow
the current project layout at repository root: `Rbac.Api`, `Rbac.Application`,
`Rbac.Domain`, `Rbac.Infrastructure.MySql`, `Rbac.Infrastructure.Redis`,
`Rbac.Infrastructure.Elasticsearch`, `Rbac.Infrastructure.Casbin`, and
`Rbac.Worker`.

Plans and tasks MUST include a Constitution Check covering layer boundaries,
truth-source rules, project context, compatibility contracts, observability, and
dependency constraints. Implementation tasks touching authorization, caching,
Outbox, Elasticsearch, DTO serialization, or public API contracts MUST include a
verification task. Agents MUST run at least `dotnet build Rbac.sln` after code
changes when dependencies are available; if local infrastructure prevents deeper
tests, the limitation MUST be reported clearly.

## Governance

This constitution supersedes conflicting informal guidance and generated task
text. Design documents and implementation tasks MUST be amended when they
conflict with these rules. Any amendment MUST include the reason, affected
principles or sections, expected migration impact, and template synchronization
status.

Versioning follows semantic rules. MAJOR versions cover incompatible changes to
truth sources, authorization semantics, dependency boundaries, or public
compatibility contracts. MINOR versions cover new principles, new mandatory
workflow gates, or materially expanded constraints. PATCH versions cover wording,
clarifications, and non-semantic corrections. Reviews MUST reject code or specs
that violate a MUST rule unless the constitution is amended first.

**Version**: 1.0.0 | **Ratified**: 2026-05-11 | **Last Amended**: 2026-05-11
