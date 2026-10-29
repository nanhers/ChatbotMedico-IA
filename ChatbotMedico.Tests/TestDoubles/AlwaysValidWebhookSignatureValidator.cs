using ChatbotMedico.Services.Interfaces;

namespace ChatbotMedico.Tests.TestDoubles;

public sealed class AlwaysValidWebhookSignatureValidator : IWebhookSignatureValidator
{
    public bool IsValid(byte[] rawBody, string? signatureHeader) => true;
}
