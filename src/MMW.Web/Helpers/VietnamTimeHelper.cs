namespace MMW.Web.Helpers;

public static class VietnamTimeHelper
{
    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    public static DateTime ToVietnamTime(DateTime value)
    {
        var utcValue = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utcValue, VietnamTimeZone);
    }

    public static string Format(DateTime value, string format = "HH:mm dd/MM/yyyy")
    {
        return ToVietnamTime(value).ToString(format);
    }

    /// <summary>Giờ VN hiện tại (theo UtcNow), độc lập timezone máy chủ.</summary>
    public static DateTime VietnamNow() => ToVietnamTime(DateTime.UtcNow);

    /// <summary>Coi <paramref name="vnLocal"/> là giờ treo-tường VN → đổi sang UTC (để lọc cột UTC).</summary>
    public static DateTime ToUtc(DateTime vnLocal)
    {
        var unspecified = DateTime.SpecifyKind(vnLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, VietnamTimeZone);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
    }
}
