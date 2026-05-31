namespace MMW.Infrastructure.Ai;

public class LlmOptions
{
    public const string Section = "AiService";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "MiniMax-Text-01";
    public int TimeoutSeconds { get; set; } = 30;
}
