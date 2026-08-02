namespace MMW.Infrastructure.Ai;

public class LlmOptions
{
    public const string Section = "AiService";
    public string Provider { get; set; } = "Gemini";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.5-flash";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxOutputTokens { get; set; } = 2048;
}
