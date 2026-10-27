using ChatbotMedico.DTOs.Messaging;
using ChatbotMedico.Repository.Interfaces;
using Google.Cloud.Firestore;

namespace ChatbotMedico.Repository;

public sealed class FirestoreChatHistoryRepository : IChatHistoryRepository
{
    private readonly string _collectionName;

    private readonly FirestoreDb _db;
    private readonly ILogger<FirestoreChatHistoryRepository> _logger;

    public FirestoreChatHistoryRepository(ILogger<FirestoreChatHistoryRepository> logger)
    {
        _collectionName = "conversations";
        _logger = logger;

        // En Cloud Run (y local con gcloud auth / GOOGLE_APPLICATION_CREDENTIALS), esto usa ADC.
        // FirestoreDb.Create infiere el projectId del ambiente; si no, se puede configurar explícitamente.
        _db = FirestoreDb.Create();
    }

    public async Task<ChatHistoryConversationDto?> GetAsync(string phoneDigits, CancellationToken ct)
    {
        DocumentReference doc = _db.Collection(_collectionName).Document(phoneDigits);

        try
        {
            DocumentSnapshot snap = await doc.GetSnapshotAsync(ct);
            if (!snap.Exists)
                return null;

            string phone = snap.ContainsField("phoneNumber")
                ? snap.GetValue<string>("phoneNumber")
                : phoneDigits;

            List<ChatHistoryMessageDto> messages;
            if (snap.ContainsField("messages"))
            {
                try
                {
                    messages = snap.GetValue<List<ChatHistoryMessageDto>>("messages") ?? new List<ChatHistoryMessageDto>();
                }
                catch
                {
                    messages = new List<ChatHistoryMessageDto>();
                }
            }
            else
            {
                messages = new List<ChatHistoryMessageDto>();
            }

            return new ChatHistoryConversationDto
            {
                PhoneNumber = phone,
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firestore: no se pudo leer historial. collection={Collection} phone={Phone}", _collectionName, phoneDigits);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string phoneDigits, CancellationToken ct)
    {
        DocumentReference doc = _db.Collection(_collectionName).Document(phoneDigits);

        try
        {
            DocumentSnapshot snap = await doc.GetSnapshotAsync(ct);
            return snap.Exists;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firestore: no se pudo verificar existencia. collection={Collection} phone={Phone}", _collectionName, phoneDigits);
            throw;
        }
    }

    public async Task CreateAsync(ChatHistoryConversationDto conversation, CancellationToken ct)
    {
        DocumentReference doc = _db.Collection(_collectionName).Document(conversation.PhoneNumber);

        try
        {
            await _db.RunTransactionAsync(async transaction =>
            {
                DocumentSnapshot snap = await transaction.GetSnapshotAsync(doc, ct);
                if (snap.Exists)
                {
                    throw new InvalidOperationException($"La conversación ya existe. phone={conversation.PhoneNumber}");
                }

                var data = new Dictionary<string, object>
                {
                    ["phoneNumber"] = conversation.PhoneNumber,
                    ["messages"] = conversation.Messages ?? new List<ChatHistoryMessageDto>(),
                    ["updatedAt"] = FieldValue.ServerTimestamp
                };

                transaction.Create(doc, data);
            }, cancellationToken: ct);
        }
        catch
        {
            // No hacemos swallow: Create debe fallar si no se pudo persistir.
            throw;
        }
    }

    public async Task UpdateAsync(ChatHistoryConversationDto conversation, CancellationToken ct)
    {
        DocumentReference doc = _db.Collection(_collectionName).Document(conversation.PhoneNumber);

        try
        {
            await _db.RunTransactionAsync(async transaction =>
            {
                DocumentSnapshot snap = await transaction.GetSnapshotAsync(doc, ct);
                if (!snap.Exists)
                {
                    throw new InvalidOperationException($"No existe la conversación a actualizar. phone={conversation.PhoneNumber}");
                }

                var data = new Dictionary<string, object>
                {
                    ["phoneNumber"] = conversation.PhoneNumber,
                    ["messages"] = conversation.Messages ?? new List<ChatHistoryMessageDto>(),
                    ["updatedAt"] = FieldValue.ServerTimestamp
                };

                transaction.Set(doc, data, SetOptions.MergeAll);
            }, cancellationToken: ct);
        }
        catch
        {
            // No hacemos swallow: Update debe fallar si no se pudo persistir.
            throw;
        }
    }

    public async Task DeleteAsync(string phoneDigits, CancellationToken ct)
    {
        DocumentReference doc = _db.Collection(_collectionName).Document(phoneDigits);

        try
        {
            await doc.DeleteAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firestore: no se pudo borrar historial. collection={Collection} phone={Phone}", _collectionName, phoneDigits);
            throw;
        }
    }
}
