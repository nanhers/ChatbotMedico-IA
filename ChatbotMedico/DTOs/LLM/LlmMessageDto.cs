namespace ChatbotMedico.DTOs.LLM;

public sealed class LlmMessageDto
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}
