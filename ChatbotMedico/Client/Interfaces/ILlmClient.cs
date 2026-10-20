using ChatbotMedico.DTOs.LLM;

namespace ChatbotMedico.Client.Interfaces;

public interface ILlmClient
{
    Task<LlmResponseDto> GetResponseAsync(LlmRequestDto request, CancellationToken cancellationToken);
}
