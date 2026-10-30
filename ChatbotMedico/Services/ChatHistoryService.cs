using ChatbotMedico.DTOs.Messaging;
using ChatbotMedico.Repository.Interfaces;
using ChatbotMedico.Services.Interfaces;

namespace ChatbotMedico.Services;

public sealed class ChatHistoryService : IChatHistoryService
{
    private readonly IChatHistoryRepository _repo;
    private readonly int _maxHistoryMessages;

    public ChatHistoryService(IChatHistoryRepository repo, IConfiguration configuration)
    {
        _repo = repo;

        int? configured = configuration.GetValue<int?>("Firestore:MaxHistoryMessages");
        if (configured is null)
            throw new InvalidOperationException("Falta configuración obligatoria: Firestore:MaxHistoryMessages");

        if (configured.Value <= 0)
            throw new InvalidOperationException("Configuración inválida: Firestore:MaxHistoryMessages debe ser > 0");

        _maxHistoryMessages = configured.Value;
    }

    public Task<ChatHistoryConversationDto?> GetAsync(string phoneDigits, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneDigits))
            throw new ArgumentException("phoneDigits es obligatorio.", nameof(phoneDigits));

        return _repo.GetAsync(phoneDigits, ct);
    }

    public async Task AppendAsync(string phoneDigits, ChatHistoryMessageDto message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneDigits))
            throw new ArgumentException("phoneDigits es obligatorio.", nameof(phoneDigits));

        if (message is null)
            throw new ArgumentNullException(nameof(message));

        ChatHistoryConversationDto? existing = await _repo.GetAsync(phoneDigits, ct);

        if (existing is null)
        {
            var created = new ChatHistoryConversationDto
            {
                PhoneNumber = phoneDigits,
                Messages = new List<ChatHistoryMessageDto> { message }
            };

            created.Messages = TrimToMax(created.Messages, _maxHistoryMessages);
            await _repo.CreateAsync(created, ct);
            return;
        }

        existing.Messages ??= new List<ChatHistoryMessageDto>();
        existing.Messages.Add(message);
        existing.Messages = TrimToMax(existing.Messages, _maxHistoryMessages);

        await _repo.UpdateAsync(existing, ct);
    }

    public Task DeleteAsync(string phoneDigits, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneDigits))
            throw new ArgumentException("phoneDigits es obligatorio.", nameof(phoneDigits));

        return _repo.DeleteAsync(phoneDigits, ct);
    }

    private static List<ChatHistoryMessageDto> TrimToMax(List<ChatHistoryMessageDto> messages, int max)
    {
        if (messages.Count <= max) return messages;
        return messages.Skip(Math.Max(0, messages.Count - max)).ToList();
    }
}
