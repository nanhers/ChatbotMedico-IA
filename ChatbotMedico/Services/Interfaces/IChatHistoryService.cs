using ChatbotMedico.DTOs.Messaging;

namespace ChatbotMedico.Services.Interfaces;

public interface IChatHistoryService
{
    Task<ChatHistoryConversationDto?> GetAsync(string phoneDigits, CancellationToken ct);
    Task AppendAsync(string phoneDigits, ChatHistoryMessageDto message, CancellationToken ct);
    Task DeleteAsync(string phoneDigits, CancellationToken ct);
}
