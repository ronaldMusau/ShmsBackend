using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class VerifyLoginOtpDto
{
    [Required] public Guid ChallengeId { get; set; }
    [Required, StringLength(6, MinimumLength = 6)] public string Code { get; set; } = string.Empty;
}

public class ResendLoginOtpDto
{
    [Required] public Guid ChallengeId { get; set; }
}
