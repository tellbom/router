# Wave 4 — Program.cs DI 补丁说明

Wave 4 对两个 Program.cs（来自 wave3-fix）仅新增 **1 行**注册。
不重复输出全文，只说明需要在哪里插入。

## Rbac.Api/Program.cs 和 Rbac.Worker/Program.cs

在 ES 相关注册块（`builder.Services.AddSingleton<RbacEsFullReindexService>()`
之后）新增：

```csharp
// PATCH-11: RbacEsAliasPreflightChecker 已在 RbacEsBootstrap.cs 实现，注册供 RbacEsFullReindexService 注入
builder.Services.AddSingleton<RbacEsAliasPreflightChecker>();
```

Worker/Program.cs 同理，在 `services.AddSingleton<RbacEsFullReindexService>()` 之后加：

```csharp
services.AddSingleton<RbacEsAliasPreflightChecker>();
```

### 说明

- `RbacEsAliasPreflightChecker` 构造函数：`(IElasticClient, ILogger<RbacEsAliasPreflightChecker>)`，
  两个依赖均已在 DI 容器中注册，直接 `AddSingleton` 即可。
- `RbacEsFullReindexService` 构造函数在 Wave 4 新增了两个参数
  `IApiPermissionMapRepository`（Wave 1 已注册）和 `RbacEsAliasPreflightChecker`（本行新增），
  DI 会自动注入，无需其他改动。
