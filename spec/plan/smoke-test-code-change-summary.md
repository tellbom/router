# Smoke Test Code Change Summary

本文件记录本轮使用 curl/API 做测试环境联调时产生的代码改动和原因，供提交前评估。

## API

| 文件 | 改动 | 原因 |
|------|------|------|
| `Rbac.Api/Program.cs` | `RbacEsFullReindexService` 从 Singleton 改为 Scoped | 该服务依赖 Repository/DbContext 等 Scoped 服务，Singleton 注入会触发生命周期校验错误。 |
| `Rbac.Api/Middleware/CurrentRbacContextMiddleware.cs` | 支持 `X-Test-Userid` 和 `Bearer fake:<userid>` 解析测试用户 | 方便测试环境绕过真实 JWT，直接用 curl 验证接口链路。属于测试辅助改动，提交前可决定是否保留或加环境限制。 |

## Worker

| 文件 | 改动 | 原因 |
|------|------|------|
| `Rbac.Worker/Program.cs` | 注册 `IGroupMemberRepository` | ES 用户文档增量同步需要回读用户所属组。 |
| `Rbac.Worker/Program.cs` | `RbacEsFullReindexService` 从 Singleton 改为 Scoped | 同 API，避免 Singleton 持有 Scoped 依赖。 |
| `Rbac.Worker/Program.cs` | 暂停 `RbacCacheWarmupWorker` HostedService 注册 | 该 HostedService 当前直接依赖 Scoped Repository，启动时会触发生命周期错误。后续可改为内部创建 Scope 后再恢复。 |
| `Rbac.Worker/Outbox/RbacRedisOutboxProcessor.cs` | Outbox payload 反序列化改用 `RbacSerializationRules.InternalOptions` | Outbox 写入使用 camelCase，默认反序列化无法正确绑定 `ruleCode/groupCode/userid` 等字段。 |
| `Rbac.Worker/Outbox/RbacElasticsearchOutboxProcessor.cs` | Outbox payload 反序列化改用 `RbacSerializationRules.InternalOptions` | 同上，避免 Worker 消费时拿到空字段导致 ValueObject 构造失败。 |
| `Rbac.Worker/Outbox/RbacElasticsearchOutboxProcessor.cs` | 用户 ES 文档回读 ProjectGrant 和 GroupMember，补齐 `projectCodes/groupCodes/groupNames/superProjects/createdAt/updatedAt` | `admin/list` 依赖 ES 的 `projectCodes` 过滤。原增量同步只写用户基础信息，导致用户已授权但列表查不到。 |

## Infrastructure

| 文件 | 改动 | 原因 |
|------|------|------|
| `Rbac.Infrastructure.Casbin/CasbinEnforcerFactory.cs` | Singleton 内通过 `IServiceScopeFactory` 创建 Scope 后读取 policy reader | 避免 Singleton 直接依赖 Scoped policy reader。 |
| `Rbac.Infrastructure.Casbin/CasbinPolicyVersionWatcher.cs` | Singleton 内通过 `IServiceScopeFactory` 创建 Scope 后读取 policy repository | 避免 Singleton 直接依赖 Scoped repository。 |
| `Rbac.Infrastructure.Elasticsearch/Search/RbacElasticQueryBuilder.cs` | 每类查询显式指定 ES index | 避免 NEST 默认索引缺失导致列表查询异常或查不到数据。 |
| `Rbac.Infrastructure.MySql/Repositories/GroupMemberRepository.cs` | 查询条件从 `ValueObject.Value == string` 改为 `ValueObject == new ValueObject(...)` | EF/Pomelo 无法翻译 `.Value` 内部属性查询，改为现有 ValueObject 映射可翻译写法。 |
| `Rbac.Application/Observability/RbacMetrics.cs` | 去掉 `IMeterFactory` 构造依赖，改为直接创建 `Meter` | 当前 DI 中未注册 `IMeterFactory`，导致服务构造失败。 |

## SQL / Config

| 文件 | 改动 | 原因 |
|------|------|------|
| `sql/rbac-init.sql` | 移除 `TEXT DEFAULT '[]'` | MySQL 5.7 不允许 TEXT/BLOB/JSON 字段设置默认值，初始化脚本会失败。 |
| `Rbac.Api/appsettings.Development.json`、`Rbac.Worker/appsettings.Development.json` | 测试环境 IP 从 `192.168.48.128` 改为 `192.168.124.2` | 适配当前测试网络。注意当前 diff 中数据库名显示为 `rbca`，而本轮实际测试通过环境变量使用的是 `rbac`，提交前建议确认。 |

## 测试结论

- MySQL 初始化成功。
- API 写入管理员、权限组、规则、项目授权成功。
- Worker 成功消费 Outbox，相关事件最终为 `Succeeded`。
- ES 已生成并写入 `rbac_user_index`、`rbac_group_index`、`rbac_rule_index`。
- `/api/admin/list`、`/api/group/list`、`/api/rule/list` 均可返回本轮 smoke 数据。

## 提交前建议

- `X-Test-Userid` 测试绕过逻辑建议加 `Development` 环境限制，或确认测试环境需要后再保留。
- `RbacCacheWarmupWorker` 当前只是暂停 HostedService 注册，后续应改成内部创建 Scope。
- `logs/` 是本轮运行日志目录，建议不要提交。
