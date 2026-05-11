# RBAC 数据库切换 PostgreSQL / Oracle 影响评估

## 评估结论

当前 RBAC 项目的基础架构对关系型数据库迁移总体友好：Domain 与 Application 层基本不感知具体数据库，API/Worker 通过 EF Core 与 Outbox 契约访问 MySQL 真相库，Redis、Elasticsearch、Casbin 均属于派生状态或外部同步目标。

总体判断：核心业务逻辑不需要重写，但切换 PostgreSQL 或 Oracle 时有 6 个明确改动面。最大工作量不在业务代码，而在 DDL 初始化脚本、数据库方言、时间戳自动更新、种子数据幂等写入和集成测试。

## 当前项目上下文

### API 与 Worker 的数据库职责

API 项目负责同步处理请求，写入 MySQL 业务表，并在同一个 `RbacDbContext` 中追加 Outbox 事件。`OutboxReaderWriter.Append` 只把事件加入 `DbContext`，不单独 `SaveChanges`，由业务写服务在业务表和 Outbox 都准备好后统一提交。

Worker 项目是独立 Generic Host，不依赖 API 启动。它轮询 `rbac_outbox` 中 `Status=Pending` 且到达重试时间的事件，依次执行 Redis 缓存失效/版本更新、Elasticsearch 增量同步、Casbin policy 刷新，成功后标记 `Succeeded`，失败后按重试策略保留 `Pending` 或进入 `Failed`。

因此，跨库迁移必须保持这个边界：

```text
API：写关系型真相库 + 写 Outbox
Worker：轮询 Outbox + 同步 Redis / ES / Casbin
关系型数据库：最终真相
Redis / ES / Casbin：异步派生状态
```

### 已确认的 MySQL 绑定点

| 位置 | 当前事实 | 迁移影响 |
|------|----------|----------|
| `Rbac.Infrastructure.MySql/Rbac.Infrastructure.MySql.csproj` | 引用 `Pomelo.EntityFrameworkCore.MySql` | 需要替换为目标数据库 EF Provider |
| `Rbac.Worker/Rbac.Worker.csproj` | 引用 `Hangfire.MySqlStorage` | 如继续使用 Hangfire，需要替换 Storage 包 |
| `Rbac.Api/Program.cs` | 使用 `UseMySql(...)` | 需要替换 DbContext Provider |
| `Rbac.Worker/Program.cs` | 使用 `UseMySql(...)` | 需要替换 DbContext Provider |
| `Rbac.Infrastructure.MySql/Outbox/RbacOutboxMapping.cs` | `Payload` 映射为 `longtext` | 需要替换为目标数据库文本大字段类型 |
| `sql/rbac-init.sql` | MySQL DDL、反引号、`DATETIME(6)`、`TINYINT(1)`、`ON UPDATE`、`INSERT IGNORE` | 需要整体重写 |
| `Rbac.Application/Repositories/IProjectGrantMySqlReader.cs` | Application 接口名包含 `MySql` | 功能不受影响，但跨库后命名误导 |
| `Rbac.Application/Snapshots/IRbacPermsetBuilder.cs` | `PermsetInputSource.MySqlCasbinDerived` | 功能不受影响，但跨库后命名误导 |

## 改动面 1：NuGet 包替换

当前锁定两个 MySQL 专属包：

- `Pomelo.EntityFrameworkCore.MySql`
- `Hangfire.MySqlStorage`

切 PostgreSQL 时建议替换为：

- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Hangfire.PostgreSql`

切 Oracle 时建议替换为：

- `Oracle.EntityFrameworkCore`
- `Hangfire.OracleStorage` 或项目确认可维护的 Oracle Hangfire Storage

影响范围低，主要是 `Directory.Packages.props`、`Rbac.Infrastructure.MySql.csproj`、`Rbac.Worker.csproj`。如果数据库项目仍命名为 `Rbac.Infrastructure.MySql`，建议同步评估是否重命名为 `Rbac.Infrastructure.Relational`、`Rbac.Infrastructure.PostgreSql` 或 `Rbac.Infrastructure.Oracle`。

## 改动面 2：DbContext Provider 配置

当前 API 和 Worker 均使用 MySQL Provider：

```csharp
opt.UseMySql(connStr, ServerVersion.AutoDetect(connStr))
```

切 PostgreSQL：

```csharp
opt.UseNpgsql(connStr)
```

切 Oracle：

```csharp
opt.UseOracle(connStr)
```

影响文件：

- `Rbac.Api/Program.cs`
- `Rbac.Worker/Program.cs`

这是低风险改动，但 API 和 Worker 必须同时修改，否则 Worker 会无法消费同一套关系型真相库。

## 改动面 3：EF Core Mapping 方言

当前已确认的显式 MySQL 字段类型只有一处：

```csharp
b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("longtext").IsRequired();
```

建议替换：

| 目标数据库 | 建议类型 |
|------------|----------|
| PostgreSQL | `text` |
| Oracle | `CLOB` |

其余 Mapping 主要使用 `HasMaxLength`、`HasDefaultValue`、`HasConversion` 等 EF Core 通用 API，迁移风险较低。

## 改动面 4：DDL 初始化脚本整体重写

`sql/rbac-init.sql` 当前是 MySQL DDL，包含 7 张核心表：

- `rbac_administrator`
- `rbac_group`
- `rbac_group_member`
- `rbac_rule`
- `rbac_project_grant`
- `rbac_api_permission_map`
- `rbac_outbox`

迁移 PostgreSQL / Oracle 时应整体重写，而不是逐行替换。主要差异如下：

| MySQL 语法 | PostgreSQL 等效 | Oracle 等效 |
|------------|-----------------|-------------|
| 反引号标识符 | 双引号或不引用小写标识符 | 双引号或不引用大写规范标识符 |
| `CHAR(36)` 存 GUID | `uuid` 或 `varchar(36)` | `varchar2(36)` |
| `DATETIME(6)` | `timestamptz` | `timestamp with time zone` |
| `TINYINT(1)` 存 bool | `boolean` | `number(1)` |
| `LONGTEXT` | `text` | `clob` |
| `ON UPDATE CURRENT_TIMESTAMP` | 触发器或 EF 拦截器 | 触发器或 EF 拦截器 |
| `ENGINE=InnoDB` / `utf8mb4` | 无需迁移 | 无需迁移 |
| `INSERT IGNORE` | `insert ... on conflict do nothing` | `merge into` |

`updated_at` 自动更新时间是 DDL 迁移中最容易遗漏的点。可选方案：

- 数据库触发器：更贴近当前 MySQL DDL 行为，但 PostgreSQL 与 Oracle 要分别维护触发器语法。
- EF Core `SaveChanges` 拦截器：减少数据库脚本差异，但要求所有写入都经过 EF Core。

当前项目强调 DBA 管理独立 SQL 脚本，因此如果继续坚持 SQL 初始化脚本为权威，应优先为 PostgreSQL / Oracle 分别提供完整 DDL。

## 改动面 5：`IProjectGrantMySqlReader` 命名

`IProjectGrantMySqlReader` 定义在 Application 层，接口本身没有 MySQL API 依赖，但名称包含 `MySql`。迁移后建议改名为：

```csharp
IProjectGrantReader
```

对应实现可按目标数据库命名：

- `ProjectGrantPostgreSqlReader`
- `ProjectGrantOracleReader`
- 或统一 `ProjectGrantRelationalReader`

这是低风险重命名，影响注入点和实现类引用。功能逻辑不需要变化。

## 改动面 6：`PermsetInputSource.MySqlCasbinDerived` 命名

`PermsetInputSource.MySqlCasbinDerived` 的真实语义是：permset 只能由关系型真相库中的授权关系加 Casbin policy 派生，不允许来自前端、Redis 或 Elasticsearch。

切库后守卫逻辑不变，但名称建议改为：

```csharp
RelationalDbCasbinDerived
```

该改动是可选项。保留原名不影响运行，但会让跨库后的架构表达变得不准确。

## 零影响或低影响区域

| 模块 | 影响判断 |
|------|----------|
| `Rbac.Domain` | 纯业务模型，无数据库 Provider 依赖 |
| `Rbac.Application` 大部分接口与服务 | 依赖抽象，不引用 EF Provider |
| Redis / FusionCache | 与关系型数据库 Provider 无关 |
| Elasticsearch | 通过 Worker 或服务回读关系型真相后同步，不依赖 MySQL 方言 |
| Casbin | 通过 reader 接口读取 policy，不直接绑定 MySQL Provider |
| 雪花 ID 生成器 | 纯算法，不依赖 MySQL 自增或序列 |
| Controller / Middleware | 通过服务层访问，不直接感知数据库 |
| Outbox 消费链 | 依赖 `IOutboxReader` 契约，业务流程不感知底层数据库 |

## 工作量预估

| 目标数据库 | 预估工作量 | 主要成本 |
|------------|------------|----------|
| PostgreSQL | 1-2 天 | Provider 替换、DDL 重写、`updated_at` 策略、集成测试 |
| Oracle | 2-3 天 | DDL 差异更大，`CLOB`、触发器、标识符、时间类型、分页与 Provider 行为验证 |

## 建议迁移顺序

1. 先确认目标数据库与命名策略：PostgreSQL / Oracle，以及是否重命名 `Rbac.Infrastructure.MySql`。
2. 抽象 DbContext Provider 注册，避免 API 和 Worker 各自手写一份 Provider 配置。
3. 替换 NuGet Provider 与 `UseMySql`。
4. 重写 `sql/rbac-init.sql` 为目标数据库版本。
5. 处理 `Payload` 字段大文本类型。
6. 可选重命名 `IProjectGrantMySqlReader` 与 `MySqlCasbinDerived`。
7. 跑 API 写入、Outbox 轮询、Redis/ES/Casbin 同步的端到端集成测试。

## 风险提示

所有绕过 API 写服务、直接修改业务表的操作，都必须同步写入 Outbox 或触发等效事件，否则 Worker 不会感知权限变更。这个约束与数据库类型无关，迁移后仍然成立。
