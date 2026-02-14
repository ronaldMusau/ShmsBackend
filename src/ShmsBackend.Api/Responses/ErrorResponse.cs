namespace ShmsBackend.Api.Models.Responses;

public class ErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string>? Errors { get; set; }
    public string? StackTrace { get; set; }
}