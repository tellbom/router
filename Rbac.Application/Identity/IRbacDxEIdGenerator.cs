namespace Rbac.Application.Identity;

/// <summary>
/// DxE_id 集中生成接口。
///
/// 约束：
/// - 新建记录的 DxE_id 必须由此接口统一生成，业务调用方不得自行传入新 ID。
/// - 底层可使用雪花 long 或其他分布式 ID，但 Generate() 返回值必须为 string。
/// - 迁移旧数据时通过 <see cref="IRbacDxEIdImportPolicy"/> 保留原 ID。
/// </summary>
public interface IRbacDxEIdGenerator
{
    /// <summary>
    /// 生成新的 DxE_id。返回值始终为 string，不允许调用方将其转为 long 或 number。
    /// </summary>
    string Generate();
}
