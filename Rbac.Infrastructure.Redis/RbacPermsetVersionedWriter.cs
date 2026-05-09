using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Rbac.Application.Snapshots;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// permset compare-before-write 版本化写入器。
///
/// 在写入 permset 前执行版本一致性检查：
/// - 构建开始时读取 version（versionAtBuildTime）。
/// - 写入前再次读取 version。
/// - 两者一致 → 允许写入。
/// - 两者不一致 → 丢弃本次构建结果（旧版本结果不覆盖新权限）。
///
/// 此类是 <see cref="RbacPermsetStore.BuildAndWriteAsync"/> 中 compare-before-write
/// 逻辑的显式封装，供外部直接调用或测试。
/// </summary>
public sealed class RbacPermsetVersionedWriter
{
    private readonly IDatabase _db;
    private readonly ILogger<RbacPermsetVersionedWriter> _logger;

    public RbacPermsetVersionedWriter(IDatabase db, ILogger<RbacPermsetVersionedWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 读取当前用户级版本号，供构建过程开始时记录基准版本。
    /// </summary>
    public async Task<long> ReadCurrentVersionAsync(string project, string userid)
    {
        var key = RbacRedisKeys.VersionUser(project, userid);
        return (long?)await _db.StringGetAsync(key) ?? 0L;
    }

    /// <summary>
    /// 执行 compare-before-write 写入。
    /// </summary>
    /// <param name="input">permset 构建输入（含 VersionAtBuildTime）。</param>
    /// <returns>写入成功返回 true；版本冲突丢弃返回 false。</returns>
    public async Task<bool> WriteIfVersionMatchAsync(PermsetBuildInput input)
    {
        if (input.Source != PermsetInputSource.MySqlCasbinDerived)
        {
            _logger.LogError(
                "Rejected illegal permset source={S} userid={U}", input.Source, input.Userid);
            return false;
        }

        var versionKey = RbacRedisKeys.VersionUser(input.Project, input.Userid);
        var currentVersion = (long?)await _db.StringGetAsync(versionKey) ?? 0L;

        if (currentVersion != input.VersionAtBuildTime)
        {
            _logger.LogWarning(
                "Permset write discarded: version mismatch userid={U} project={P} " +
                "buildVersion={B} currentVersion={C}",
                input.Userid, input.Project, input.VersionAtBuildTime, currentVersion);
            return false;
        }

        // 版本一致：原子写入
        var permsetKey = RbacRedisKeys.Permset(input.Project, input.Userid);
        var tx = _db.CreateTransaction();
        _ = tx.KeyDeleteAsync(permsetKey);
        if (input.Members.Count > 0)
        {
            var members = input.Members.Select(m => (RedisValue)m).ToArray();
            _ = tx.SetAddAsync(permsetKey, members);
        }
        _ = tx.KeyExpireAsync(permsetKey, TimeSpan.FromMinutes(45));
        var committed = await tx.ExecuteAsync();

        if (!committed)
        {
            _logger.LogWarning(
                "Permset transaction failed userid={U} project={P}", input.Userid, input.Project);
            return false;
        }

        _logger.LogDebug(
            "Permset written userid={U} project={P} members={C}",
            input.Userid, input.Project, input.Members.Count);
        return true;
    }
}
