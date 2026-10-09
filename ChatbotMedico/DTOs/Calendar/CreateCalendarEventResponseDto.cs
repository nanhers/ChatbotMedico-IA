namespace ChatbotMedico.DTOs.Calendar;

public sealed class CreateCalendarEventResponseDto
{
    public string EventId { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string? HtmlLink { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
}
