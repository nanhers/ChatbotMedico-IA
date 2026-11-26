using ChatbotMedico.DTOs.Sedes;

namespace ChatbotMedico.Repository.Interfaces;

public interface ISedesRepository
{
    Task<SedeDto?> GetSingleAsync(CancellationToken ct);
}
