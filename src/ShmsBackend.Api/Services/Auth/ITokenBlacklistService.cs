namespace ShmsBackend.Api.Services.Auth;

public interface ITokenBlacklistService
{
    Task BlacklistTokenAsync(string token, TimeSpan? expiration = null);
    Task<bool> IsTokenBlacklistedAsync(string token);
}