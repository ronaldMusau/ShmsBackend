using System;
using System.Security.Claims;
using System.Threading.Tasks;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Services.Auth;

public interface ITokenService
{
    string GenerateAccessToken(Guid userId, string email, UserType userType);
    string GeneratePortalAccessToken(Guid userId, string email, PortalUserType portalUserType);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
    Task<bool> ValidateRefreshTokenAsync(Guid userId, string refreshToken);
}
