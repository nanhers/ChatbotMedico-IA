using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatbotMedico.Client.GoogleCloudTask.Interfaces;
using ChatbotMedico.DTOs.Tasks;
using ChatbotMedico.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChatbotMedico.Services;

public sealed class WebhookGatewayService : IWebhookGatewayService
{
    private readonly IWebhookSignatureValidator _signatureValidator;
    private readonly ITaskDispatcherClient _taskDispatcher;
    private readonly ILogger<WebhookGatewayService> _logger;

    public WebhookGatewayService(
        IWebhookSignatureValidator signatureValidator,
        ITaskDispatcherClient taskDispatcher,
        ILogger<WebhookGatewayService> logger)
    {
        _signatureValidator = signatureValidator;
        _taskDispatcher = taskDispatcher;
        _logger = logger;
    }

    public async Task<WebhookGatewayResult> HandleAsync(byte[] rawBody, string? signatureHeader, CancellationToken ct)
    {
        if (!_signatureValidator.IsValid(rawBody, signatureHeader))
        {
            _logger.LogWarning("[WEBHOOK POST] Firma inválida. Respondiendo 401.");
            return WebhookGatewayResult.Unauthorized;
        }

        string correlationId = Guid.NewGuid().ToString("N");
        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId
        });

        try
        {
            var dto = new ProcessWebhookTaskRequestDto
            {
                RawBodyBase64 = Convert.ToBase64String(rawBody),
                SignatureHeader = signatureHeader,
                CorrelationId = correlationId
            };

            JsonDocument payload = JsonSerializer.SerializeToDocument(dto);
            await _taskDispatcher.DispatchAsync("process-webhook", payload, ct);
        }
        catch (Exception ex)
        {
            // Por requerimiento: ACK 200 aunque falle el encolado. Solo log.
            _logger.LogError(ex, "[WEBHOOK POST] Error encolando task; se responderá 200 para evitar reintentos de Meta.");
        }

        return WebhookGatewayResult.Ok;
    }
}
