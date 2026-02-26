namespace ShmsBackend.Api.Configuration;

public class AppSettings
{
    public string ApplicationName { get; set; } = "SHMS Backend";
    public string Version { get; set; } = "1.0.0";
    public string Environment { get; set; } = "Development";
    public int OtpExpirationMinutes { get; set; } = 10;
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 30;
    public string FrontendUrl { get; set; } = "http://localhost:4200";
}