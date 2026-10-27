using Google.Cloud.Firestore;

namespace ChatbotMedico.DTOs.Messaging;

[FirestoreData]
public sealed class ChatHistoryMessageDto
{
    [FirestoreProperty("timestamp")]
    public Timestamp Timestamp { get; set; }

    [FirestoreProperty("role")]
    public string Role { get; set; } = string.Empty;

    [FirestoreProperty("text")]
    public string Text { get; set; } = string.Empty;
}
