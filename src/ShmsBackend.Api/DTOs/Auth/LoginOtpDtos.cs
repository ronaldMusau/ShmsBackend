using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class VerifyLoginOtpDto
{
    [Required] public Guid ChallengeId { get; set; }
    [Required, StringLength(16, MinimumLength = 6)] public string Code { get; set; } = string.Empty;
}

public class ResendLoginOtpDto
{
    [Required] public Guid ChallengeId { get; set; }
}

public class EnrolStartDto
{
    [Required] public Guid ChallengeId { get; set; }
}

public class EnrolConfirmDto
{
    [Required] public Guid ChallengeId { get; set; }
    [Required, StringLength(6, MinimumLength = 6)] public string Code { get; set; } = string.Empty;
}

public class EnrolSetupDto
{
    public string Secret { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
}
