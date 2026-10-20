namespace ChatbotMedico.DTOs.LLM;

public sealed class LlmToolDefinitionDto
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // JSON Schema (string) para que sea provider-agnostic.
    public string ParametersJsonSchema { get; set; } = string.Empty;
}
