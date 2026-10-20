using System.Text.Json;
using System.Text.Json.Nodes;
using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.LLM;
using Google.GenAI;
using Google.GenAI.Types;

namespace ChatbotMedico.Client.Gemini;

public sealed class GeminiLlmClient : ILlmClient
{
    private readonly Google.GenAI.Client _client;
    private readonly string _model;

    public GeminiLlmClient(IConfiguration configuration)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Falta configuración Gemini:ApiKey.");
        }

        _model = configuration["Gemini:Model"];
        if (string.IsNullOrWhiteSpace(_model))
        {
            _model = "gemini-3.6-flash";
        }

        _client = new Google.GenAI.Client(apiKey: apiKey);
    }

    public async Task<LlmResponseDto> GetResponseAsync(LlmRequestDto request, CancellationToken cancellationToken)
    {
        // Mapear mensajes: usamos SystemInstruction + contents (user).
        var (systemInstruction, contents) = MapMessages(request.Messages);

        // Mapear tools (function calling nativo de Gemini)
        List<Tool>? tools = MapTools(request.Tools);

        GenerateContentConfig config = new GenerateContentConfig
        {
            SystemInstruction = systemInstruction,
            Tools = tools
        };

        GenerateContentResponse response = await _client.Models.GenerateContentAsync(
            model: _model,
            contents: contents,
            config: config,
            cancellationToken: cancellationToken);

        LlmResponseDto dto = new LlmResponseDto
        {
            Provider = _model
        };

        dto.ToolCalls = ExtractFunctionCalls(response);

        return dto;
    }

    private static (Content? systemInstruction, List<Content> contents) MapMessages(List<LlmMessageDto> messages)
    {
        Content? system = null;
        List<Content> contents = new List<Content>();

        foreach (var m in messages)
        {
            string role = m.Role.Trim().ToLowerInvariant();
            if (role == "system")
            {
                system = new Content
                {
                    Parts = new List<Part> { new Part { Text = m.Content } }
                };
            }
            else
            {
                contents.Add(new Content
                {
                    Role = role == "assistant" ? "model" : "user",
                    Parts = new List<Part> { new Part { Text = m.Content } }
                });
            }
        }

        return (system, contents);
    }

    private static List<Tool>? MapTools(List<LlmToolDefinitionDto> toolDefinitions)
    {
        if (toolDefinitions.Count == 0) return null;

        List<FunctionDeclaration> functionDeclarations = new List<FunctionDeclaration>(toolDefinitions.Count);

        foreach (var t in toolDefinitions)
        {
            // El SDK recomienda usar ResponseJsonSchema para preservar orden. Para function calling,
            // convertimos el JSON schema a JsonNode y lo asignamos a ParametersJsonSchema.
            JsonNode? schemaNode = null;
            if (!string.IsNullOrWhiteSpace(t.ParametersJsonSchema))
            {
                schemaNode = JsonNode.Parse(t.ParametersJsonSchema);
            }

            functionDeclarations.Add(new FunctionDeclaration
            {
                Name = t.Name,
                Description = t.Description,
                ParametersJsonSchema = schemaNode
            });
        }

        return new List<Tool>
        {
            new Tool
            {
                FunctionDeclarations = functionDeclarations
            }
        };
    }

    private static List<LlmToolCallDto> ExtractFunctionCalls(GenerateContentResponse response)
    {
        List<LlmToolCallDto> toolCalls = new List<LlmToolCallDto>();

        var candidate = response.Candidates?.FirstOrDefault();
        var parts = candidate?.Content?.Parts;
        if (parts is null) return toolCalls;

        foreach (var p in parts)
        {
            // Forma oficial: FunctionCall en Part
            if (p.FunctionCall is null) continue;

            string name = p.FunctionCall.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Args viene como objeto; lo serializamos a JSON.
            string argsJson;
            try
            {
                argsJson = JsonSerializer.Serialize(p.FunctionCall.Args);
            }
            catch (Exception ex)
            {
                throw new FormatException("No se pudieron serializar los argumentos del function-call devuelto por el LLM.", ex);
            }

            toolCalls.Add(new LlmToolCallDto
            {
                Name = name,
                ArgumentsJson = argsJson
            });
        }

        return toolCalls;
    }
}
