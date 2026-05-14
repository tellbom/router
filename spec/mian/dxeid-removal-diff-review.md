# DxEId Removal Diff Review

## Purpose

This document summarizes the current git diff produced from `spec/plan/dxeid-removal-scope.md`.

The core direction is to fully remove the legacy `DxEId` / `dxe_id` compatibility identifier from runtime code, API contracts, MySQL schema, Elasticsearch documents, and seed data. Public write routes now use natural business keys:

| Entity | Old route key | New route key |
| --- | --- | --- |
| Administrator | `{dxeId}` | `{userid}` |
| Group | `{dxeId}` | `{groupCode}` |
| Rule | `{dxeId}` | `{ruleCode}` |

Internal `Guid Id` primary keys are intentionally preserved. Permission logic based on `permissionCode`, `ruleCode`, Casbin, Redis permission sets, and project context is not intentionally changed.

## Diff Scale

Current `git diff --stat` shows:

- 41 tracked files changed
- 156 insertions
- 662 deletions
- 4 DxEId-only files deleted
- `spec/plan/dxeid-removal-scope.md` is currently untracked and used as the implementation basis
- `artifacts/` is also untracked and contains local test logs

Deleted files:

- `Rbac.Application/Identity/IRbacDxEIdGenerator.cs`
- `Rbac.Application/Identity/RbacDxEIdImportPolicy.cs`
- `Rbac.Domain/Validation/RbacDxEIdUniquenessRules.cs`
- `Rbac.Infrastructure.MySql/Identity/RbacDxEIdGenerationOptions.cs`

## Runtime Model Changes

### Domain

`DxEId` has been removed as a value object and as an aggregate property.

Changed files:

- `Rbac.Domain/ValueObjects/RbacValueObjects.cs`
- `Rbac.Domain/Users/RbacAdministrator.cs`
- `Rbac.Domain/Groups/RbacGroup.cs`
- `Rbac.Domain/Rules/RbacRule.cs`
- `Rbac.Domain/Validation/RbacIdentityValidationRules.cs`

Main changes:

- Removed `DxEId` record.
- Removed `DxEId` properties from administrator, group, and rule aggregates.
- Removed `DxEId` parameters from factory methods:
  - `RbacAdministrator.Create`
  - `RbacGroup.Create`
  - `RbacRule.CreateMenu`
  - `RbacRule.CreateButton`
- Updated comments so the runtime identity model no longer documents DxEId as a supported frontend identifier.

Review focus:

- Confirm no runtime domain invariant still expects `DxEId`.
- Confirm natural business keys still have adequate validation and uniqueness coverage.

### Application

The application layer now loads write targets by natural keys instead of DxEId.

Changed files include:

- `Rbac.Application/Management/RbacManagementWriteGuard.cs`
- `Rbac.Application/Repositories/RbacRepositoryContracts.cs`
- `Rbac.Application/Contracts/Compatibility/BackendIndexDtos.cs`
- `Rbac.Application/Contracts/Compatibility/FrontendCompatibilityContracts.cs`
- `Rbac.Application/Contracts/Menus/RbacMenuDtos.cs`
- `Rbac.Application/Mapping/RbacCompatibilityMappers.cs`
- `Rbac.Application/Menus/RbacMenuBuilder.cs`
- `Rbac.Application/Backend/RbacBackendIndexService.cs`
- `Rbac.Application/Outbox/RbacOutboxEvents.cs`
- `Rbac.Application/Search/IRbacManagementSearchService.cs`
- `Rbac.Application/Serialization/RbacSerializationRules.cs`

Main changes:

- Removed repository contract methods:
  - `FindByDxEIdAsync`
- Replaced guard methods:
  - `LoadAdminByDxEIdAsync` -> `LoadAdminByUseridAsync`
  - `LoadGroupByDxEIdAsync` -> `LoadGroupByCodeAsync`
  - `LoadRuleByDxEIdAsync` -> `LoadRuleByCodeAsync`
- Removed DxEId fields from DTOs and compatibility mappings.
- Removed DxEId from menu DTO construction and backend index responses.
- Removed DxEId from outbox payload shape.
- Removed `LongToStringConverter` and `NullableLongToStringConverter`, which existed for long-to-string DxEId serialization.

Review focus:

- Confirm removing the long converters does not affect any non-DxEId long fields that still need string serialization.
  - Resolved: `ApiResponse.Time` and `PagedData.Total` are expected to remain JSON numbers. They are documented as numeric `long` values in `README.md`, actual smoke responses returned numbers, and both Unix-second timestamps and normal pagination totals are within JavaScript safe integer range.
- Confirm compatibility DTOs no longer promise `dxeId` to frontend callers.
- Confirm write guard still prevents stale ES data from being used as the write source of truth.

## Persistence And Search

### MySQL

Changed files:

- `Rbac.Infrastructure.MySql/Mapping/RbacEntityMappings.cs`
- `Rbac.Infrastructure.MySql/Repositories/RbacRepositories.cs`
- `Rbac.Infrastructure.MySql/Management/RbacManagementWriteService.cs`

Main changes:

- Removed EF mappings for `dxe_id` columns on:
  - `rbac_administrator`
  - `rbac_group`
  - `rbac_rule`
- Removed unique indexes:
  - `ux_admin_dxe_id`
  - `ux_group_dxe_id`
  - `ux_rule_dxe_id`
- Removed repository implementations for `FindByDxEIdAsync`.
- Removed DxEId generator injection and generation from write/create flows.

Review focus:

- Confirm existing unique constraints on natural keys are sufficient:
  - `ux_admin_userid`
  - `ux_group_code_project`
  - `ux_rule_code_project`
- Confirm no EF query or projection still references `DxEId`.

### Elasticsearch

Changed files:

- `Rbac.Infrastructure.Elasticsearch/Documents/RbacEsDocuments.cs`
- `Rbac.Infrastructure.Elasticsearch/Indexes/RbacUserIndexMapping.cs`
- `Rbac.Infrastructure.Elasticsearch/Indexes/RbacGroupIndexMapping.cs`
- `Rbac.Infrastructure.Elasticsearch/Indexes/RbacRuleIndexMapping.cs`
- `Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsFullReindexService.cs`
- `Rbac.Infrastructure.Elasticsearch/Services/RbacManagementSearchService.cs`
- `Rbac.Worker/Outbox/RbacElasticsearchOutboxProcessor.cs`

Main changes:

- Removed `DxEId` document fields and `dxe_id` index mappings.
- Removed `dxe_id` from `allText` / copy-to search content.
- Removed DxEId assignments from full reindex and outbox indexing.
- Preserved ES document `_id` behavior based on internal `Guid.ToString()`.

Review focus:

- Confirm index recreation/reindexing is planned for environments that already have old mappings.
- Confirm deleting ES docs still uses the unchanged internal Guid document id.

## API Contract Changes

Changed files:

- `Rbac.Api/Controllers/AdminController.cs`
- `Rbac.Api/Controllers/GroupController.cs`
- `Rbac.Api/Controllers/RuleController.cs`
- `Rbac.Api/Controllers/ControllersAdditions.cs`
- `Rbac.Api/Controllers/ApiMapController.cs`
- `Rbac.Api/Controllers/AuthController.cs`
- `Rbac.Api/Program.cs`
- `Rbac.Worker/Program.cs`
- `README.md`

Main changes:

- Removed `IRbacDxEIdGenerator` injection from controllers and DI.
- Updated administrator write routes from `dxeId` to `userid`.
- Updated group write/member routes from `dxeId` to `groupCode`.
- Updated rule write/status/weigh/delete routes from `dxeId` to `ruleCode`.
- Updated create responses:
  - admin returns `{ userid }`
  - group returns `{ groupCode }`
  - rule returns `{ ruleCode }`
- Updated `README.md` endpoint documentation and response field descriptions.
- `ApiMapController` had unused DxEId generator injection removed. Its public route model remains Guid-based and unrelated to DxEId.

Review focus:

- Confirm frontend callers are ready for the route parameter changes.
- Confirm route authorization map entries in database/seeds match the new route templates.
- Confirm no route ambiguity was introduced by natural keys.

## SQL And Seed Data

Changed files:

- `sql/rbac-init.sql`
- `sql/rbac-bootstrap.sql`

Main changes:

- Removed `dxe_id` columns and unique indexes from initial schema.
- Removed `dxe_id` values from bootstrap inserts.
- Updated seeded API permission routes:
  - `/api/admin/{dxeId}` -> `/api/admin/{userid}`
  - `/api/group/{dxeId}` -> `/api/group/{groupCode}`
  - `/api/rule/{dxeId}` -> `/api/rule/{ruleCode}`

Actual database migration already executed on the current test database:

```sql
ALTER TABLE rbac_administrator DROP INDEX ux_admin_dxe_id, DROP COLUMN dxe_id;
ALTER TABLE rbac_group DROP INDEX ux_group_dxe_id, DROP COLUMN dxe_id;
ALTER TABLE rbac_rule DROP INDEX ux_rule_dxe_id, DROP COLUMN dxe_id;
```

The actual `rbac_api_permission_map` table was also updated to use `{userid}`, `{groupCode}`, and `{ruleCode}`.

Review focus:

- Confirm production migration SQL should be captured separately if this change is promoted.
- Confirm `INSERT IGNORE` seed behavior does not leave old route templates in already-initialized databases.

## Residual DxEId Search Results

`rg -n "dxeId|DxEId|dxe_id|dxeid"` no longer reports runtime code paths. Remaining hits are in historical planning/task/spec documents, including:

- `spec/task/tasks.md`
- `spec/constitution/constitution.md`
- older `spec/plan/*.md` documents
- current `spec/plan/dxeid-removal-scope.md`

These are documentation/history references, not compiled runtime references. Claude should still decide whether old planning documents should be updated or left as historical context before final commit.

## Verification Already Performed

Build:

```powershell
dotnet build Rbac.Api\Rbac.Api.csproj --no-restore
```

Result:

- Build succeeded
- 0 errors
- NuGet source mapping warnings only

API smoke test against the actual MySQL database after dropping `dxe_id`:

- `POST /api/auth/login`
- `GET /api/admin/index`
- `GET /api/admin/list?page=1&pageSize=5`
- `GET /api/group/index?select=true`
- `GET /api/group/list?page=1&pageSize=5`
- `POST /api/rule`
- `PUT /api/rule/{ruleCode}/status`
- `PUT /api/rule/{ruleCode}/weigh`
- `DELETE /api/rule/{ruleCode}`
- `POST /api/group`
- `PUT /api/group/{groupCode}/status`
- `DELETE /api/group/{groupCode}`
- `POST /api/admin`
- `PUT /api/admin/{userid}/username`
- `PUT /api/admin/{userid}/status`
- `DELETE /api/admin/{userid}`

Result:

- All listed requests returned HTTP 200
- All listed business responses returned `code: 0`
- Response bodies did not contain `dxeId` or `dxe_id`
- Temporary smoke data was deleted

Database verification after tests:

- No `dxe_id`, `dxeId`, or `dxeid` columns found in current `rbac` schema
- No `%dxe%` indexes found
- No `%dxe%` route patterns found in `rbac_api_permission_map`
- No tested smoke admin/group/rule records remained

Operational note:

- `/ops/health?project=oversia` returned HTTP 200 with status `degraded` because outbox had pending items. This does not appear related to DxEId removal.

## Suggested Claude Review Checklist

- Check runtime code for any missed `DxEId`, `dxeId`, `dxe_id`, or `IRbacDxEIdGenerator` references.
- Check that all public write routes and README examples consistently use natural keys.
- Check that route authorization patterns in SQL seed data and the actual database align with controller routes.
- Check that removing long JSON converters does not change unrelated API serialization behavior. Current conclusion: `time` and `total` should be JSON numbers, so no dedicated converter is required for those fields.
- Check that ES mapping removal is paired with an index migration/reindex plan for existing deployments.
- Check whether historical spec documents should be updated, archived, or left untouched before commit.
- Check whether `artifacts/` should be deleted or ignored before committing.
