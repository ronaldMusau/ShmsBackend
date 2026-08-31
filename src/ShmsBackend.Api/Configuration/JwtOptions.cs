namespace ShmsBackend.Api.Configuration;

public class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    // Legacy single value — kept for backwards compatibility, no longer used for token issuance.
    public int AccessTokenExpirationMinutes { get; set; } = 10080;

    // Distinct access-token lifetimes per audience.
    public int AdminAccessTokenExpirationMinutes { get; set; } = 240;
    public int PortalAccessTokenExpirationMinutes { get; set; } = 60;

    public int RefreshTokenExpirationDays { get; set; } = 7;
}