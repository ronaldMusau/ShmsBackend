using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.PortalAuth;
using ShmsBackend.Api.Models.Responses;

namespace ShmsBackend.Api.Services.PortalAuth;

public interface IPortalAuthService
{
    Task<ApiResponse<PortalAuthResponse>> LoginAsync(PortalLoginDto dto);
    Task<ApiResponse<string>> RegisterExplorerAsync(RegisterExplorerDto dto);
    Task<ApiResponse<PortalAuthResponse>> RefreshTokenAsync(PortalRefreshTokenDto dto);
    Task<ApiResponse<string>> LogoutAsync(string token);
    Task<ApiResponse<string>> RequestPasswordResetAsync(PortalRequestPasswordResetDto dto);
    Task<ApiResponse<string>> ResetPasswordAsync(PortalResetPasswordDto dto);
    Task<ApiResponse<string>> VerifyEmailAsync(PortalVerifyEmailDto dto);
    Task<ApiResponse<string>> SetPasswordAsync(PortalSetPasswordDto dto);
}
