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
    private readonly ILogger<GeminiLlmClient> _logger;

    private readonly int _maxRetries;
    private readonly int _initialDelaySeconds;
    private readonly double _backoffMultiplier;

    public GeminiLlmClient(IConfiguration configuration, ILogger<GeminiLlmClient> logger)
    {
        _logger = logger;

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

        int? maxRetries = configuration.GetValue<int?>("LlmSettings:MaxRetries");
        int? initialDelaySeconds = configuration.GetValue<int?>("LlmSettings:InitialDelaySeconds");
        double? backoffMultiplier = configuration.GetValue<double?>("LlmSettings:BackoffMultiplier");

        if (!maxRetries.HasValue)
        {
            throw new InvalidOperationException("Falta configuración LlmSettings:MaxRetries.");
        }

        if (!initialDelaySeconds.HasValue)
        {
            throw new InvalidOperationException("Falta configuración LlmSettings:InitialDelaySeconds.");
        }

        if (!backoffMultiplier.HasValue)
        {
            throw new InvalidOperationException("Falta configuración LlmSettings:BackoffMultiplier.");
        }

        if (maxRetries.Value < 0)
        {
            throw new InvalidOperationException("Configuración inválida LlmSettings:MaxRetries. Debe ser >= 0.");
        }

        if (initialDelaySeconds.Value < 0)
        {
            throw new InvalidOperationException("Configuración inválida LlmSettings:InitialDelaySeconds. Debe ser >= 0.");
        }

        if (double.IsNaN(backoffMultiplier.Value) || double.IsInfinity(backoffMultiplier.Value) || backoffMultiplier.Value < 1.0)
        {
            throw new InvalidOperationException("Configuración inválida LlmSettings:BackoffMultiplier. Debe ser >= 1.0.");
        }

        _maxRetries = maxRetries.Value;
        _initialDelaySeconds = initialDelaySeconds.Value;
        _backoffMultiplier = backoffMultiplier.Value;

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

        GenerateContentResponse response = await ExecuteWithRetryAsync(
            () => _client.Models.GenerateContentAsync(
                model: _model,
                contents: contents,
                config: config,
                cancellationToken: cancellationToken),
            cancellationToken);

        LlmResponseDto dto = new LlmResponseDto
        {
            Provider = _model
        };

        dto.ToolCalls = ExtractFunctionCalls(response);

        return dto;
    }

    private async Task<GenerateContentResponse> ExecuteWithRetryAsync(
        Func<Task<GenerateContentResponse>> action,
        CancellationToken cancellationToken)
    {
        int totalAttempts = 1 + _maxRetries;

        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Google.GenAI.ServerError ex) when (attempt < totalAttempts && IsRetryableServerError(ex))
            {
                TimeSpan delay = CalculateDelay(attempt);

                _logger.LogWarning(
                    ex,
                    "Gemini transient failure; retrying. model={Model} attempt={Attempt}/{TotalAttempts} delayMs={DelayMs}",
                    _model,
                    attempt,
                    totalAttempts,
                    (int)delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        // No debería llegar aquí: el último intento o retorna o lanza.
        throw new InvalidOperationException("Fallo inesperado en la política de reintentos de Gemini.");
    }

    private static bool IsRetryableServerError(Google.GenAI.ServerError ex)
    {
        // MVP: el caso reportado.
        string msg = ex.Message ?? string.Empty;
        return msg.Contains("high demand", StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        // attempt=1 falló y vamos a reintentar -> usar initialDelay * backoff^(attempt-1)
        double seconds = _initialDelaySeconds * Math.Pow(_backoffMultiplier, attempt - 1);
        if (seconds < 0) seconds = 0;
        return TimeSpan.FromSeconds(seconds);
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
