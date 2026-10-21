using ChatbotMedico.Client.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace ChatbotMedico.Client;

public sealed class GoogleCalendarClient : ICalendarClient
{
    private readonly ILogger<GoogleCalendarClient> _logger;

    public GoogleCalendarClient(ILogger<GoogleCalendarClient> logger)
    {
        _logger = logger;
    }

    private async Task<CalendarService> CreateCalendarServiceAsync()
    {
        // ADC (Application Default Credentials): en Cloud Run usa Workload Identity.
        GoogleCredential credential = await GoogleCredential.GetApplicationDefaultAsync();
        if (credential.IsCreateScopedRequired)
        {
            credential = credential.CreateScoped(CalendarService.Scope.Calendar);
        }

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ChatbotMedico"
        });
    }

    public async Task<string> GetCalendarTimeZoneAsync(string calendarId)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            throw new ArgumentException("calendarId es obligatorio.", nameof(calendarId));
        }

        CalendarService svc = await CreateCalendarServiceAsync();

        // Calendars.Get: obtiene el recurso calendario por ID y expone TimeZone.
        // Nota: CalendarList.* depende de que el calendario esté en la lista del usuario (suscrito/visible),
        // lo cual no siempre aplica para identidades sin UI (p. ej. service accounts en Cloud Run).
        Calendar cal = await svc.Calendars.Get(calendarId).ExecuteAsync();
        string tz = cal.TimeZone?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(tz))
        {
            throw new InvalidOperationException($"No se pudo resolver la zona horaria del calendario '{calendarId}'.");
        }

        return tz;
    }

    public async Task<IReadOnlyList<CalendarEventInfo>> ListEventsAsync(
        string calendarId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string timeZone)
    {
        CalendarService svc = await CreateCalendarServiceAsync();

        EventsResource.ListRequest req = svc.Events.List(calendarId);
        req.SingleEvents = true;
        req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        req.TimeMinDateTimeOffset = windowStart;
        req.TimeMaxDateTimeOffset = windowEnd;
        req.TimeZone = timeZone;

        _logger.LogInformation(
            "Listando eventos en Google Calendar. calendarId={CalendarId} windowStart={WindowStart} windowEnd={WindowEnd} timeZone={TimeZone}",
            calendarId, windowStart, windowEnd, timeZone);

        Events result = await req.ExecuteAsync();
        IList<Event> items = result.Items ?? new List<Event>();

        List<CalendarEventInfo> mapped = new List<CalendarEventInfo>(items.Count);
        foreach (var ev in items)
        {
            CalendarEventInterval interval = MapEventToInterval(ev, timeZone);
            mapped.Add(new CalendarEventInfo(
                EventId: ev.Id ?? string.Empty,
                CalendarId: calendarId,
                Summary: ev.Summary,
                HtmlLink: ev.HtmlLink,
                Interval: interval));
        }

        return mapped;
    }

    public async Task<CalendarEventInfo> CreateEventAsync(
        string calendarId,
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string timeZone)
    {
        var svc = await CreateCalendarServiceAsync();

        Event ev = new Event
        {
            Summary = summary,
            Start = new EventDateTime { DateTimeDateTimeOffset = start, TimeZone = timeZone },
            End = new EventDateTime { DateTimeDateTimeOffset = end, TimeZone = timeZone }
        };

        _logger.LogInformation(
            "Creando evento en Google Calendar. calendarId={CalendarId} start={Start} end={End} timeZone={TimeZone}",
            calendarId, start, end, timeZone);

        EventsResource.InsertRequest insert = svc.Events.Insert(ev, calendarId);
        Event created = await insert.ExecuteAsync();

        CalendarEventInterval interval = MapEventToInterval(created, timeZone);
        return new CalendarEventInfo(
            EventId: created.Id ?? string.Empty,
            CalendarId: calendarId,
            Summary: created.Summary,
            HtmlLink: created.HtmlLink,
            Interval: interval);
    }

    public async Task<CalendarEventInfo?> GetEventAsync(string calendarId, string eventId, string timeZone)
    {
        var svc = await CreateCalendarServiceAsync();

        _logger.LogInformation(
            "Obteniendo evento de Google Calendar. calendarId={CalendarId} eventId={EventId} timeZone={TimeZone}",
            calendarId, eventId, timeZone);

        try
        {
            EventsResource.GetRequest get = svc.Events.Get(calendarId, eventId);
            get.TimeZone = timeZone;
            Event ev = await get.ExecuteAsync();

            CalendarEventInterval interval = MapEventToInterval(ev, timeZone);
            return new CalendarEventInfo(
                EventId: ev.Id ?? string.Empty,
                CalendarId: calendarId,
                Summary: ev.Summary,
                HtmlLink: ev.HtmlLink,
                Interval: interval);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static CalendarEventInterval MapEventToInterval(Event ev, string timeZone)
    {
        // Reglas: cualquier bloqueo cuenta (all-day o con hora).
        // - Si viene dateTime, usamos DateTimeOffset.
        // - Si viene date (all-day), interpretamos [date 00:00, nextDate 00:00) en la zona indicada.

        if (ev.Start?.DateTimeDateTimeOffset is not null && ev.End?.DateTimeDateTimeOffset is not null)
        {
            return new CalendarEventInterval(ev.Start.DateTimeDateTimeOffset.Value, ev.End.DateTimeDateTimeOffset.Value);
        }

        if (!string.IsNullOrWhiteSpace(ev.Start?.Date) && !string.IsNullOrWhiteSpace(ev.End?.Date))
        {
            // Nota: validación mínima de timeZone (sin librerías). Si no se puede resolver, usamos UTC.
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            }
            catch
            {
                tz = TimeZoneInfo.Utc;
            }

            DateOnly startDate = DateOnly.Parse(ev.Start.Date);
            DateOnly endDate = DateOnly.Parse(ev.End.Date);

            DateTime startLocal = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            DateTime endLocal = endDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

            DateTimeOffset startOffset = new DateTimeOffset(startLocal, tz.GetUtcOffset(startLocal));
            DateTimeOffset endOffset = new DateTimeOffset(endLocal, tz.GetUtcOffset(endLocal));

            return new CalendarEventInterval(startOffset, endOffset);
        }

        // Fallback defensivo si viene incompleto.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new CalendarEventInterval(now, now);
    }
}
