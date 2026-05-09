namespace Rbac.Infrastructure.MySql.Identity;

/// <summary>
/// DxE_id 生成配置。绑定 appsettings.json "DxEId" 节。
/// </summary>
public sealed class RbacDxEIdGenerationOptions
{
    public const string SectionName = "DxEId";

    /// <summary>雪花算法 WorkerId（0-31），多实例部署时需唯一。</summary>
    public int WorkerId { get; set; } = 1;

    /// <summary>雪花算法 DatacenterId（0-31）。</summary>
    public int DatacenterId { get; set; } = 1;
}

/// <summary>
/// 基于雪花算法的 DxE_id 生成器。
///
/// 底层产生 long 类型雪花 ID，序列化到 JSON 时必须转为 string（由 LongToStringConverter 保证）。
/// 实现 <see cref="Rbac.Application.Identity.IRbacDxEIdGenerator"/>，由 DI 注入。
///
/// 线程安全：使用 Interlocked + lock 保证并发安全。
/// </summary>
public sealed class SnowflakeDxEIdGenerator : Application.Identity.IRbacDxEIdGenerator
{
    private readonly object _lock = new();

    // 雪花算法参数
    private const long Twepoch = 1577836800000L; // 2020-01-01 00:00:00 UTC
    private const int WorkerIdBits = 5;
    private const int DatacenterIdBits = 5;
    private const int SequenceBits = 12;
    private const long MaxWorkerId = -1L ^ (-1L << WorkerIdBits);
    private const long MaxDatacenterId = -1L ^ (-1L << DatacenterIdBits);
    private const int WorkerIdShift = SequenceBits;
    private const int DatacenterIdShift = SequenceBits + WorkerIdBits;
    private const int TimestampLeftShift = SequenceBits + WorkerIdBits + DatacenterIdBits;
    private const long SequenceMask = -1L ^ (-1L << SequenceBits);

    private readonly long _workerId;
    private readonly long _datacenterId;
    private long _sequence;
    private long _lastTimestamp = -1L;

    public SnowflakeDxEIdGenerator(RbacDxEIdGenerationOptions options)
    {
        if (options.WorkerId > MaxWorkerId || options.WorkerId < 0)
            throw new ArgumentException($"WorkerId must be in [0, {MaxWorkerId}].");
        if (options.DatacenterId > MaxDatacenterId || options.DatacenterId < 0)
            throw new ArgumentException($"DatacenterId must be in [0, {MaxDatacenterId}].");

        _workerId = options.WorkerId;
        _datacenterId = options.DatacenterId;
    }

    /// <summary>生成下一个 DxE_id，始终返回 string。</summary>
    public string Generate() => NextId().ToString();

    private long NextId()
    {
        lock (_lock)
        {
            var timestamp = CurrentTimeMillis();

            if (timestamp < _lastTimestamp)
                throw new InvalidOperationException(
                    $"Clock moved backwards. Refusing to generate id for {_lastTimestamp - timestamp} ms.");

            if (_lastTimestamp == timestamp)
            {
                _sequence = (_sequence + 1) & SequenceMask;
                if (_sequence == 0)
                    timestamp = TilNextMillis(_lastTimestamp);
            }
            else
            {
                _sequence = 0L;
            }

            _lastTimestamp = timestamp;

            return ((timestamp - Twepoch) << TimestampLeftShift)
                | (_datacenterId << DatacenterIdShift)
                | (_workerId << WorkerIdShift)
                | _sequence;
        }
    }

    private static long TilNextMillis(long lastTimestamp)
    {
        var timestamp = CurrentTimeMillis();
        while (timestamp <= lastTimestamp)
            timestamp = CurrentTimeMillis();
        return timestamp;
    }

    private static long CurrentTimeMillis() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
