# Global Read Reuse

## G016 结论

GA3 不需要修改 Elasticsearch 查询层。

`RbacElasticQueryBuilder.Terms()` 对 `null` 或空值会跳过过滤条件。因此 Global API 的跨 project 搜索只需保持 `query.Project = null`，即可复用现有 `IRbacManagementSearchService` 实现全项目读取。

已复用的端点：

- `GET /api/global/user/list`
- `GET /api/global/group/list`
- `GET /api/global/menu/list`

`GET /api/global/project/list` 通过 `IProjectGrantRepository.GetDistinctProjectsAsync()` 从 `rbac_project_grant` 推导项目列表，并使用 `RbacGlobalConstants.IsReservedProject()` 排除 `__global__`。
