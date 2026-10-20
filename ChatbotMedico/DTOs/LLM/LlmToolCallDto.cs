namespace ChatbotMedico.DTOs.LLM;

public sealed class LlmToolCallDto
{
    public string Name { get; set; } = string.Empty;

    public string ArgumentsJson { get; set; } = string.Empty;
}
