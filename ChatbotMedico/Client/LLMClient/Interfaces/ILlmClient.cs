using ChatbotMedico.DTOs.LLM;

namespace ChatbotMedico.Client.LLMClient.Interfaces;

public interface ILlmClient
{
    Task<LlmResponseDto> GetResponseAsync(LlmRequestDto request, CancellationToken cancellationToken);
}
