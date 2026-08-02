namespace MMW.Infrastructure.Email;

public class EmailOptions
{
    public const string Section = "Email";
    public string Provider { get; set; } = "Smtp";
    public string From { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseTls { get; set; } = true;
}
