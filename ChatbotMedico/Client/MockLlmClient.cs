using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.LLM;

namespace ChatbotMedico.Client;

public sealed class MockLlmClient : ILlmClient
{
    public string Provider { get; set; } = "gemini-3.6-flash";

    public List<LlmToolCallDto> ToolCalls { get; set; } =
    [
        new LlmToolCallDto
        {
            Name = "schedule_appointment",
            ArgumentsJson = "{\"calendarId\":\"fake-calendar@invalid\",\"startTime\":\"2026-10-25T10:00:00-06:00\",\"summary\":\"Juan Perez\"}"
        }
    ];

    public Exception? ExceptionToThrow { get; set; }

    public Task<LlmResponseDto> GetResponseAsync(LlmRequestDto request, CancellationToken cancellationToken)
    {
        if (ExceptionToThrow is not null)
        {
            return Task.FromException<LlmResponseDto>(ExceptionToThrow);
        }

        return Task.FromResult(new LlmResponseDto
        {
            Provider = Provider,
            ToolCalls = ToolCalls
        });
    }
}
