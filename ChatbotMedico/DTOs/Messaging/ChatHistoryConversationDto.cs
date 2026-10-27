namespace ChatbotMedico.DTOs.Messaging;

public sealed class ChatHistoryConversationDto
{
    public string PhoneNumber { get; set; } = string.Empty;

    public List<ChatHistoryMessageDto> Messages { get; set; } = new();
}
