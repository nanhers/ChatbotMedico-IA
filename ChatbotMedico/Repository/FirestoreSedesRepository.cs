using ChatbotMedico.DTOs.Sedes;
using ChatbotMedico.Repository.Interfaces;
using Google.Cloud.Firestore;

namespace ChatbotMedico.Repository;

public sealed class FirestoreSedesRepository : ISedesRepository
{
    private const string CollectionName = "sedes";

    private readonly FirestoreDb _db;
    private readonly ILogger<FirestoreSedesRepository> _logger;

    public FirestoreSedesRepository(FirestoreDb db, ILogger<FirestoreSedesRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SedeDto?> GetSingleAsync(CancellationToken ct)
    {
        QuerySnapshot snapshot = await _db.Collection(CollectionName)
            .Limit(1)
            .GetSnapshotAsync(ct);

        DocumentSnapshot? doc = snapshot.Documents.FirstOrDefault();
        if (doc is null || !doc.Exists)
        {
            return null;
        }

        // MVP: campos name, address, googleMapsUrl
        // Si falta alguno, devolvemos string.Empty; el manejo de error global debe ser genérico.
        var sede = new SedeDto
        {
            Name = doc.ContainsField("name") ? (doc.GetValue<string>("name") ?? string.Empty) : string.Empty,
            Address = doc.ContainsField("address") ? (doc.GetValue<string>("address") ?? string.Empty) : string.Empty,
            GoogleMapsUrl = doc.ContainsField("googleMapsUrl") ? doc.GetValue<string>("googleMapsUrl") : null
        };

        return sede;
    }
}
