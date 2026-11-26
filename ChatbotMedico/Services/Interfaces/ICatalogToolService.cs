using ChatbotMedico.DTOs.AI;

namespace ChatbotMedico.Services.Interfaces;

public interface ICatalogToolService
{
    Task<AiToolActionResultDto> ConsultCatalogAsync(string catalogName, CancellationToken ct);
}
