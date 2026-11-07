using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Models.Responses;

namespace ShmsBackend.Api.Services.Auth;

public interface IAuthService
{
    Task<ApiResponse<AuthResponse>> LoginAsync(LoginDto loginDto, string? ip = null);
    Task<ApiResponse<AuthResponse>> VerifyLoginOtpAsync(VerifyLoginOtpDto dto, string? ip);
    Task<ApiResponse<AuthResponse>> ResendLoginOtpAsync(ResendLoginOtpDto dto, string? ip);
    Task<ApiResponse<AuthResponse>> RefreshTokenAsync(RefreshTokenDto refreshTokenDto);
    Task<ApiResponse<string>> LogoutAsync(string token);
    Task<ApiResponse<string>> RequestPasswordResetAsync(RequestPasswordResetDto requestPasswordResetDto);
    Task<ApiResponse<string>> ResetPasswordAsync(ResetPasswordDto dto);
    Task<ApiResponse<string>> ChangePasswordAsync(Guid adminId, ChangePasswordDto dto);
}