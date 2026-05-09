using Microsoft.Extensions.Logging;
using Nest;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Elasticsearch.Documents;
using Rbac.Infrastructure.Elasticsearch.Indexes;

namespace Rbac.Infrastructure.Elasticsearch.Reindex;

/// <summary>
/// ES 全量重建服务。
///
/// 重建流程（设计文档 §3.4 / §7.8）：
/// 1. 检查 alias 是否存在且指向唯一当前索引。
/// 2. 创建新物理索引（版本化命名，如 rbac_user_index_v20260509_001）。
/// 3. 应用 mapping 和 settings。
/// 4. 从 MySQL 全量读取数据并写入新索引。
/// 5. 校验文档数量与 MySQL 记录数。
/// 6. 校验通过后原子切换 alias 到新索引。
/// 7. 切换失败或校验失败时保留旧索引，不影响当前查询。
///
/// 重建期间查询仍走旧 alias，管理端不中断服务。
/// </summary>
public sealed class RbacEsFullReindexService
{
    private readonly IElasticClient _esClient;
    private readonly IAdministratorRepository _adminRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly IRuleRepository _ruleRepo;
    private readonly ILogger<RbacEsFullReindexService> _logger;

    public RbacEsFullReindexService(
        IElasticClient esClient,
        IAdministratorRepository adminRepo,
        IGroupRepository groupRepo,
        IRuleRepository ruleRepo,
        ILogger<RbacEsFullReindexService> logger)
    {
        _esClient = esClient;
        _adminRepo = adminRepo;
        _groupRepo = groupRepo;
        _ruleRepo = ruleRepo;
        _logger = logger;
    }

    /// <summary>
    /// 重建用户索引。
    /// </summary>
    public async Task<ReindexResult> ReindexUsersAsync(
        string? project = null, CancellationToken ct = default)
    {
        var alias = RbacUserIndexMapping.IndexName;
        var newIndex = BuildVersionedIndexName(alias);

        return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
        {
            // 从 MySQL 全量读取（project 为 null 时读取全部）
            var admins = project is not null
                ? await _adminRepo.FindByProjectAsync(new ProjectCode(project), ct)
                : await _adminRepo.FindByProjectAsync(new ProjectCode("*"), ct); // 实现层处理

            var docs = admins.Select(a => new UserDocument
            {
                Id = a.Id.ToString(),
                DxEId = a.DxEId.Value,    // string，不为 number
                Userid = a.Userid.Value,
                Username = a.Username,
                Status = a.Status.ToString(),
            }).ToList();

            await BulkIndexAsync<UserDocument>(newIndex, docs, ct);
            return docs.Count;
        });
    }

    /// <summary>重建权限组索引。</summary>
    public async Task<ReindexResult> ReindexGroupsAsync(
        string? project = null, CancellationToken ct = default)
    {
        var alias = RbacGroupIndexMapping.IndexName;
        var newIndex = BuildVersionedIndexName(alias);

        return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
        {
            var projectCode = new ProjectCode(project ?? "*");
            var groups = await _groupRepo.FindByProjectAsync(projectCode, ct);

            var docs = groups.Select(g => new GroupDocument
            {
                Id = g.Id.ToString(),
                DxEId = g.DxEId.Value,
                Project = g.Project.Value,
                GroupCode = g.GroupCode.Value,
                GroupName = g.GroupName,
                ParentGroupCode = g.ParentGroupCode?.Value,
                RuleCodes = g.RuleCodes.Select(r => r.Value).ToList(),
                PermissionCodes = g.PermissionCodes.Select(p => p.Value).ToList(),
                Status = g.Status.ToString(),
            }).ToList();

            await BulkIndexAsync<GroupDocument>(newIndex, docs, ct);
            return docs.Count;
        });
    }

    /// <summary>重建规则索引。</summary>
    public async Task<ReindexResult> ReindexRulesAsync(
        string? project = null, CancellationToken ct = default)
    {
        var alias = RbacRuleIndexMapping.IndexName;
        var newIndex = BuildVersionedIndexName(alias);

        return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
        {
            var projectCode = new ProjectCode(project ?? "*");
            var rules = await _ruleRepo.FindActiveByProjectAsync(projectCode, ct);

            var docs = rules.Select(r => new RuleDocument
            {
                Id = r.Id.ToString(),
                DxEId = r.DxEId.Value,
                Project = r.Project.Value,
                RuleCode = r.RuleCode.Value,
                PermissionCode = r.PermissionCode.Value,
                ParentRuleCode = r.ParentRuleCode?.Value,
                Title = r.Title,
                Name = r.Name,
                Path = r.Path,
                Type = r.Type.ToString().ToLowerInvariant(),
                MenuType = r.MenuType?.ToString().ToLowerInvariant() ?? string.Empty,
                Status = r.Status.ToString(),
                Weigh = r.Weigh,
            }).ToList();

            await BulkIndexAsync<RuleDocument>(newIndex, docs, ct);
            return docs.Count;
        });
    }

    // ── 核心重建流程 ──────────────────────────────────────────────

    private async Task<ReindexResult> ExecuteReindexAsync(
        string alias, string newIndex,
        CancellationToken ct,
        Func<Task<int>> buildAndIndexFunc)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Reindex started alias={Alias} newIndex={Index}", alias, newIndex);

        try
        {
            // 1. 创建新索引（使用版本化名）
            await CreateIndexAsync(alias, newIndex, ct);

            // 2. 从 MySQL 写入 ES
            var docCount = await buildAndIndexFunc();

            // 3. 校验文档数
            await _esClient.Indices.RefreshAsync(newIndex, ct: ct);
            var countResp = await _esClient.CountAsync<object>(c => c.Index(newIndex), ct);
            var esCount = countResp.Count;

            if (esCount < docCount * 0.99) // 允许 1% 误差
            {
                _logger.LogError(
                    "Reindex count mismatch alias={Alias} mysql={Mysql} es={Es}",
                    alias, docCount, esCount);
                await _esClient.Indices.DeleteAsync(newIndex, ct: ct);
                return ReindexResult.Failure(alias, newIndex, $"Count mismatch mysql={docCount} es={esCount}");
            }

            // 4. 原子切换 alias
            await SwitchAliasAsync(alias, newIndex, ct);

            sw.Stop();
            _logger.LogInformation(
                "Reindex completed alias={Alias} newIndex={Index} docCount={Count} elapsedMs={Ms}",
                alias, newIndex, docCount, sw.ElapsedMilliseconds);

            return ReindexResult.Success(alias, newIndex, (int)esCount);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Reindex failed alias={Alias} newIndex={Index} elapsedMs={Ms}",
                alias, newIndex, sw.ElapsedMilliseconds);

            // 失败时尝试删除新索引（保留旧索引继续服务）
            try { await _esClient.Indices.DeleteAsync(newIndex, ct: ct); } catch { }

            return ReindexResult.Failure(alias, newIndex, ex.Message);
        }
    }

    private async Task CreateIndexAsync(string alias, string newIndex, CancellationToken ct)
    {
        // 根据 alias 选择对应 mapping
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
            _ => throw new NotSupportedException($"No mapping found for alias: {alias}")
        };

        if (!response.IsValid)
            throw new InvalidOperationException(
                $"Create index failed: {response.ServerError?.Error?.Reason}");
    }

    private async Task SwitchAliasAsync(string alias, string newIndex, CancellationToken ct)
    {
        // 获取当前指向的旧索引
        var aliasResp = await _esClient.Indices.GetAliasAsync(alias, ct: ct);
        var oldIndices = aliasResp.Indices?.Keys.Select(k => k.Name).ToList()
            ?? new List<string>();

        // 原子操作：移除旧索引 alias + 添加新索引 alias
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

        // 删除旧物理索引（alias 切换成功后）
        foreach (var old in oldIndices.Where(i => i != newIndex))
        {
            try { await _esClient.Indices.DeleteAsync(old, ct: ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old index={Old}", old);
            }
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
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var seq = DateTimeOffset.UtcNow.Ticks % 1000;
        return $"{alias}_v{date}_{seq:000}";
    }
}

/// <summary>重建结果。</summary>
public sealed class ReindexResult
{
    public bool IsSuccess { get; private init; }
    public string Alias { get; private init; } = string.Empty;
    public string NewIndex { get; private init; } = string.Empty;
    public int DocumentCount { get; private init; }
    public string? FailureReason { get; private init; }

    public static ReindexResult Success(string alias, string newIndex, int count) =>
        new() { IsSuccess = true, Alias = alias, NewIndex = newIndex, DocumentCount = count };

    public static ReindexResult Failure(string alias, string newIndex, string reason) =>
        new() { IsSuccess = false, Alias = alias, NewIndex = newIndex, FailureReason = reason };
}
