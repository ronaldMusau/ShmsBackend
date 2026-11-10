using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ShmsBackend.Api.Services.Payment;

public class MpesaB2CConfig
{
    public string ShortCode { get; set; } = string.Empty;
    public string InitiatorName { get; set; } = string.Empty;
    /// <summary>Ready-made security credential. If empty it is built from InitiatorPassword and CertificatePath.</summary>
    public string SecurityCredential { get; set; } = string.Empty;
    public string InitiatorPassword { get; set; } = string.Empty;
    /// <summary>Path to Safaricom's public certificate (.cer) for this environment (sandbox or production).</summary>
    public string CertificatePath { get; set; } = string.Empty;
    /// <summary>Public base of the payouts callbacks, for example https://xxxx.ngrok-free.app/api/payouts/mpesa/callback</summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;
    public string CallbackSecret { get; set; } = string.Empty;
    public string PaymentRequestPath { get; set; } = "/mpesa/b2c/v3/paymentrequest";
    public decimal MaxPerPayout { get; set; } = 50000m;
    public decimal MaxPerDay { get; set; } = 150000m;
}

public record B2CSendResult(bool Accepted, string? ConversationId, string Code, string Message);

public interface IMpesaB2CService
{
    bool IsConfigured(out string problem);
    Task<B2CSendResult> SendAsync(Guid payoutId, string phone, decimal amount, string remarks);
    bool IsValidCallbackSecret(string? supplied);
}

public class MpesaB2CService : IMpesaB2CService
{
    private readonly MpesaConfig _mpesa;
    private readonly MpesaB2CConfig _b2c;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<MpesaB2CService> _logger;

    public MpesaB2CService(IOptions<MpesaConfig> mpesa, IOptions<MpesaB2CConfig> b2c, IHttpClientFactory http, ILogger<MpesaB2CService> logger)
    { _mpesa = mpesa.Value; _b2c = b2c.Value; _http = http; _logger = logger; }

    public bool IsConfigured(out string problem)
    {
        problem = "";
        if (string.IsNullOrWhiteSpace(_b2c.ShortCode)) problem = "MpesaB2C:ShortCode is not set.";
        else if (string.IsNullOrWhiteSpace(_b2c.InitiatorName)) problem = "MpesaB2C:InitiatorName is not set.";
        else if (string.IsNullOrWhiteSpace(_b2c.SecurityCredential) && (string.IsNullOrWhiteSpace(_b2c.InitiatorPassword) || string.IsNullOrWhiteSpace(_b2c.CertificatePath)))
            problem = "Set MpesaB2C:SecurityCredential, or both MpesaB2C:InitiatorPassword and MpesaB2C:CertificatePath.";
        else if (string.IsNullOrWhiteSpace(_b2c.CallbackBaseUrl) || string.IsNullOrWhiteSpace(_b2c.CallbackSecret)) problem = "MpesaB2C:CallbackBaseUrl and MpesaB2C:CallbackSecret are not set.";
        return problem == "";
    }

    public bool IsValidCallbackSecret(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(_b2c.CallbackSecret) || string.IsNullOrEmpty(supplied)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(_b2c.CallbackSecret));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private string SecurityCredential()
    {
        if (!string.IsNullOrWhiteSpace(_b2c.SecurityCredential)) return _b2c.SecurityCredential;
        using var cert = X509CertificateLoader.LoadCertificateFromFile(_b2c.CertificatePath);
        using var rsa = cert.GetRSAPublicKey() ?? throw new InvalidOperationException("The certificate has no RSA public key.");
        return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(_b2c.InitiatorPassword), RSAEncryptionPadding.Pkcs1));
    }

    private async Task<string> TokenAsync()
    {
        var client = _http.CreateClient("Mpesa");
        var req = new HttpRequestMessage(HttpMethod.Get, $"{_mpesa.BaseUrl}/oauth/v1/generate?grant_type=client_credentials");
        req.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_mpesa.ConsumerKey}:{_mpesa.ConsumerSecret}")));
        var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new Exception("Could not get an M-Pesa access token. Check that the app has the M-Pesa Sandbox product.");
        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>Sends one payout. The payout id is the OriginatorConversationID, so M-Pesa itself rejects a repeat of the same payout.</summary>
    public async Task<B2CSendResult> SendAsync(Guid payoutId, string phone, decimal amount, string remarks)
    {
        if (!IsConfigured(out var problem)) return new(false, null, "CONFIG", problem);
        try
        {
            var token = await TokenAsync();
            var cb = _b2c.CallbackBaseUrl.TrimEnd('/');
            var payload = new
            {
                OriginatorConversationID = payoutId.ToString(),
                InitiatorName = _b2c.InitiatorName,
                SecurityCredential = SecurityCredential(),
                CommandID = "BusinessPayment",
                Amount = (long)amount,
                PartyA = _b2c.ShortCode,
                PartyB = phone,
                Remarks = remarks.Length > 100 ? remarks[..100] : remarks,
                QueueTimeOutURL = $"{cb}/timeout/{_b2c.CallbackSecret}",
                ResultURL = $"{cb}/result/{_b2c.CallbackSecret}",
                Occassion = "Refund"
            };
            var client = _http.CreateClient("Mpesa");
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_mpesa.BaseUrl}{_b2c.PaymentRequestPath}")
            { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
            req.Headers.Add("Authorization", $"Bearer {token}");
            var res = await client.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            _logger.LogInformation("B2C payout {PayoutId} submitted, HTTP {Status}", payoutId, (int)res.StatusCode);
            string code = "", desc = "", conv = "";
            try
            {
                var root = JsonDocument.Parse(text).RootElement;
                code = root.TryGetProperty("ResponseCode", out var c) ? c.GetString() ?? "" : "";
                desc = root.TryGetProperty("ResponseDescription", out var d) ? d.GetString() ?? "" : (root.TryGetProperty("errorMessage", out var em) ? em.GetString() ?? "" : "");
                conv = root.TryGetProperty("ConversationID", out var cv) ? cv.GetString() ?? "" : "";
            }
            catch { desc = "M-Pesa returned an unreadable answer."; }
            return new(res.IsSuccessStatusCode && code == "0", string.IsNullOrEmpty(conv) ? null : conv, code, desc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "B2C payout {PayoutId} could not be sent", payoutId);
            return new(false, null, "ERROR", "Could not reach M-Pesa. The payout was not confirmed: check it with M-Pesa before retrying.");
        }
    }
}
