namespace ChatbotMedico.DTOs.Tasks;

public sealed class ProcessWebhookTaskRequestDto
{
    public string RawBodyBase64 { get; set; } = string.Empty;
    public string? SignatureHeader { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
