using System.Security.Cryptography;
using System.Text;

namespace MMW.Infrastructure.Exchanges.Binance;

/// <summary>Ký HMAC-SHA256 cho query string Binance (dùng chung read-only + order).</summary>
public static class BinanceSigner
{
    public static string Sign(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
