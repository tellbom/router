using Microsoft.Extensions.Logging;
using Rbac.Infrastructure.Elasticsearch.Reindex;

namespace Rbac.Worker.Reindex;

/// <summary>
/// ES 全量重建 Worker 入口。
///
/// PATCH-10: ReindexAllAsync 补充 permission_view / audit_log 索引重建。
/// PATCH-11: preflight 已在 RbacEsFullReindexService.ExecuteReindexAsync 内部执行，
///           Worker 层无需感知。
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
        _logger         = logger;
    }

    /// <summary>
    /// 全量重建所有五个索引。
    /// 顺序：user → group → rule → permission_view → audit_log。
    /// </summary>
    public async Task ReindexAllAsync(string? project = null, CancellationToken ct = default)
    {
        _logger.LogInformation("ReindexAll started project={Project}", project ?? "ALL");

        var results = new List<ReindexResult>();

        results.Add(await SafeReindexAsync(
            () => _reindexService.ReindexUsersAsync(project, ct),
            "rbac_user_index"));

        results.Add(await SafeReindexAsync(
            () => _reindexService.ReindexGroupsAsync(project, ct),
            "rbac_group_index"));

        results.Add(await SafeReindexAsync(
            () => _reindexService.ReindexRulesAsync(project, ct),
            "rbac_rule_index"));

        // PATCH-10: 新增
        results.Add(await SafeReindexAsync(
            () => _reindexService.ReindexPermissionViewAsync(project, ct),
            "rbac_permission_view_index"));

        // PATCH-10: 新增（audit_log 空索引重建，确保 alias 健康）
        results.Add(await SafeReindexAsync(
            () => _reindexService.ReindexAuditLogAsync(project, ct),
            "rbac_audit_log_index"));

        var succeeded = results.Count(r => r.IsSuccess);
        var failed    = results.Count(r => !r.IsSuccess);

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

    /// <summary>重建单个索引。</summary>
    public async Task<ReindexResult> ReindexSingleAsync(
        string alias, string? project = null, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "ReindexSingle alias={Alias} project={Project}", alias, project ?? "ALL");

        return alias switch
        {
            "rbac_user_index" =>
                await SafeReindexAsync(
                    () => _reindexService.ReindexUsersAsync(project, ct), alias),
            "rbac_group_index" =>
                await SafeReindexAsync(
                    () => _reindexService.ReindexGroupsAsync(project, ct), alias),
            "rbac_rule_index" =>
                await SafeReindexAsync(
                    () => _reindexService.ReindexRulesAsync(project, ct), alias),
            "rbac_permission_view_index" =>           // PATCH-10
                await SafeReindexAsync(
                    () => _reindexService.ReindexPermissionViewAsync(project, ct), alias),
            "rbac_audit_log_index" =>                 // PATCH-10
                await SafeReindexAsync(
                    () => _reindexService.ReindexAuditLogAsync(project, ct), alias),
            _ => ReindexResult.Failure(alias, alias, $"Unknown alias: {alias}")
        };
    }

    private async Task<ReindexResult> SafeReindexAsync(
        Func<Task<ReindexResult>> reindexFunc, string alias)
    {
        try
        {
            var result = await reindexFunc();

            if (result.IsSuccess)
                _logger.LogInformation(
                    "Reindex success alias={Alias} newIndex={Index} docs={Count}",
                    result.Alias, result.NewIndex, result.DocumentCount);
            else
                _logger.LogError(
                    "Reindex failed alias={Alias} reason={Reason}",
                    result.Alias, result.FailureReason);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reindex threw exception alias={Alias}", alias);
            return ReindexResult.Failure(alias, alias, ex.Message);
        }
    }
}
