using ChatbotMedico.DTOs.Calendar;

namespace ChatbotMedico.DTOs.AI;

public sealed class AiToolActionResultDto
{
    public string ToolName { get; set; } = string.Empty;

    public string ArgumentsJson { get; set; } = string.Empty;

    public CreateCalendarEventResponseDto? CalendarEventResponse { get; set; }

    public string? ReplyMessage { get; set; }
}
