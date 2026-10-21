using System.Text.Json;
using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.AI;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.DTOs.LLM;
using ChatbotMedico.Services.Interfaces;

namespace ChatbotMedico.Application;

public class ChatBot
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(45);

    private readonly ILlmClient _llm;
    private readonly ICalendarService _calendarService;
    private readonly ILogger<ChatBot> _logger;

    public ChatBot(ILlmClient llm, ICalendarService calendarService, ILogger<ChatBot> logger)
    {
        _llm = llm;
        _calendarService = calendarService;
        _logger = logger;
    }

    public async Task<CreateCalendarEventResponseDto> InterpretAndScheduleAsync(AiInterpretRequestDto request, CancellationToken ct)
    {
        Dictionary<string, string[]> errors = ValidateRequest(request, out string text);
        if (errors.Count > 0)
        {
            // Sin excepción personalizada: usamos ArgumentException con el primer error.
            KeyValuePair<string, string[]> first = errors.First();
            throw new ArgumentException(first.Value.FirstOrDefault() ?? "Datos inválidos", first.Key);
        }

        LlmRequestDto llmRequest = new LlmRequestDto
        {
            Messages =
            [
                new LlmMessageDto
                {
                    Role = "system",
                    Content = "Eres un asistente para agendar citas médicas. " +
                              "Cuando el usuario quiera agendar, llama a la herramienta schedule_appointment. " +
                              "Extrae del texto: (1) calendarId (correo del calendario), (2) summary (nombre del paciente), " +
                              "(3) startTime en ISO-8601. Asume que la zona horaria es America/Mexico_City." +
                              "El usuario siempre proporcionará fecha completa con año (ej. 16 de octubre del 2026) y hora. " +
                              "NO inventes datos. Si falta algo, NO llames herramientas."
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
                }
            ]
        };

        LlmResponseDto response;
        try
        {
            response = await _llm.GetResponseAsync(llmRequest, ct);
        }
        catch (Exception ex)
        {
            // No mencionamos proveedor; propagamos el mensaje real.
            _logger.LogWarning(ex, "Fallo al invocar el proveedor de IA.");
            throw new InvalidOperationException(ex.Message, ex);
        }

        string provider = string.IsNullOrWhiteSpace(response.Provider) ? "unknown" : response.Provider;

        // Un solo log resumen del LLM (neutral al proveedor)
        _logger.LogInformation(
            "Response del LLM: model={Model} toolCalls={ToolCallsCount}",
            provider,
            response.ToolCalls.Count);

        // Una línea por tool-call (name + args)
        foreach (var tc in response.ToolCalls)
        {
            string name = string.IsNullOrWhiteSpace(tc.Name) ? "(none)" : tc.Name;
            string argsJson = string.IsNullOrWhiteSpace(tc.ArgumentsJson) ? "none" : tc.ArgumentsJson;

            _logger.LogInformation("LLM: tool-call name={ToolCallName} args={ToolCallArgs}", name, argsJson);
        }

        LlmToolCallDto? toolCall = response.ToolCalls.FirstOrDefault(t => string.Equals(t.Name, "schedule_appointment", StringComparison.OrdinalIgnoreCase));
        if (toolCall is null)
        {
            throw new InvalidOperationException("No se pudo interpretar el mensaje o faltan datos para agendar.");
        }

        if (string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            throw new FormatException("No se pudieron leer los argumentos de la herramienta schedule_appointment: ArgumentsJson viene vacío.");
        }

        ScheduleAppointmentArgs args;
        try
        {
            args = JsonSerializer.Deserialize<ScheduleAppointmentArgs>(toolCall.ArgumentsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new JsonException("JsonSerializer.Deserialize devolvió null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudieron deserializar los argumentos de la herramienta schedule_appointment. args={ArgsJson}",
                toolCall.ArgumentsJson);

            throw new FormatException("No se pudieron leer los argumentos de la herramienta schedule_appointment.", ex);
        }

        if (string.IsNullOrWhiteSpace(args.CalendarId))
        {
            throw new InvalidOperationException("Falta calendarId en los argumentos de la herramienta.");
        }

        if (string.IsNullOrWhiteSpace(args.Summary))
        {
            throw new InvalidOperationException("Falta summary en los argumentos de la herramienta.");
        }

        if (!DateTimeOffset.TryParse(args.StartTime, out DateTimeOffset start))
        {
            throw new FormatException("startTime no es ISO-8601 válido.");
        }

        CreateCalendarEventRequestDto createReq = new CreateCalendarEventRequestDto
        {
            CalendarId = args.CalendarId,
            Summary = args.Summary,
            StartTime = start.ToString("O"),
            EndTime = start.Add(DefaultDuration).ToString("O")
        };

        return await _calendarService.CreateEventAsync(createReq);
    }

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
