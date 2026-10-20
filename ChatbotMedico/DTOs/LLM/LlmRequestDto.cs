namespace ChatbotMedico.DTOs.LLM;

public sealed class LlmRequestDto
{
    public string? Model { get; set; }

    public List<LlmMessageDto> Messages { get; set; } = new();

    public List<LlmToolDefinitionDto> Tools { get; set; } = new();

    public Dictionary<string, object?>? Options { get; set; }
}
