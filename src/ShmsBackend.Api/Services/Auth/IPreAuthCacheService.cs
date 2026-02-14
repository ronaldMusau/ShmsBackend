using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Services.Auth;

public interface IPreAuthCacheService
{
    Task StorePreAuthDataAsync(string email, UserType userType, PreAuthDto preAuthData);
    Task<PreAuthDto?> GetPreAuthDataAsync(string email, UserType userType);
    Task RemovePreAuthDataAsync(string email, UserType userType);
}