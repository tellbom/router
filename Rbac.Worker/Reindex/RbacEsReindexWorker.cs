using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rbac.Infrastructure.Elasticsearch.Reindex;

namespace Rbac.Worker.Reindex;

/// <summary>
/// ES 全量重建 Worker 入口点。
///
/// 提供三种触发方式：
/// 1. 手动触发（管理端或运维操作）。
/// 2. 定时触发（Quartz.NET / Hangfire 调度）。
/// 3. ES 同步异常后的补偿触发。
///
/// 安全保证：
/// - 重建失败时保留旧索引和旧 alias，管理端查询不中断。
/// - 文档数校验不通过时不切换 alias。
/// - 重建结果写结构化日志，运维可观测。
/// - 不支持并发重建同一 alias（由外部调度保证）。
/// </summary>
public sealed class RbacEsReindexWorker
{
    private readonly RbacEsFullReindexService _reindexService;
    private readonly ILogger<RbacEsReindexWorker> _logger;

    public RbacEsReindexWorker(
        RbacEsFullReindexService reindexService,
        ILogger<RbacEsReindexWorker> logger)
    {
        _reindexService = reindexService;
        _logger = logger;
    }

    /// <summary>
    /// 全量重建所有索引（管理端触发或 alias 异常恢复场景）。
    /// </summary>
    public async Task ReindexAllAsync(string? project = null, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "ReindexAll started project={Project}", project ?? "ALL");

        var results = new List<ReindexResult>();

        results.Add(await SafeReindexAsync(
            () => _reindexService.ReindexUsersAsync(project, ct),
            "rbac_user_index", ct));

        results.Add(await SafeReindexAsync(
            () => _reindexService.ReindexGroupsAsync(project, ct),
            "rbac_group_index", ct));

        results.Add(await SafeReindexAsync(
            () => _reindexService.ReindexRulesAsync(project, ct),
            "rbac_rule_index", ct));

        // 汇总日志
        var succeeded = results.Count(r => r.IsSuccess);
        var failed = results.Count(r => !r.IsSuccess);

        _logger.LogInformation(
            "ReindexAll completed project={Project} succeeded={S} failed={F}",
            project ?? "ALL", succeeded, failed);

        foreach (var r in results.Where(r => !r.IsSuccess))
        {
            _logger.LogError(
                "Reindex failed alias={Alias} reason={Reason}",
                r.Alias, r.FailureReason);
        }
    }

    /// <summary>
    /// 重建单个索引（按 alias 名称）。
    /// </summary>
    public async Task<ReindexResult> ReindexSingleAsync(
        string alias, string? project = null, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "ReindexSingle alias={Alias} project={Project}", alias, project ?? "ALL");

        return alias switch
        {
            "rbac_user_index" =>
                await SafeReindexAsync(() => _reindexService.ReindexUsersAsync(project, ct), alias, ct),
            "rbac_group_index" =>
                await SafeReindexAsync(() => _reindexService.ReindexGroupsAsync(project, ct), alias, ct),
            "rbac_rule_index" =>
                await SafeReindexAsync(() => _reindexService.ReindexRulesAsync(project, ct), alias, ct),
            _ => ReindexResult.Failure(alias, alias, $"Unknown alias: {alias}")
        };
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private async Task<ReindexResult> SafeReindexAsync(
        Func<Task<ReindexResult>> reindexFunc,
        string alias,
        CancellationToken ct)
    {
        try
        {
            var result = await reindexFunc();

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "Reindex success alias={Alias} newIndex={Index} docs={Count}",
                    result.Alias, result.NewIndex, result.DocumentCount);
            }
            else
            {
                _logger.LogError(
                    "Reindex failed alias={Alias} reason={Reason}",
                    result.Alias, result.FailureReason);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reindex threw exception alias={Alias}", alias);
            return ReindexResult.Failure(alias, alias, ex.Message);
        }
    }
}
