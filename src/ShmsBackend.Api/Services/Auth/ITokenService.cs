using System;
using System.Security.Claims;
using System.Threading.Tasks;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Services.Auth;

public interface ITokenService
{
    string GenerateAccessToken(Guid userId, string email, UserType userType);  // Changed from List<RoleType>
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
    Task<bool> ValidateRefreshTokenAsync(Guid userId, string refreshToken);
}