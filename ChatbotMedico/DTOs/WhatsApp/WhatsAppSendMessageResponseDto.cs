using System.Text.Json.Serialization;

namespace ChatbotMedico.DTOs.WhatsApp;

public sealed class WhatsAppSendMessageResponseDto
{
    [JsonPropertyName("messages")]
    public List<WhatsAppMessageIdDto> Messages { get; set; } = new();

    public sealed class WhatsAppMessageIdDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
