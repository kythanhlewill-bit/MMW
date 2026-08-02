namespace MMW.Web.Models;

/// <summary>Dữ liệu cho partial _Pagination (tái dùng cho mọi trang danh sách).</summary>
public class PagerModel
{
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 20;
    public string PageParam { get; set; } = "page";
    public string PageSizeParam { get; set; } = "pageSize";
    public int[] PageSizes { get; set; } = { 20, 50, 100 };

    public static readonly int[] Allowed = { 20, 50, 100 };

    public static int NormalizeSize(int size) => Allowed.Contains(size) ? size : 20;
    public static int NormalizePage(int page) => Math.Max(1, page);

    public static PagerModel Build(int page, int pageSize, int totalCount, string pageParam = "page", string pageSizeParam = "pageSize")
    {
        pageSize = NormalizeSize(pageSize);
        page = NormalizePage(page);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PagerModel
        {
            CurrentPage = page,
            TotalPages = totalPages,
            TotalCount = totalCount,
            PageSize = pageSize,
            PageParam = pageParam,
            PageSizeParam = pageSizeParam,
        };
    }
}
