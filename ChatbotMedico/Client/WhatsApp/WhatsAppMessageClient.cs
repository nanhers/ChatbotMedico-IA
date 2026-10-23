using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.WhatsApp;

namespace ChatbotMedico.Client.WhatsApp;

public sealed class WhatsAppMessageClient : IMessageClient
{
    private readonly HttpClient _http;
    private readonly ILogger<WhatsAppMessageClient> _logger;

    private readonly string _accessToken;
    private readonly string _phoneNumberId;
    private readonly string _apiBaseUrl;
    private readonly string _apiVersion;

    public WhatsAppMessageClient(IConfiguration configuration, ILogger<WhatsAppMessageClient> logger)
    {
        _logger = logger;

        _accessToken = configuration["WhatsApp:AccessToken"] ?? string.Empty;
        _phoneNumberId = configuration["WhatsApp:PhoneNumberId"] ?? string.Empty;
        _apiBaseUrl = configuration["WhatsApp:ApiBaseUrl"] ?? string.Empty;
        _apiVersion = configuration["WhatsApp:ApiVersion"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_accessToken))
            throw new InvalidOperationException("Falta configuración WhatsApp:AccessToken.");

        if (string.IsNullOrWhiteSpace(_phoneNumberId))
            throw new InvalidOperationException("Falta configuración WhatsApp:PhoneNumberId.");

        if (string.IsNullOrWhiteSpace(_apiBaseUrl))
            throw new InvalidOperationException("Falta configuración WhatsApp:ApiBaseUrl.");

        if (string.IsNullOrWhiteSpace(_apiVersion))
            throw new InvalidOperationException("Falta configuración WhatsApp:ApiVersion.");

        _http = new HttpClient
        {
            BaseAddress = new Uri(_apiBaseUrl.TrimEnd('/') + "/")
        };

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    public async Task SendTextAsync(string to, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("to es obligatorio.", nameof(to));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("message es obligatorio.", nameof(message));

        string path = $"{_apiVersion.Trim('/')}/{_phoneNumberId}/messages";

        WhatsAppSendMessageRequestDto body = new WhatsAppSendMessageRequestDto
        {
            To = to,
            Type = "text",
            Text = new WhatsAppSendMessageRequestDto.WhatsAppTextPayloadDto
            {
                Body = message
            }
        };

        string json = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using HttpResponseMessage resp = await _http.SendAsync(req, ct);
        string respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("WhatsApp send failed. status={StatusCode} body={Body}", (int)resp.StatusCode, respBody);
            throw new InvalidOperationException("No se pudo enviar el mensaje por WhatsApp.");
        }

        try
        {
            WhatsAppSendMessageResponseDto? dto = JsonSerializer.Deserialize<WhatsAppSendMessageResponseDto>(respBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            string? id = dto?.Messages?.FirstOrDefault()?.Id;
            _logger.LogInformation("WhatsApp message sent. to={To} messageId={MessageId}", to, id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo deserializar la respuesta de WhatsApp.");
        }
    }
}
