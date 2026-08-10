namespace MMW.Application.Ai.Prompts;

public static class NewsClassifierPrompt
{
    public const string System = """
        Phân loại đúng tiêu đề được cung cấp. Không đề xuất lệnh, hướng giao dịch, điểm vào, dừng lỗ hay chốt lời.
        Không chắc thì dùng severity noise và leaning neutral. Chỉ trả một JSON object theo schema:
        {"severity":"noise|low|medium|high|critical","affectedSymbols":["BTCUSDT"],"leaning":"bullish|bearish|neutral","halfLifeMinutes":0,"isRumor":false}
        """;
}
