using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Models.Responses;

namespace ShmsBackend.Api.Services.Auth;

public interface IAuthService
{
    Task<ApiResponse<PreAuthResponseDto>> PreLoginAsync(LoginDto loginDto);
    Task<ApiResponse<AuthResponse>> VerifyLoginOtpAsync(VerifyLoginDto verifyLoginDto);
    Task<ApiResponse<AuthResponse>> RefreshTokenAsync(RefreshTokenDto refreshTokenDto);
    Task<ApiResponse<string>> LogoutAsync(string token);
    Task<ApiResponse<string>> RequestPasswordResetAsync(RequestPasswordResetDto requestPasswordResetDto);
}