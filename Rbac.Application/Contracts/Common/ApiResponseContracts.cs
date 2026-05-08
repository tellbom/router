namespace Rbac.Application.Contracts.Common;

/// <summary>
/// 统一响应包装体。所有 API 响应必须使用此结构返回。
/// 字段顺序：code / msg / data / time，与前端约定一致。
/// </summary>
public sealed class ApiResponse<T>
{
    /// <summary>业务状态码。成功为 0，业务错误为非 0 正整数。</summary>
    public int Code { get; init; }

    /// <summary>提示信息。成功时为 "ok" 或空，失败时为可读错误描述。</summary>
    public string Msg { get; init; } = string.Empty;

    /// <summary>响应数据体。失败时为 null。</summary>
    public T? Data { get; init; }

    /// <summary>服务端响应时间戳（Unix 秒）。</summary>
    public long Time { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>构建成功响应。</summary>
    public static ApiResponse<T> Ok(T data) =>
        new() { Code = 0, Msg = "ok", Data = data };

    /// <summary>构建失败响应。</summary>
    public static ApiResponse<T> Fail(int code, string msg) =>
        new() { Code = code, Msg = msg };
}

/// <summary>
/// 非泛型便捷版本，用于无数据的成功响应。
/// </summary>
public sealed class ApiResponse
{
    public int Code { get; init; }
    public string Msg { get; init; } = string.Empty;
    public long Time { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static ApiResponse Ok() => new() { Code = 0, Msg = "ok" };
    public static ApiResponse Fail(int code, string msg) => new() { Code = code, Msg = msg };
}

/// <summary>
/// 分页数据体。data.list / data.total 结构，与前端约定一致。
/// </summary>
public sealed class PagedData<T>
{
    /// <summary>当前页数据列表。</summary>
    public IReadOnlyList<T> List { get; init; } = Array.Empty<T>();

    /// <summary>满足过滤条件的总记录数（不是当前页数量）。</summary>
    public long Total { get; init; }
}

/// <summary>
/// 分页查询入参基类。
/// </summary>
public class PagedQuery
{
    /// <summary>页码，从 1 开始。</summary>
    public int Page { get; init; } = 1;

    /// <summary>每页条数，最大 100。</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>计算 ES/DB 的 from offset。</summary>
    public int Offset => (Page - 1) * PageSize;
}
