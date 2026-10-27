using ChatbotMedico.DTOs.Messaging;

namespace ChatbotMedico.Services.Interfaces;

public interface IChatHistoryService
{
    Task AppendAsync(string phoneDigits, ChatHistoryMessageDto message, CancellationToken ct);
}
