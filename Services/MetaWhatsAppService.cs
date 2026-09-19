using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ML.Charity.API.Client.Services;

public class MetaWhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MetaWhatsAppService> _logger;
    private readonly string _phoneNumberId;
    private readonly string _accessToken;
    private readonly string _apiUrl;
    private readonly string _templateName;
    private readonly string _languageCode;
    private readonly bool _includeUrlButton;

    public MetaWhatsAppService(HttpClient httpClient, IConfiguration config, ILogger<MetaWhatsAppService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _phoneNumberId = config["WhatsApp:PhoneNumberID"] ?? string.Empty;
        _accessToken = config["WhatsApp:AccessToken"] ?? string.Empty;
        _apiUrl = config["WhatsApp:ApiUrl"] ?? "https://graph.facebook.com/v25.0/";
        _templateName = config["WhatsApp:TemplateName"] ?? "madavoor_donation_receipt";
        _languageCode = config["WhatsApp:LanguageCode"] ?? "en";
        _includeUrlButton = config.GetValue<bool>("WhatsApp:IncludeUrlButton", false);
    }

    // OTP passwordless login is disabled / paused as not required now
    public Task SendOtpAsync(string phoneNumber, string otpCode)
    {
        return Task.CompletedTask;
    }

    public async Task<bool> SendReceiptAsync(string phoneNumber, string donorName, int kits, double amount, string receiptToken)
    {
        if (string.IsNullOrWhiteSpace(_phoneNumberId) || string.IsNullOrWhiteSpace(_accessToken))
        {
            _logger.LogWarning("[WhatsApp] WhatsApp credentials (PhoneNumberID or AccessToken) are missing or empty in configuration. Receipt skipped for {ReceiptToken}.", receiptToken);
            return false;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            _logger.LogWarning("[WhatsApp] Cannot send WhatsApp receipt for {ReceiptToken}: Phone number is missing.", receiptToken);
            return false;
        }

        // Meta requires country code with subscriber digits only (no '+', spaces, dashes, or parentheses)
        var formattedNumber = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(formattedNumber))
        {
            _logger.LogWarning("[WhatsApp] Invalid phone number '{PhoneNumber}' for receipt {ReceiptToken}.", phoneNumber, receiptToken);
            return false;
        }

        // Approved Template 'madavoor_donation_receipt':
        // Assalamu Alaikum {{1}},
        // Thank you for your generous contribution to the Ibadi Kit Challenge! 🤲
        // *Donation Details:*
        // 📦 Number of Kits: {{2}}
        // 💵 Total Amount: ₹{{3}}
        // 🧾 Receipt Token: {{4}}
        // May your contribution be accepted and rewarded abundantly.
        var safeDonorName = string.IsNullOrWhiteSpace(donorName) ? "Valued Donor" : donorName.Trim();
        var safeReceiptToken = string.IsNullOrWhiteSpace(receiptToken) ? "N/A" : receiptToken.Trim();
        var safeKitCount = kits > 0 ? kits.ToString() : "1";
        var safeAmount = amount.ToString("0.00"); // ₹ symbol is hardcoded in template, so pass numeric value only

        // 1. Mandatory Body Component matching the 4 template parameters
        var components = new List<object>
        {
            new
            {
                type = "body",
                parameters = new[]
                {
                    new { type = "text", text = safeDonorName },     // {{1}} Donor Name
                    new { type = "text", text = safeKitCount },       // {{2}} Kit Count
                    new { type = "text", text = safeAmount },         // {{3}} Total Amount (e.g. 5000.00)
                    new { type = "text", text = safeReceiptToken }   // {{4}} Receipt Token (e.g. MDV-7F2A9)
                }
            }
        };

        // 2. Optional Dynamic URL Button (only included if template was created with dynamic button in Meta Dashboard)
        if (_includeUrlButton)
        {
            components.Add(new
            {
                type = "button",
                sub_type = "url",
                index = "0",
                parameters = new[]
                {
                    new { type = "text", text = receiptToken } // Dynamic URL suffix (e.g., https://charity.madavoor.app/badge/{{1}})
                }
            });
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = formattedNumber,
            type = "template",
            template = new
            {
                name = _templateName,
                language = new { code = _languageCode },
                components = components.ToArray()
            }
        };

        var baseUrl = _apiUrl.TrimEnd('/');
        var requestUrl = $"{baseUrl}/{_phoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var jsonPayload = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[WhatsApp] Receipt sent successfully for token {ReceiptToken} to {Recipient}. Meta Response: {Response}",
                    receiptToken, formattedNumber, responseBody);
                return true;
            }
            else
            {
                _logger.LogError("[WhatsApp] Meta API call failed ({StatusCode}) for receipt {ReceiptToken} to {Recipient}. Response: {Response}",
                    response.StatusCode, receiptToken, formattedNumber, responseBody);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WhatsApp] Exception while sending receipt {ReceiptToken} to {Recipient}",
                receiptToken, formattedNumber);
            throw;
        }
    }
}