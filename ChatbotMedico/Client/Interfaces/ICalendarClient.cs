namespace ChatbotMedico.Client.Interfaces;

public sealed record CalendarEventInterval(DateTimeOffset Start, DateTimeOffset End);

public sealed record CalendarEventInfo(
    string EventId,
    string CalendarId,
    string? Summary,
    string? HtmlLink,
    CalendarEventInterval Interval);

public interface ICalendarClient
{
    Task<IReadOnlyList<CalendarEventInfo>> ListEventsAsync(
        string calendarId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string timeZone);

    Task<CalendarEventInfo> CreateEventAsync(
        string calendarId,
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string timeZone);

    Task<CalendarEventInfo?> GetEventAsync(
        string calendarId,
        string eventId,
        string timeZone);
}
