namespace MMW.Shared.Models;

/// <summary>
/// Kết quả trả về chuẩn cho toàn hệ thống (mirror pattern Result của EOffice, đã gọn hoá).
/// </summary>
public class Result
{
    public bool Succeeded { get; set; }
    public List<string> Messages { get; set; } = new();

    public static Result Success() => new() { Succeeded = true };
    public static Result Fail(params string[] messages) =>
        new() { Succeeded = false, Messages = messages.ToList() };
}

public class Result<T> : Result
{
    public T? Data { get; set; }

    public static Result<T> Success(T data) => new() { Succeeded = true, Data = data };
    public static new Result<T> Fail(params string[] messages) =>
        new() { Succeeded = false, Messages = messages.ToList() };
}
