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
    bool VerifySignature(string signature, object callbackData);
}

public class MpesaService : IMpesaService
{
    private readonly MpesaConfig _config;
    private readonly ILogger<MpesaService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private string? _currentToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

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

    private string GenerateTimestamp()
    {
        return DateTime.Now.ToString("yyyyMMddHHmmss");
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
        _tokenExpiry = DateTime.UtcNow.AddMinutes(50);

        return _currentToken;
    }

    public async Task<STKPushResponse> InitiateSTKPushAsync(STKPushRequest request)
    {
        try
        {
            var token = await GetAccessTokenAsync();
            var timestamp = GenerateTimestamp();
            var password = GeneratePassword(timestamp);
            var formattedPhone = FormatPhoneNumber(request.PhoneNumber);
            var amount = (int)Math.Round(request.Amount);

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
                CallBackURL = _config.CallbackUrl,
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
                amount, formattedPhone);

            var response = await client.SendAsync(httpRequest);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("STK Push response: {Content}", content);

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

            var result = JsonSerializer.Deserialize<STKQueryResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new STKQueryResponse { ResultCode = "1037" };
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

    public bool VerifySignature(string signature, object callbackData)
    {
        try
        {
            if (string.IsNullOrEmpty(signature)) return false;
            var json = JsonSerializer.Serialize(callbackData);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config.ConsumerSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
            var computed = Convert.ToBase64String(hash);
            return signature == computed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature verification failed");
            return false;
        }
    }
}
