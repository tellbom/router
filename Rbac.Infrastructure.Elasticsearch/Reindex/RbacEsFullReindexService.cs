using Microsoft.Extensions.Logging;
using Nest;
using Rbac.Application.Auditing;
using Rbac.Application.Mapping;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Elasticsearch.Bootstrap;
using Rbac.Infrastructure.Elasticsearch.Documents;
using Rbac.Infrastructure.Elasticsearch.Indexes;

namespace Rbac.Infrastructure.Elasticsearch.Reindex;

/// <summary>
/// ES 全量重建服务。
///
/// PATCH-10: 补全 permission_view / audit_log 索引重建。
/// PATCH-11: 注入 RbacEsAliasPreflightChecker，在 ExecuteReindexAsync 入口执行 preflight。
///           Preflight 类已在 RbacEsBootstrap.cs 中完整实现，本文件仅接入调用。
///
/// 重建流程（设计文档 §3.4 / §7.8）：
/// 0. [PATCH-11] Alias preflight 检查（alias 存在且指向唯一索引）。
/// 1. 创建新物理索引（版本化命名）。
/// 2. 应用 mapping 和 settings。
/// 3. 从 DM 全量读取数据并写入新索引。
/// 4. 校验文档数量。
/// 5. 校验通过后原子切换 alias。
/// 6. 切换失败或校验失败时保留旧索引，不影响当前查询。
/// </summary>
public sealed class RbacEsFullReindexService
{
    private readonly IElasticClient _esClient;
    private readonly IAdministratorRepository _adminRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly IGroupMemberRepository _groupMemberRepo;
    private readonly IProjectGrantRepository _projectGrantRepo;
    private readonly IRuleRepository _ruleRepo;
    private readonly IApiPermissionMapRepository _apiMapRepo; // PATCH-10: permission_view 数据来源
    private readonly RbacEsAliasPreflightChecker _preflight;  // PATCH-11: 已有类，仅接入
    private readonly ILogger<RbacEsFullReindexService> _logger;

    public RbacEsFullReindexService(
        IElasticClient esClient,
        IAdministratorRepository adminRepo,
        IGroupRepository groupRepo,
        IGroupMemberRepository groupMemberRepo,
        IProjectGrantRepository projectGrantRepo,
        IRuleRepository ruleRepo,
        IApiPermissionMapRepository apiMapRepo,
        RbacEsAliasPreflightChecker preflight,
        ILogger<RbacEsFullReindexService> logger)
    {
        _esClient   = esClient;
        _adminRepo  = adminRepo;
        _groupRepo  = groupRepo;
        _groupMemberRepo = groupMemberRepo;
        _projectGrantRepo = projectGrantRepo;
        _ruleRepo   = ruleRepo;
        _apiMapRepo = apiMapRepo;
        _preflight  = preflight;
        _logger     = logger;
    }

    // ── 现有三个索引重建方法（结构不变，只在 ExecuteReindexAsync 中加 preflight）──

    /// <summary>重建用户索引。</summary>
    public async Task<ReindexResult> ReindexUsersAsync(
        string? project = null, CancellationToken ct = default)
    {
        var alias    = RbacUserIndexMapping.IndexName;
        var newIndex = BuildVersionedIndexName(alias);

        return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
        {
            var admins = project is not null
                ? await _adminRepo.FindByProjectAsync(new ProjectCode(project), ct)
                : await _adminRepo.FindByProjectAsync(new ProjectCode("*"), ct);

            var docs = new List<UserDocument>();
            foreach (var admin in admins)
            {
                docs.Add(await BuildUserDocumentAsync(admin, ct));
            }

            await BulkIndexAsync<UserDocument>(newIndex, docs, ct);
            return docs.Count;
        });
    }

    /// <summary>重建权限组索引。</summary>
    private async Task<UserDocument> BuildUserDocumentAsync(
        Rbac.Domain.Users.RbacAdministrator admin,
        CancellationToken ct)
    {
        var grants = await _projectGrantRepo.FindByUseridAsync(admin.Userid, ct);
        var projectCodes = grants.Select(g => g.Project.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var superProjects = grants.Where(g => g.IsSuper)
            .Select(g => g.Project.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groupCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projectCodes)
        {
            var memberships = await _groupMemberRepo.FindByUseridAndProjectAsync(
                admin.Userid.Value, project, ct);
            foreach (var membership in memberships)
            {
                groupCodes.Add(membership.GroupCode.Value);

                var group = await _groupRepo.FindByGroupCodeAsync(
                    membership.GroupCode, membership.Project, ct);
                if (group is not null)
                    groupNames.Add(group.GroupName);
            }
        }

        return new UserDocument
        {
            Id = admin.Id.ToString(),
            Userid = admin.Userid.Value,
            Username = admin.Username,
            ProjectCodes = projectCodes,
            GroupCodes = groupCodes.ToList(),
            GroupNames = groupNames.ToList(),
            Status = admin.Status.ToString(),
            SuperProjects = superProjects,
            CreatedAt = admin.CreatedAt,
            UpdatedAt = admin.UpdatedAt,
        };
    }

    public async Task<ReindexResult> ReindexGroupsAsync(
        string? project = null, CancellationToken ct = default)
    {
        var alias    = RbacGroupIndexMapping.IndexName;
        var newIndex = BuildVersionedIndexName(alias);

        return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
        {
            var groups = await _groupRepo.FindByProjectAsync(
                new ProjectCode(project ?? "*"), ct);

            var docs = groups.Select(g => new GroupDocument
            {
                Id              = g.Id.ToString(),
                Project         = g.Project.Value,
                GroupCode       = g.GroupCode.Value,
                GroupName       = g.GroupName,
                ParentGroupCode = g.ParentGroupCode?.Value,
                RuleCodes       = g.RuleCodes.Select(r => r.Value).ToList(),
                PermissionCodes = g.PermissionCodes.Select(p => p.Value).ToList(),
                Status          = g.Status.ToString(),
            }).ToList();

            await BulkIndexAsync<GroupDocument>(newIndex, docs, ct);
            return docs.Count;
        });
    }

    /// <summary>重建规则索引。</summary>
    public async Task<ReindexResult> ReindexRulesAsync(
        string? project = null, CancellationToken ct = default)
    {
        var alias    = RbacRuleIndexMapping.IndexName;
        var newIndex = BuildVersionedIndexName(alias);

        return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
        {
            var rules = await _ruleRepo.FindActiveByProjectAsync(
                new ProjectCode(project ?? "*"), ct);

            var docs = rules.Select(r => new RuleDocument
            {
                Id            = r.Id.ToString(),
                Project       = r.Project.Value,
                RuleCode      = r.RuleCode.Value,
                PermissionCode = r.PermissionCode.Value,
                ParentRuleCode = r.ParentRuleCode?.Value,
                Title         = r.Title,
                Name          = r.Name,
                Path          = r.Path,
                Icon          = r.Icon,
                Type          = RbacCompatibilityMappers.ToFrontendRuleType(r.Type),
                MenuType      = RbacCompatibilityMappers.ToFrontendMenuType(r.MenuType),
                Component     = r.Component,
                Url           = r.Url,
                Extend        = r.Extend,
                Remark        = r.Remark,
                Keepalive     = r.Keepalive.ToString().ToLowerInvariant(),
                Status        = r.Status.ToString(),
                Weigh         = r.Weigh,
                CreatedAt     = r.CreatedAt,
                UpdatedAt     = r.UpdatedAt,
            }).ToList();

            await BulkIndexAsync<RuleDocument>(newIndex, docs, ct);
            return docs.Count;
        });
    }

    // ── PATCH-10: 新增两个索引重建方法 ───────────────────────────────

    /// <summary>
    /// PATCH-10: 重建权限视图索引（permission_view）。
    ///
    /// 数据来源：rbac_api_permission_map（DM 真相）+ rbac_group（groupCodes/groupNames 关联）。
    /// permission_view 是一个管理端可观性视图，不是运行态鉴权真相。
    ///
    /// ProjectCode("*") 表示全项目读取，由 ApiPermissionMapRepository.FindActiveByProjectAsync 实现。
    /// </summary>
    public async Task<ReindexResult> ReindexPermissionViewAsync(
        string? project = null, CancellationToken ct = default)
    {
        var alias    = RbacPermissionViewIndexMapping.IndexName;
        var newIndex = BuildVersionedIndexName(alias);

        return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
        {
            var projectCode = new ProjectCode(project ?? "*");

            // 从 DM 读取所有 active API 权限映射
            var maps = await _apiMapRepo.FindActiveByProjectAsync(projectCode, ct);

            // 读取所有 active 组，用于关联 permissionCode → groupCodes / groupNames
            var groups = await _groupRepo.FindByProjectAsync(projectCode, ct);

            // 构建 permissionCode → (groupCodes, groupNames) 的查找字典
            var permToGroups = new Dictionary<string, (List<string> Codes, List<string> Names)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups)
            {
                foreach (var pc in g.PermissionCodes)
                {
                    if (!permToGroups.TryGetValue(pc.Value, out var pair))
                    {
                        pair = (new List<string>(), new List<string>());
                        permToGroups[pc.Value] = pair;
                    }
                    pair.Codes.Add(g.GroupCode.Value);
                    pair.Names.Add(g.GroupName);
                }
            }

            // 构建 ES 文档（每条 api_permission_map 记录对应一条 permission_view 文档）
            var docs = maps.Select(m =>
            {
                var key = m.PermissionCode.Value;
                var hasGroupInfo = permToGroups.TryGetValue(key, out var groupInfo);

                return new PermissionViewDocument
                {
                    Project        = m.Project.Value,
                    HttpMethod     = m.HttpMethod,
                    PermissionCode = m.PermissionCode.Value,
                    RuleCode       = string.Empty,          // permission_view 不关联 ruleCode
                    Action         = m.Action,
                    ResourceType   = "api",
                    Path           = m.RoutePattern,
                    GroupCodes     = hasGroupInfo ? groupInfo.Codes : new List<string>(),
                    GroupNames     = hasGroupInfo ? groupInfo.Names : new List<string>(),
                    Status         = m.Status.ToString(),
                    UpdatedAt      = m.UpdatedAt,
                };
            }).ToList();

            await BulkIndexAsync<PermissionViewDocument>(newIndex, docs, ct);
            return docs.Count;
        });
    }

    /// <summary>
    /// PATCH-10: 重建审计日志索引（audit_log）。
    ///
    /// audit_log 的真相数据在 DM 的审计表中，或由实时 Channel 写入 ES。
    /// 全量重建场景（audit_log 索引损坏/恢复）：
    ///   - 如果有独立审计 DM 表，从中读取并写入。
    ///   - 如果审计数据只存在于 ES（当前架构），全量重建时重新建空索引+切换 alias 即可，
    ///     保留已有数据靠 SwitchAliasAsync 不删除原索引文档（alias 切换后旧索引保留）。
    ///
    /// 当前实现：创建新空索引 + 切换 alias（保证 alias 指向唯一索引）。
    /// 后续扩展：如有审计 DM 表，在此方法中回读并 bulk 写入。
    /// </summary>
    public async Task<ReindexResult> ReindexAuditLogAsync(
        string? project = null, CancellationToken ct = default)
    {
        var alias    = RbacAuditLogIndexMapping.IndexName;
        var newIndex = BuildVersionedIndexName(alias);

        // 审计日志的全量重建是"确保索引和 alias 健康"，而非从 DM 批量回填
        // 因为 audit_log 数据量通常极大，且写入是实时流式的
        return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
        {
            _logger.LogInformation(
                "AuditLog reindex: new empty index created. " +
                "Historical audit data remains queryable from old index until alias cut-over. " +
                "Streaming write via RbacAuditEventWorker will populate the new index going forward.");

            // 返回 0：新索引为空，文档数验证跳过（见 ExecuteReindexAsync 中的 0 文档逻辑）
            await Task.CompletedTask;
            return 0;
        });
    }

    // ── 核心重建流程（PATCH-11 在此处插入 preflight）──────────────────

    private async Task<ReindexResult> ExecuteReindexAsync(
        string alias, string newIndex,
        CancellationToken ct,
        Func<Task<int>> buildAndIndexFunc)
    {
        // PATCH-11: Alias preflight 检查
        // RbacEsAliasPreflightChecker.CheckAsync 已在 RbacEsBootstrap.cs 完整实现
        // 仅在此处调用，不重复编写逻辑
        var preflightResult = await _preflight.CheckAsync(alias, ct);
        if (!preflightResult.IsPass)
        {
            _logger.LogError(
                "Reindex aborted: preflight failed alias={Alias} reason={Reason}",
                alias, preflightResult.FailReason);
            return ReindexResult.Failure(alias, newIndex,
                $"Preflight failed: {preflightResult.FailReason}");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "Reindex started alias={Alias} newIndex={Index} currentIndex={Current}",
            alias, newIndex, preflightResult.CurrentIndex);

        try
        {
            // 1. 创建新索引
            await CreateIndexAsync(alias, newIndex, ct);

            // 2. 从数据源写入 ES
            var docCount = await buildAndIndexFunc();

            // 3. 文档数校验（docCount=0 时跳过，audit_log 空索引场景）
            if (docCount > 0)
            {
                await _esClient.Indices.RefreshAsync(newIndex, ct: ct);
                var countResp = await _esClient.CountAsync<object>(
                    c => c.Index(newIndex), ct);
                var esCount = countResp.Count;

                if (esCount < docCount * 0.99)
                {
                    _logger.LogError(
                        "Reindex count mismatch alias={Alias} source={Source} es={Es}",
                        alias, docCount, esCount);
                    await _esClient.Indices.DeleteAsync(newIndex, ct: ct);
                    return ReindexResult.Failure(alias, newIndex,
                        $"Count mismatch: source={docCount} es={esCount}");
                }
            }

            // 4. 原子切换 alias
            await SwitchAliasAsync(alias, newIndex, ct);

            sw.Stop();
            _logger.LogInformation(
                "Reindex completed alias={Alias} newIndex={Index} docCount={Count} elapsedMs={Ms}",
                alias, newIndex, docCount, sw.ElapsedMilliseconds);

            return ReindexResult.Success(alias, newIndex, docCount);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Reindex failed alias={Alias} newIndex={Index} elapsedMs={Ms}",
                alias, newIndex, sw.ElapsedMilliseconds);

            try { await _esClient.Indices.DeleteAsync(newIndex, ct: ct); } catch { }

            return ReindexResult.Failure(alias, newIndex, ex.Message);
        }
    }

    // ── 私有辅助（原有实现不变）────────────────────────────────────

    private async Task CreateIndexAsync(string alias, string newIndex, CancellationToken ct)
    {
        var existsResp = await _esClient.Indices.ExistsAsync(newIndex, ct: ct);
        if (existsResp.Exists)
            throw new InvalidOperationException(
                $"Index {newIndex} already exists. Reindex aborted to avoid overwriting.");

        var response = alias switch
        {
            RbacUserIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(newIndex,
                    c => RbacUserIndexMapping.Build(c), ct),
            RbacGroupIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(newIndex,
                    c => RbacGroupIndexMapping.Build(c), ct),
            RbacRuleIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(newIndex,
                    c => RbacRuleIndexMapping.Build(c), ct),
            RbacPermissionViewIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(newIndex,
                    c => RbacPermissionViewIndexMapping.Build(c), ct),
            RbacAuditLogIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(newIndex,
                    c => RbacAuditLogIndexMapping.Build(c), ct),
            _ => throw new NotSupportedException($"No mapping found for alias: {alias}")
        };

        if (!response.IsValid)
            throw new InvalidOperationException(
                $"Create index failed: {response.ServerError?.Error?.Reason}");
    }

    private async Task SwitchAliasAsync(string alias, string newIndex, CancellationToken ct)
    {
        var aliasResp  = await _esClient.Indices.GetAliasAsync(alias, ct: ct);
        var oldIndices = aliasResp.Indices?.Keys.Select(k => k.Name).ToList()
            ?? new List<string>();

        var bulkResponse = await _esClient.Indices.BulkAliasAsync(b =>
        {
            foreach (var old in oldIndices)
                b = b.Remove(r => r.Index(old).Alias(alias));
            b = b.Add(a => a.Index(newIndex).Alias(alias));
            return b;
        }, ct);

        if (!bulkResponse.IsValid)
            throw new InvalidOperationException(
                $"Alias switch failed: {bulkResponse.ServerError?.Error?.Reason}");

        _logger.LogInformation(
            "Alias switched alias={Alias} from=[{Old}] to={New}",
            alias, string.Join(",", oldIndices), newIndex);

        // audit_log 保留旧索引（历史数据不删除）
        if (alias != RbacAuditLogIndexMapping.IndexName)
        {
            foreach (var old in oldIndices.Where(i => i != newIndex))
            {
                try { await _esClient.Indices.DeleteAsync(old, ct: ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old index={Old}", old);
                }
            }
        }
        else
        {
            _logger.LogInformation(
                "AuditLog old index retained (historical data preserved): [{Old}]",
                string.Join(",", oldIndices));
        }
    }

    private async Task BulkIndexAsync<T>(
        string index, IReadOnlyList<T> docs, CancellationToken ct) where T : class
    {
        if (docs.Count == 0) return;

        const int batchSize = 500;
        for (var i = 0; i < docs.Count; i += batchSize)
        {
            var batch = docs.Skip(i).Take(batchSize).ToList();
            var response = await _esClient.BulkAsync(b =>
                b.Index(index).IndexMany(batch), ct);

            if (response.Errors)
            {
                var firstError = response.ItemsWithErrors.FirstOrDefault()?.Error?.Reason;
                throw new InvalidOperationException($"Bulk index errors: {firstError}");
            }
        }
    }

    private static string BuildVersionedIndexName(string alias)
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var rand = Guid.NewGuid().ToString("N")[..4];
        return $"{alias}_v{date}_{rand}";
    }
}

/// <summary>重建结果。</summary>
public sealed class ReindexResult
{
    public bool    IsSuccess     { get; private init; }
    public string  Alias         { get; private init; } = string.Empty;
    public string  NewIndex      { get; private init; } = string.Empty;
    public int     DocumentCount { get; private init; }
    public string? FailureReason { get; private init; }

    public static ReindexResult Success(string alias, string newIndex, int count) =>
        new() { IsSuccess = true, Alias = alias, NewIndex = newIndex, DocumentCount = count };

    public static ReindexResult Failure(string alias, string newIndex, string reason) =>
        new() { IsSuccess = false, Alias = alias, NewIndex = newIndex, FailureReason = reason };
}
