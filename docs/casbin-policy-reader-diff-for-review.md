# Casbin Policy Reader 修复 Diff 评估材料

## 背景

旧权限数据通过 API 导入后，Worker 执行 Cache Warmup / Casbin reload 时暴露 EF Core 查询翻译异常：

```text
The LINQ expression ... m.Project.Value == project.Value could not be translated.
```

根因是 MySQL EF Core 查询中直接访问值对象的 `.Value`，部分表达式无法被 Pomelo/EF Core 翻译为 SQL。

## 修改目标

- 不改 DTO。
- 不改表结构。
- 不改业务语义。
- 仅把数据库侧查询改为 EF 可翻译表达式。
- 值对象 `.Value` 展开移动到内存侧。

## 涉及文件

- `Rbac.Infrastructure.MySql/Policies/CasbinMySqlPolicyReaders.cs`
- `Rbac.Infrastructure.MySql/Repositories/RbacRepositories.cs`

## 关键改动说明

1. `ProjectCode("*")` 仍表示全项目，不追加 project 过滤。
2. 普通 project 过滤从 `m.Project.Value == project.Value` 改为 `m.Project == project`。
3. `PermissionCode.Value`、`Userid.Value`、`GroupCode.Value` 等值对象展开在 `ToListAsync` 后执行，避免 EF 翻译。
4. 查询结果语义保持一致：仍返回 Casbin 所需的 grouping policy 和 permission policy 元组。

## Git Diff

```diff
diff --git a/Rbac.Infrastructure.MySql/Policies/CasbinMySqlPolicyReaders.cs b/Rbac.Infrastructure.MySql/Policies/CasbinMySqlPolicyReaders.cs
index 9d1d50f..a3142d4 100644
--- a/Rbac.Infrastructure.MySql/Policies/CasbinMySqlPolicyReaders.cs
+++ b/Rbac.Infrastructure.MySql/Policies/CasbinMySqlPolicyReaders.cs
@@ -39,16 +39,17 @@ public sealed class CasbinMySqlGroupingPolicyReader : ICasbinGroupingPolicyReade
 
         // 直接从 rbac_group_member 读取 (userid, groupCode, project) 三元组
         // ProjectCode("*") = 全项目，跳过 project 过滤
-        var query = project.Value == "*"
-            ? _db.GroupMembers
-            : _db.GroupMembers.Where(m => m.Project.Value == project.Value);
+        var query = _db.GroupMembers.AsQueryable();
+        if (project.Value != "*")
+            query = query.Where(m => m.Project == project);
 
-        var result = await query
+        var members = await query.ToListAsync(ct);
+        var result = members
             .Select(m => ValueTuple.Create(
                 m.Userid.Value,
                 m.GroupCode.Value,
                 m.Project.Value))
-            .ToListAsync(ct);
+            .ToList();
 
         _logger.LogDebug(
             "GroupingPolicy loaded project={P} rows={N}", project.Value, result.Count);
@@ -82,16 +83,19 @@ public sealed class CasbinMySqlPermissionPolicyReader : ICasbinPermissionPolicyR
     {
         _logger.LogDebug("LoadPermissionPolicy project={P}", project.Value);
 
-        var groups = await _db.Groups
-            .Where(g => (project.Value == "*" || g.Project.Value == project.Value)
-                        && g.Status == GroupStatus.Active)
-            .ToListAsync(ct);
+        var groupsQuery = _db.Groups.Where(g => g.Status == GroupStatus.Active);
+        if (project.Value != "*")
+            groupsQuery = groupsQuery.Where(g => g.Project == project);
 
-        var apiMaps = await _db.ApiPermissionMaps
-            .Where(m => (project.Value == "*" || m.Project.Value == project.Value)
-                        && m.Status == ApiMapStatus.Active)
+        var groups = await groupsQuery.ToListAsync(ct);
+
+        var apiMapsQuery = _db.ApiPermissionMaps.Where(m => m.Status == ApiMapStatus.Active);
+        if (project.Value != "*")
+            apiMapsQuery = apiMapsQuery.Where(m => m.Project == project);
+
+        var apiMaps = (await apiMapsQuery.ToListAsync(ct))
             .Select(m => new { PermCode = m.PermissionCode.Value, Action = m.Action })
-            .ToListAsync(ct);
+            .ToList();
 
         var actionLookup = apiMaps
             .GroupBy(m => m.PermCode)
diff --git a/Rbac.Infrastructure.MySql/Repositories/RbacRepositories.cs b/Rbac.Infrastructure.MySql/Repositories/RbacRepositories.cs
index e09d317..9c885a0 100644
--- a/Rbac.Infrastructure.MySql/Repositories/RbacRepositories.cs
+++ b/Rbac.Infrastructure.MySql/Repositories/RbacRepositories.cs
@@ -295,16 +295,19 @@ public sealed class CasbinPolicyRepository : ICasbinPolicyRepository
     public async Task<IReadOnlyList<(string GroupCode, string Project, string PermissionCode, string Action)>>
         GetPermissionPoliciesAsync(ProjectCode project, CancellationToken ct = default)
     {
-        var groups = await _db.Groups
-            .Where(g => (project.Value == "*" || g.Project.Value == project.Value)
-                        && g.Status == GroupStatus.Active)
-            .ToListAsync(ct);
+        var groupsQuery = _db.Groups.Where(g => g.Status == GroupStatus.Active);
+        if (project.Value != "*")
+            groupsQuery = groupsQuery.Where(g => g.Project == project);
+
+        var groups = await groupsQuery.ToListAsync(ct);
 
-        var apiMaps = await _db.ApiPermissionMaps
-            .Where(m => (project.Value == "*" || m.Project.Value == project.Value)
-                        && m.Status == ApiMapStatus.Active)
+        var apiMapsQuery = _db.ApiPermissionMaps.Where(m => m.Status == ApiMapStatus.Active);
+        if (project.Value != "*")
+            apiMapsQuery = apiMapsQuery.Where(m => m.Project == project);
+
+        var apiMaps = (await apiMapsQuery.ToListAsync(ct))
             .Select(m => new { PermCode = m.PermissionCode.Value, Action = m.Action })
-            .ToListAsync(ct);
+            .ToList();
 
         var actionLookup = apiMaps
             .GroupBy(m => m.PermCode)
```

## 验证结果

```text
dotnet build Rbac.Worker\Rbac.Worker.csproj --no-restore
```

结果：构建成功，0 错误。

Worker 启动后日志验证：

```text
GroupingPolicy loaded project=default rows=2
PermissionPolicy loaded project=default policies=53
Cache warmup completed projects=1
```

未再出现：

```text
could not be translated
Warmup failed
Casbin Enforcer reload FAILED
```

## 当前 Git 状态提示

本次可评估代码改动只有上述 2 个文件。当前工作区还存在运行日志和未跟踪测试目录，不属于本次代码修复 diff：

```text
M logs/api-smoke.out.log
?? rules/
?? test/
```
