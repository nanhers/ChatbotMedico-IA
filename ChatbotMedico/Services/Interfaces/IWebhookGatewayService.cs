using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChatbotMedico.Services.Interfaces;

public interface IWebhookGatewayService
{
    Task<WebhookGatewayResult> HandleAsync(byte[] rawBody, string? signatureHeader, CancellationToken ct);
}

public enum WebhookGatewayResult
{
    Ok = 0,
    Unauthorized = 1
}
