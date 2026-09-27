namespace DevHub.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalItems, int TotalPages)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, long totalItems)
    {
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling((double)totalItems / pageSize);
        return new PagedResult<T>(items, page, pageSize, totalItems, totalPages);
    }

    public static PagedResult<T> Empty(int page, int pageSize)
        => new(Array.Empty<T>(), page, pageSize, 0, 0);
}

public sealed record PaginationQuery(int Page, int PageSize)
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public static PaginationQuery Normalize(int? page, int? pageSize)
        => new(
            Math.Max(1, page ?? 1),
            Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
