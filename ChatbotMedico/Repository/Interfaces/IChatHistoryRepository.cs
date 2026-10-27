using ChatbotMedico.DTOs.Messaging;

namespace ChatbotMedico.Repository.Interfaces;

public interface IChatHistoryRepository
{
    Task<ChatHistoryConversationDto?> GetAsync(string phoneDigits, CancellationToken ct);
    Task<bool> ExistsAsync(string phoneDigits, CancellationToken ct);
    Task CreateAsync(ChatHistoryConversationDto conversation, CancellationToken ct);
    Task UpdateAsync(ChatHistoryConversationDto conversation, CancellationToken ct);
    Task DeleteAsync(string phoneDigits, CancellationToken ct);
}
