using System;

namespace ChatbotMedico.Services.Interfaces;

public interface IWebhookSignatureValidator
{
    bool IsValid(byte[] rawBody, string? signatureHeader);
}
