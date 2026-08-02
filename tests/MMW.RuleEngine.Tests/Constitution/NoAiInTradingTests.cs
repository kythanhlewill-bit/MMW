using System.Reflection;
using MMW.Application.Interfaces;
using MMW.Application.Trading.Structure;
using Xunit;

namespace MMW.RuleEngine.Tests.Constitution;

/// <summary>
/// Bộ gác FR-041: không lớp nào trong tầng quyết định được nhận <c>ILlmService</c>.
/// </summary>
/// <remarks>
/// Ranh giới "AI chỉ được veto hoặc giảm" được cưỡng chế bằng KIẾN TRÚC, không bằng kỷ luật
/// cá nhân: nếu tầng quyết định không thể với tới dịch vụ AI thì nó không thể bị AI chi phối,
/// bất kể ai viết mã sau này nghĩ gì.
///
/// AI được phép tác động đúng một chỗ: một hệ số trong <c>[0, 1]</c> nhân vào kích thước,
/// tính ở <c>MarketContextApplier</c> ngoài tầng này.
/// </remarks>
public class NoAiInTradingTests
{
    private static bool IsTradingLayer(Type t) =>
        t.Namespace is not null
        && (t.Namespace.StartsWith("MMW.Application.Trading", StringComparison.Ordinal)
            || t.Namespace.StartsWith("MMW.Application.Backtest", StringComparison.Ordinal));

    private static IReadOnlyList<Type> TradingTypes() =>
        typeof(SwingDetector).Assembly.GetTypes().Where(IsTradingLayer).ToList();

    [Fact]
    public void Khong_constructor_nao_trong_tang_quyet_dinh_nhan_ILlmService()
    {
        var offenders = new List<string>();

        foreach (var type in TradingTypes())
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var p in ctor.GetParameters())
                {
                    if (typeof(ILlmService).IsAssignableFrom(p.ParameterType))
                        offenders.Add($"{type.FullName}(.., {p.ParameterType.Name} {p.Name}, ..)");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Tầng quyết định không được phụ thuộc dịch vụ AI (FR-041). Vi phạm:" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
    }

    [Fact]
    public void Khong_phuong_thuc_nao_trong_tang_quyet_dinh_nhan_hay_tra_ILlmService()
    {
        // Chặn cả đường lách qua tham số phương thức hay giá trị trả về, không chỉ constructor.
        var offenders = new List<string>();

        foreach (var type in TradingTypes())
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (typeof(ILlmService).IsAssignableFrom(m.ReturnType))
                    offenders.Add($"{type.FullName}.{m.Name} trả về {m.ReturnType.Name}");

                foreach (var p in m.GetParameters())
                {
                    if (typeof(ILlmService).IsAssignableFrom(p.ParameterType))
                        offenders.Add($"{type.FullName}.{m.Name}(.., {p.ParameterType.Name} {p.Name}, ..)");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Tầng quyết định không được nhận hay trả về dịch vụ AI (FR-041). Vi phạm:" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
    }

    [Fact]
    public void Bo_gac_thuc_su_co_quet_duoc_lop_nao_do()
    {
        Assert.NotEmpty(TradingTypes());
    }
}
