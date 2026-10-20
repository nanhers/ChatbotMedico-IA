using ChatbotMedico.Client.Interfaces;

namespace ChatbotMedico.Client;

public sealed class MockCalendarClient : ICalendarClient
{
    private const string DefaultTimeZone = "America/Mexico_City";

    public Task<string> GetCalendarTimeZoneAsync(string calendarId)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            throw new ArgumentException("calendarId es obligatorio.", nameof(calendarId));
        }

        return Task.FromResult(DefaultTimeZone);
    }

    // Persistencia simple entre requests en ambiente local.
    // Es thread-safe y no depende del scope del request.
    private static readonly object Gate = new();
    private static readonly Dictionary<string, List<CalendarEventInfo>> EventsByCalendarId = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<CalendarEventInfo>> ListEventsAsync(
        string calendarId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string timeZone)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            return Task.FromResult<IReadOnlyList<CalendarEventInfo>>(Array.Empty<CalendarEventInfo>());
        }

        lock (Gate)
        {
            if (!EventsByCalendarId.TryGetValue(calendarId, out var list) || list.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<CalendarEventInfo>>(Array.Empty<CalendarEventInfo>());
            }

            // Half-open: [start, end)
            List<CalendarEventInfo> filtered = list
                .Where(e => e.Interval.Start < windowEnd && windowStart < e.Interval.End)
                .OrderBy(e => e.Interval.Start)
                .ToList();

            return Task.FromResult<IReadOnlyList<CalendarEventInfo>>(filtered);
        }
    }

    public Task<CalendarEventInfo> CreateEventAsync(
        string calendarId,
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string timeZone)
    {
        string id = Guid.NewGuid().ToString("N");

        CalendarEventInfo info = new CalendarEventInfo(
            EventId: id,
            CalendarId: calendarId,
            Summary: summary,
            HtmlLink: $"https://example.local/calendar/{calendarId}/events/{id}",
            Interval: new CalendarEventInterval(start, end));

        lock (Gate)
        {
            if (!EventsByCalendarId.TryGetValue(calendarId, out var list))
            {
                list = new List<CalendarEventInfo>();
                EventsByCalendarId[calendarId] = list;
            }

            list.Add(info);
        }

        return Task.FromResult(info);
    }

    public Task<CalendarEventInfo?> GetEventAsync(
        string calendarId,
        string eventId,
        string timeZone)
    {
        if (string.IsNullOrWhiteSpace(calendarId) || string.IsNullOrWhiteSpace(eventId))
        {
            return Task.FromResult<CalendarEventInfo?>(null);
        }

        lock (Gate)
        {
            if (!EventsByCalendarId.TryGetValue(calendarId, out var list) || list.Count == 0)
            {
                return Task.FromResult<CalendarEventInfo?>(null);
            }

            CalendarEventInfo? found = list.FirstOrDefault(e => string.Equals(e.EventId, eventId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(found);
        }
    }
}
