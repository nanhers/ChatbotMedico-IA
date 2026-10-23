using System.Text.Json.Serialization;

namespace ChatbotMedico.DTOs.WhatsApp;

public sealed class WhatsAppSendMessageRequestDto
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = "whatsapp";

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public WhatsAppTextPayloadDto Text { get; set; } = new WhatsAppTextPayloadDto();

    public sealed class WhatsAppTextPayloadDto
    {
        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;
    }
}
