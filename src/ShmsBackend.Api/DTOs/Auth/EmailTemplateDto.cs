namespace ShmsBackend.Api.Models.DTOs.Email;

public class EmailTemplateDto
{
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 10;
}