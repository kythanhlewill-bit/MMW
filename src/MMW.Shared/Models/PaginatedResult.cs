namespace MMW.Shared.Models;

/// <summary>
/// Kết quả phân trang (giữ nguyên hình dạng PaginatedResult của EOffice).
/// </summary>
public class PaginatedResult<T> : Result
{
    public PaginatedResult(List<T> data) => Data = data;

    public PaginatedResult(
        bool succeeded,
        List<T>? data = default,
        List<string>? messages = null,
        int count = 0,
        int pageNumber = 1,
        int pageSize = 10)
    {
        Data = data ?? new List<T>();
        CurrentPage = pageNumber;
        Succeeded = succeeded;
        Messages = messages ?? new List<string>();
        PageSize = pageSize;
        TotalPages = (int)Math.Ceiling(count / (double)pageSize);
        TotalCount = count;
    }

    public List<T> Data { get; set; } = new();
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public int PageSize { get; set; }

    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;

    public static PaginatedResult<T> Create(List<T> data, int count, int pageNumber, int pageSize) =>
        new(true, data, null, count, pageNumber, pageSize);
}
