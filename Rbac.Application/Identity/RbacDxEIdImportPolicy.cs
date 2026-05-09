namespace Rbac.Application.Identity;

/// <summary>
/// 迁移导入时旧 DxE_id 保留策略契约。
/// 允许迁移旧 PHP 数据时保留原有 DxE_id，但必须经过唯一性校验和冲突处理。
/// </summary>
public interface IRbacDxEIdImportPolicy
{
    /// <summary>
    /// 校验待导入的 DxE_id 是否可用。
    /// </summary>
    Task<DxEIdImportResult> ValidateAsync(
        string dxeId, string entityType, string? project = null,
        CancellationToken ct = default);
}

/// <summary>
/// DxE_id 导入校验结果。
/// </summary>
public sealed class DxEIdImportResult
{
    public bool IsValid { get; init; }

    /// <summary>发生冲突时提供的建议 ID（由 IRbacDxEIdGenerator 重新生成）。</summary>
    public string? SuggestedDxEId { get; init; }

    public string? ConflictReason { get; init; }

    public static DxEIdImportResult Ok() => new() { IsValid = true };

    public static DxEIdImportResult Conflict(string reason, string suggestedId) =>
        new() { IsValid = false, ConflictReason = reason, SuggestedDxEId = suggestedId };
}

/// <summary>
/// 默认导入策略实现：全局唯一性校验，冲突时提供新 ID。
/// </summary>
public sealed class DefaultDxEIdImportPolicy : IRbacDxEIdImportPolicy
{
    private readonly IRbacDxEIdGenerator _generator;
    private readonly IDxEIdExistenceChecker _checker;

    public DefaultDxEIdImportPolicy(
        IRbacDxEIdGenerator generator,
        IDxEIdExistenceChecker checker)
    {
        _generator = generator;
        _checker = checker;
    }

    public async Task<DxEIdImportResult> ValidateAsync(
        string dxeId, string entityType, string? project = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dxeId))
            return DxEIdImportResult.Conflict("DxEId is empty.", _generator.Generate());

        var exists = await _checker.ExistsAsync(dxeId, entityType, project, ct);
        if (exists)
        {
            var suggested = _generator.Generate();
            return DxEIdImportResult.Conflict(
                $"DxEId '{dxeId}' already exists for entityType '{entityType}'.", suggested);
        }

        return DxEIdImportResult.Ok();
    }
}

/// <summary>DxE_id 存在性检查契约，由 Infrastructure.MySql 实现。</summary>
public interface IDxEIdExistenceChecker
{
    Task<bool> ExistsAsync(
        string dxeId, string entityType, string? project = null,
        CancellationToken ct = default);
}
