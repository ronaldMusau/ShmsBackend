using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ShmsBackend.Api.Services.Payment;

public class MpesaConfig
{
    public string ConsumerKey { get; set; } = string.Empty;
    public string ConsumerSecret { get; set; } = string.Empty;
    public string Shortcode { get; set; } = string.Empty;
    public string Passkey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    /// <summary>
    /// Long random secret appended to the callback URL path (/api/payments/mpesa/callback/{secret}).
    /// Safaricom does not sign STK callbacks, so this is what proves a callback came from the URL we gave Safaricom.
    /// Set it via user-secrets / environment variables, never in a committed file.
    /// </summary>
    public string CallbackSecret { get; set; } = string.Empty;
}

public class STKPushResponse
{
    public string MerchantRequestID { get; set; } = string.Empty;
    public string CheckoutRequestID { get; set; } = string.Empty;
    public string ResponseCode { get; set; } = string.Empty;
    public string ResponseDescription { get; set; } = string.Empty;
    public string CustomerMessage { get; set; } = string.Empty;
}

public class STKQueryResponse
{
    public string ResponseCode { get; set; } = string.Empty;
    public string ResponseDescription { get; set; } = string.Empty;
    public string ResultCode { get; set; } = string.Empty;
    public string ResultDesc { get; set; } = string.Empty;
    public string CheckoutRequestID { get; set; } = string.Empty;
    public string MerchantRequestID { get; set; } = string.Empty;
}

public class MpesaCallbackItem
{
    public string Name { get; set; } = string.Empty;
    public JsonElement? Value { get; set; }
}

public class MpesaCallbackMetadata
{
    public List<MpesaCallbackItem> Item { get; set; } = new();
}

public class MpesaStkCallback
{
    public string MerchantRequestID { get; set; } = string.Empty;
    public string CheckoutRequestID { get; set; } = string.Empty;
    public int ResultCode { get; set; }
    public string ResultDesc { get; set; } = string.Empty;
    public MpesaCallbackMetadata? CallbackMetadata { get; set; }
}

public class MpesaCallbackBody
{
    public MpesaStkCallback stkCallback { get; set; } = new();
}

public class MpesaCallback
{
    public MpesaCallbackBody Body { get; set; } = new();
}

public class MpesaPaymentDetails
{
    public decimal? Amount { get; set; }
    public string? MpesaReceiptNumber { get; set; }
    public string? TransactionDate { get; set; }
    public string? PhoneNumber { get; set; }
    public int ResultCode { get; set; }
    public string? ResultDescription { get; set; }
    public bool IsSuccess => ResultCode == 0;
    public bool IsCancelled => ResultCode == 1032;
    public bool IsInsufficientFunds => ResultCode == 1;
}

public class STKPushRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string AccountReference { get; set; } = "Payment";
    public string TransactionDesc { get; set; } = "Payment";
    public string? TenantId { get; set; }
    public string? HouseId { get; set; }
    public string? PaymentId { get; set; }
}

public interface IMpesaService
{
    Task<STKPushResponse> InitiateSTKPushAsync(STKPushRequest request);
    Task<STKQueryResponse> QuerySTKStatusAsync(string checkoutRequestId);
    MpesaPaymentDetails ExtractPaymentDetails(MpesaCallback callbackData);
    /// <summary>Constant-time check of the secret taken from the callback URL path.</summary>
    bool IsValidCallbackSecret(string? supplied);
    /// <summary>
    /// Asks M-Pesa (STK Query) whether the checkout really succeeded. true = confirmed, false = M-Pesa says it did NOT
    /// succeed (a forged or wrong callback), null = couldn't get a definite answer (network/rate limit) — treat as unconfirmed.
    /// </summary>
    Task<bool?> ConfirmSuccessAsync(string checkoutRequestId);
}

public class MpesaService : IMpesaService
{
    private readonly MpesaConfig _config;
    private readonly ILogger<MpesaService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private string? _currentToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private static readonly System.Text.RegularExpressions.Regex SafaricomPhone = new(@"^254(7|1)\d{8}$");

    public MpesaService(
        IOptions<MpesaConfig> config,
        ILogger<MpesaService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    private string FormatPhoneNumber(string phone)
    {
        var cleaned = new string(phone.Where(char.IsDigit).ToArray());
        if (cleaned.StartsWith("0"))
            return "254" + cleaned.Substring(1);
        if (cleaned.StartsWith("254"))
            return cleaned;
        return "254" + cleaned;
    }

    // Daraja expects Kenyan time (EAT, UTC+3, no daylight saving) — build it from UTC so it is right on any server clock/zone.
    private string GenerateTimestamp()
    {
        return DateTime.UtcNow.AddHours(3).ToString("yyyyMMddHHmmss");
    }

    private static string MaskPhone(string phone)
        => phone.Length >= 7 ? phone[..4] + "****" + phone[^3..] : "****";

    private string BuildCallbackUrl()
    {
        if (string.IsNullOrWhiteSpace(_config.CallbackSecret))
            throw new InvalidOperationException("M-Pesa callback secret is not configured (Mpesa:CallbackSecret).");
        return $"{_config.CallbackUrl.TrimEnd('/')}/{_config.CallbackSecret}";
    }

    private string GeneratePassword(string timestamp)
    {
        var raw = $"{_config.Shortcode}{_config.Passkey}{timestamp}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private async Task<string> GetAccessTokenAsync()
    {
        if (_currentToken != null && DateTime.UtcNow < _tokenExpiry)
            return _currentToken;

        await _tokenLock.WaitAsync();
        try
        {
        if (_currentToken != null && DateTime.UtcNow < _tokenExpiry)
            return _currentToken;

        var client = _httpClientFactory.CreateClient("Mpesa");
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_config.ConsumerKey}:{_config.ConsumerSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_config.BaseUrl}/oauth/v1/generate?grant_type=client_credentials");
        request.Headers.Add("Authorization", $"Basic {credentials}");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to get M-Pesa access token: {content}");

        var json = JsonDocument.Parse(content);
        _currentToken = json.RootElement.GetProperty("access_token").GetString()!;
        // Refresh a minute before Safaricom's stated expiry (normally 3599s).
        var expiresIn = 3599;
        if (json.RootElement.TryGetProperty("expires_in", out var exp))
        {
            var expText = exp.ValueKind == JsonValueKind.Number ? exp.GetRawText() : exp.GetString();
            if (int.TryParse(expText, out var parsed) && parsed > 120)
                expiresIn = parsed;
        }
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

        return _currentToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<STKPushResponse> InitiateSTKPushAsync(STKPushRequest request)
    {
        try
        {
            var token = await GetAccessTokenAsync();
            var timestamp = GenerateTimestamp();
            var password = GeneratePassword(timestamp);
            var formattedPhone = FormatPhoneNumber(request.PhoneNumber);
            if (!SafaricomPhone.IsMatch(formattedPhone))
                throw new ArgumentException("Enter a valid Safaricom phone number, e.g. 0712 345 678 or 0112 345 678.");
            var amount = (int)Math.Round(request.Amount, MidpointRounding.AwayFromZero);
            if (amount < 1)
                throw new ArgumentException("The amount must be at least KES 1.");

            var payload = new
            {
                BusinessShortCode = _config.Shortcode,
                Password = password,
                Timestamp = timestamp,
                TransactionType = "CustomerPayBillOnline",
                Amount = amount,
                PartyA = formattedPhone,
                PartyB = _config.Shortcode,
                PhoneNumber = formattedPhone,
                CallBackURL = BuildCallbackUrl(),
                AccountReference = request.AccountReference.Length > 12
                    ? request.AccountReference.Substring(0, 12)
                    : request.AccountReference,
                TransactionDesc = request.TransactionDesc.Length > 13
                    ? request.TransactionDesc.Substring(0, 13)
                    : request.TransactionDesc
            };

            var client = _httpClientFactory.CreateClient("Mpesa");
            var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/mpesa/stkpush/v1/processrequest");
            httpRequest.Headers.Add("Authorization", $"Bearer {token}");
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json");

            _logger.LogInformation("Initiating STK Push for amount {Amount} to {Phone}",
                amount, MaskPhone(formattedPhone));

            var response = await client.SendAsync(httpRequest);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("STK Push responded {Status}", (int)response.StatusCode);

            var result = JsonSerializer.Deserialize<STKPushResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null || result.ResponseCode != "0")
                throw new Exception($"STK Push failed: {content}");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "STK Push failed");
            throw;
        }
    }

    public async Task<STKQueryResponse> QuerySTKStatusAsync(string checkoutRequestId)
    {
        try
        {
            var token = await GetAccessTokenAsync();
            var timestamp = GenerateTimestamp();
            var password = GeneratePassword(timestamp);

            var payload = new
            {
                BusinessShortCode = _config.Shortcode,
                Password = password,
                Timestamp = timestamp,
                CheckoutRequestID = checkoutRequestId
            };

            var client = _httpClientFactory.CreateClient("Mpesa");
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/mpesa/stkpushquery/v1/query");
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            // M-Pesa answers with an error body while the customer is still entering the PIN (or when rate limited).
            // That is "no answer yet", not a customer timeout — leave ResultCode empty so callers treat it as pending.
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("STK query for {CheckoutRequestId} not final yet (HTTP {Status}): {Body}",
                    checkoutRequestId, (int)response.StatusCode, content.Length > 300 ? content[..300] : content);
                return new STKQueryResponse { CheckoutRequestID = checkoutRequestId, ResultDesc = "Status not available yet" };
            }

            var result = JsonSerializer.Deserialize<STKQueryResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new STKQueryResponse { CheckoutRequestID = checkoutRequestId, ResultDesc = "Status not available yet" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "STK status query failed for {CheckoutRequestId}", checkoutRequestId);
            throw;
        }
    }

    public MpesaPaymentDetails ExtractPaymentDetails(MpesaCallback callbackData)
    {
        var details = new MpesaPaymentDetails
        {
            ResultCode = callbackData.Body.stkCallback.ResultCode,
            ResultDescription = callbackData.Body.stkCallback.ResultDesc
        };

        var items = callbackData.Body.stkCallback.CallbackMetadata?.Item;
        if (items == null) return details;

        foreach (var item in items)
        {
            switch (item.Name)
            {
                case "Amount":
                    if (item.Value.HasValue && item.Value.Value.ValueKind == JsonValueKind.Number)
                        details.Amount = item.Value.Value.GetDecimal();
                    break;
                case "MpesaReceiptNumber":
                    if (item.Value.HasValue && item.Value.Value.ValueKind == JsonValueKind.String)
                        details.MpesaReceiptNumber = item.Value.Value.GetString();
                    break;
                case "TransactionDate":
                    if (item.Value.HasValue)
                        details.TransactionDate = item.Value.Value.ToString();
                    break;
                case "PhoneNumber":
                    if (item.Value.HasValue)
                        details.PhoneNumber = item.Value.Value.ToString();
                    break;
            }
        }

        return details;
    }

    public bool IsValidCallbackSecret(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(_config.CallbackSecret) || string.IsNullOrEmpty(supplied)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(_config.CallbackSecret));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public async Task<bool?> ConfirmSuccessAsync(string checkoutRequestId)
    {
        // Safaricom closes the callback connection after a few seconds, so this is a single, time-boxed check:
        // right after a callback M-Pesa's query often answers 500 ("not final yet"), which just means "unconfirmed".
        try
        {
            var query = QuerySTKStatusAsync(checkoutRequestId);
            var finished = await Task.WhenAny(query, Task.Delay(TimeSpan.FromSeconds(2.5)));
            if (finished != query) return null;
            var r = await query;
            if (r.ResultCode == "0") return true;
            if (!string.IsNullOrEmpty(r.ResultCode)) return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "STK confirm failed for {CheckoutRequestId}", checkoutRequestId);
        }
        return null;
    }
}
