namespace ChatbotMedico.DTOs.Calendar;

public sealed class CreateCalendarEventRequestDto
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? TimeZone { get; set; }
}
