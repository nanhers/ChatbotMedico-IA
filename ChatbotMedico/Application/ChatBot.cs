using System.Text.Json;
using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.AI;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.DTOs.LLM;
using ChatbotMedico.DTOs.Messaging;
using ChatbotMedico.Services.Interfaces;

namespace ChatbotMedico.Application;

public class ChatBot
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(45);

    private readonly ILlmClient _llm;
    private readonly ICalendarService _calendarService;
    private readonly IMessageClient _messageClient;
    private readonly ILogger<ChatBot> _logger;

    public ChatBot(ILlmClient llm, ICalendarService calendarService, IMessageClient messageClient, ILogger<ChatBot> logger)
    {
        _llm = llm;
        _calendarService = calendarService;
        _messageClient = messageClient;
        _logger = logger;
    }

    public async Task<int> ProcessWhatsAppWebhookAsync(JsonElement payload, CancellationToken ct)
    {
        int processed = 0;
        int failed = 0;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            _logger.LogInformation("[WEBHOOK POST] Payload inválido: ValueKind={ValueKind}", payload.ValueKind);
            return 0;
        }

        if (!payload.TryGetProperty("entry", out JsonElement entryArr) || entryArr.ValueKind != JsonValueKind.Array)
        {
            _logger.LogInformation("[WEBHOOK POST] Payload sin entry[]");
            return 0;
        }

        foreach (JsonElement entry in entryArr.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out JsonElement changesArr) || changesArr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement change in changesArr.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out JsonElement valueObj) || valueObj.ValueKind != JsonValueKind.Object)
                    continue;

                if (!valueObj.TryGetProperty("messages", out JsonElement messagesArr) || messagesArr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement message in messagesArr.EnumerateArray())
                {
                    string? from = message.TryGetProperty("from", out JsonElement fromEl) ? fromEl.GetString() : null;
                    string? type = message.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() : null;

                    if (!string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!message.TryGetProperty("text", out JsonElement textObj) || textObj.ValueKind != JsonValueKind.Object)
                        continue;

                    string? body = textObj.TryGetProperty("body", out JsonElement bodyEl) ? bodyEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(body))
                        continue;

                    _logger.LogInformation(
                        "[WEBHOOK POST] Incoming text message from={From} bodyLength={BodyLength}",
                        from,
                        body.Length);

                    try
                    {
                        AiInterpretRequestDto req = new AiInterpretRequestDto { Text = body };
                        AiToolActionResultDto action = await InterpretAndScheduleAsync(req, ct);
                        processed++;

                        if (!string.IsNullOrWhiteSpace(from))
                        {
                            if (string.Equals(action.ToolName, "reply_message", StringComparison.OrdinalIgnoreCase))
                            {
                                string reply = string.IsNullOrWhiteSpace(action.ReplyMessage)
                                    ? "No pude interpretar tu mensaje para agendar cita."
                                    : action.ReplyMessage;

                                await _messageClient.SendTextAsync(from, reply, ct);
                            }
                            else if (string.Equals(action.ToolName, "schedule_appointment", StringComparison.OrdinalIgnoreCase))
                            {
                                await _messageClient.SendTextAsync(from, "Su cita quedó agendada.", ct);
                            }
                            else
                            {
                                await _messageClient.SendTextAsync(from, "No pude interpretar tu mensaje para agendar cita.", ct);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(ex,
                            "[WEBHOOK POST] Error procesando mensaje. from={From} bodyLength={BodyLength}",
                            from,
                            body.Length);

                        if (!string.IsNullOrWhiteSpace(from))
                        {
                            try
                            {
                                await _messageClient.SendTextAsync(from, "Hubo un error al agendar su cita.", ct);
                            }
                            catch (Exception sendEx)
                            {
                                _logger.LogError(sendEx, "[WEBHOOK POST] Error enviando mensaje WhatsApp de error. from={From}", from);
                            }
                        }
                    }
                }
            }
        }

        _logger.LogInformation("[WEBHOOK POST] processedMessages={Processed} failedMessages={Failed}", processed, failed);
        return processed;
    }

    public async Task<AiToolActionResultDto> InterpretAndScheduleAsync(AiInterpretRequestDto request, CancellationToken ct)
    {
        Dictionary<string, string[]> errors = ValidateRequest(request, out string text);
        if (errors.Count > 0)
        {
            KeyValuePair<string, string[]> first = errors.First();
            throw new ArgumentException(first.Value.FirstOrDefault() ?? "Datos inválidos", first.Key);
        }

        LlmRequestDto llmRequest = BuildLlmRequest(text);

        // Invocamos al LLM y capturamos cualquier excepción para loguearla
        LlmResponseDto response;
        try
        {
            response = await _llm.GetResponseAsync(llmRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al invocar el proveedor de IA.");
            throw new InvalidOperationException(ex.Message, ex);
        }


        string provider = string.IsNullOrWhiteSpace(response.Provider) ? "unknown" : response.Provider;

        _logger.LogInformation(
            "Response del LLM: model={Model} toolCalls={ToolCallsCount}",
            provider,
            response.ToolCalls.Count);

        // Una línea por tool-call (name + args)
        foreach (var tc in response.ToolCalls)
        {
            string name = string.IsNullOrWhiteSpace(tc.Name) ? "(none)" : tc.Name;
            string argsPreview = string.IsNullOrWhiteSpace(tc.ArgumentsJson) ? "none" : tc.ArgumentsJson;

            _logger.LogInformation("LLM: tool-call name={ToolCallName} args={ToolCallArgs}", name, argsPreview);
        }

        if (response.ToolCalls.Count > 1)
        {
            _logger.LogWarning("LLM devolvió múltiples tool-calls; se tomará el primero. toolCallsCount={ToolCallsCount}", response.ToolCalls.Count);
        }

        LlmToolCallDto? toolCall = response.ToolCalls.FirstOrDefault();

        if (toolCall is null || string.IsNullOrWhiteSpace(toolCall.Name))
        {
            return new AiToolActionResultDto
            {
                ToolName = string.Empty,
                ArgumentsJson = string.Empty,
                ReplyMessage = "No pude interpretar tu mensaje para agendar cita."
            };
        }

        return await ExecuteToolCall(toolCall, ct);
    }

    private static LlmRequestDto BuildLlmRequest(string text)
    {
        return new LlmRequestDto
        {
            Messages =
            [
                new LlmMessageDto
                {
                    Role = "system",
                    Content = "Eres un asistente para agendar citas médicas por WhatsApp. " +
                              "Tienes dos herramientas: (1) schedule_appointment para agendar una cita en Google Calendar y " +
                              "(2) reply_message para responder al usuario (incluyendo pedir datos faltantes o responder preguntas generales). " +
                              "Cuando el usuario quiera agendar y tengas TODOS los datos, llama a schedule_appointment. " +
                              "Extrae del texto: (1) calendarId (correo del calendario), (2) summary (nombre del paciente), " +
                              "(3) startTime en ISO-8601. Asume que la zona horaria es America/Mexico_City. " +
                              "El usuario siempre proporcionará fecha completa con año (ej. 16 de octubre del 2026) y hora. " +
                              "NO inventes datos. Si falta algo para agendar o si el usuario hace una pregunta general, llama a reply_message. " +
                              "Devuelve exactamente 1 tool-call."
                },
                new LlmMessageDto { Role = "user", Content = text }
            ],
            Tools =
            [
                new LlmToolDefinitionDto
                {
                    Name = "schedule_appointment",
                    Description = "Agenda una cita médica en Google Calendar",
                    ParametersJsonSchema = ScheduleAppointmentArgsJsonSchema
                },
                new LlmToolDefinitionDto
                {
                    Name = "reply_message",
                    Description = "Responde al usuario por WhatsApp (preguntar datos faltantes o responder preguntas generales)",
                    ParametersJsonSchema = ReplyMessageArgsJsonSchema
                }
            ]
        };
    }

    
    private async Task<AiToolActionResultDto> ExecuteToolCall(LlmToolCallDto toolCall, CancellationToken ct)
    {
        string toolName = toolCall.Name?.Trim() ?? string.Empty;
        string argsJson = toolCall.ArgumentsJson ?? string.Empty;

        // Validación común: aplica para cualquier tool (incluidas futuras)
        if (string.IsNullOrWhiteSpace(toolName) || string.IsNullOrWhiteSpace(argsJson))
        {
            return new AiToolActionResultDto
            {
                ToolName = "reply_message",
                ArgumentsJson = string.Empty,
                ReplyMessage = "No pude interpretar tu mensaje para agendar cita."
            };
        }

        if (string.Equals(toolName, "reply_message", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteReplyMessageToolCall(toolName, argsJson);
        }

        if (string.Equals(toolName, "schedule_appointment", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteScheduleAppointmentToolCallAsync(toolName, argsJson, ct);
        }

        return new AiToolActionResultDto
        {
            ToolName = toolName,
            ArgumentsJson = string.Empty,
            ReplyMessage = "No pude interpretar tu mensaje para agendar cita."
        };
    }

    private AiToolActionResultDto ExecuteReplyMessageToolCall(string toolName, string argsJson)
    {
        
        ReplyMessageArgsDto args;
        try
        {
            args = JsonSerializer.Deserialize<ReplyMessageArgsDto>(argsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new JsonException("JsonSerializer.Deserialize devolvió null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudieron deserializar los argumentos de la herramienta reply_message. args={ArgsJson}",
                argsJson);

            return new AiToolActionResultDto
            {
                ToolName = toolName,
                ArgumentsJson = argsJson,
                ReplyMessage = "No pude interpretar tu mensaje para agendar cita."
            };
        }

        return new AiToolActionResultDto
        {
            ToolName = toolName,
            ArgumentsJson = argsJson,
            ReplyMessage = args.Message
        };
    }

    private async Task<AiToolActionResultDto> ExecuteScheduleAppointmentToolCallAsync(string toolName, string argsJson, CancellationToken ct)
    {
        
        ScheduleAppointmentArgs args;
        try
        {
            args = JsonSerializer.Deserialize<ScheduleAppointmentArgs>(argsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new JsonException("JsonSerializer.Deserialize devolvió null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudieron deserializar los argumentos de la herramienta schedule_appointment. args={ArgsJson}",
                argsJson);

            throw new FormatException("No se pudieron leer los argumentos de la herramienta schedule_appointment.", ex);
        }

        if (string.IsNullOrWhiteSpace(args.CalendarId))
        {
            return new AiToolActionResultDto
            {
                ToolName = "reply_message",
                ArgumentsJson = string.Empty,
                ReplyMessage = "¿Cuál es el calendarId (correo del calendario) para agendar la cita?"
            };
        }

        if (string.IsNullOrWhiteSpace(args.Summary))
        {
            return new AiToolActionResultDto
            {
                ToolName = "reply_message",
                ArgumentsJson = string.Empty,
                ReplyMessage = "¿A nombre de quién agendo la cita (nombre del paciente)?"
            };
        }

        if (!DateTimeOffset.TryParse(args.StartTime, out DateTimeOffset start))
        {
            return new AiToolActionResultDto
            {
                ToolName = "reply_message",
                ArgumentsJson = string.Empty,
                ReplyMessage = "¿Para qué fecha y hora desea la cita? (incluya año y hora)"
            };
        }

        CreateCalendarEventRequestDto createReq = new CreateCalendarEventRequestDto
        {
            CalendarId = args.CalendarId,
            Summary = args.Summary,
            StartTime = start.ToString("O"),
            EndTime = start.Add(DefaultDuration).ToString("O")
        };

        CreateCalendarEventResponseDto created = await _calendarService.CreateEventAsync(createReq);

        return new AiToolActionResultDto
        {
            ToolName = toolName,
            ArgumentsJson = argsJson,
            CalendarEventResponse = created
        };
    }

    private const string ReplyMessageArgsJsonSchema = "{\n" +
        "  \"type\": \"object\",\n" +
        "  \"properties\": {\n" +
        "    \"message\": { \"type\": \"string\", \"description\": \"Mensaje a enviar al usuario\" }\n" +
        "  },\n" +
        "  \"required\": [\"message\" ]\n" +
        "}";

    private const string ScheduleAppointmentArgsJsonSchema = "{\n" +
        "  \"type\": \"object\",\n" +
        "  \"properties\": {\n" +
        "    \"calendarId\": { \"type\": \"string\", \"description\": \"Id del calendario destino (correo)\" },\n" +
        "    \"summary\": { \"type\": \"string\", \"description\": \"Nombre del paciente para el summary\" },\n" +
        "    \"startTime\": { \"type\": \"string\", \"description\": \"Fecha/hora inicio ISO-8601\" }\n" +
        "  },\n" +
        "  \"required\": [\"calendarId\", \"summary\", \"startTime\" ]\n" +
        "}";

    private sealed class ScheduleAppointmentArgs
    {
        public string CalendarId { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
    }

    private static Dictionary<string, string[]> ValidateRequest(AiInterpretRequestDto request, out string text)
    {
        Dictionary<string, string[]> errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        text = request.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            errors["text"] = ["text es obligatorio."];

        return errors;
    }

}