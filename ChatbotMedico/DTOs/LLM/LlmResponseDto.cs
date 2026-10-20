namespace ChatbotMedico.DTOs.LLM;

public sealed class LlmResponseDto
{
    public List<LlmToolCallDto> ToolCalls { get; set; } = new();

    public string? Provider { get; set; }
}
