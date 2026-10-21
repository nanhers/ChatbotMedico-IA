using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotMedico.Services;

public sealed class CalendarService : ICalendarService
{
    private readonly ICalendarClient _calendarClient;
    private readonly ILogger<CalendarService> _logger;

    public CalendarService(ICalendarClient calendarClient, ILogger<CalendarService> logger)
    {
        _calendarClient = calendarClient;
        _logger = logger;
    }

    public async Task<CreateCalendarEventResponseDto> CreateEventAsync(CreateCalendarEventRequestDto request)
    {
        Dictionary<string, string[]> errors = ValidateCreateRequest(request, out string calendarId, out string summary, out DateTimeOffset start, out DateTimeOffset end);
        if (errors.Count > 0)
        {
            throw new ValidationProblemException(errors);
        }

        string calendarTimeZone = await ResolveCalendarTimeZoneOrThrowAsync(calendarId);

        _logger.LogInformation(
            "Calendar: validando traslape calendarId={CalendarId} start={Start} end={End} timeZone={TimeZone}",
            calendarId,
            start,
            end,
            calendarTimeZone);

        // Buscar eventos solo en la ventana solicitada y validar traslape.
        IReadOnlyList<CalendarEventInfo> existing = await _calendarClient.ListEventsAsync(calendarId, start, end, calendarTimeZone);

        if (HasOverlap(existing.Select(e => e.Interval), start, end))
        {
            throw new ScheduleConflictException("Conflicto de agenda: el intervalo solicitado se traslapa con un evento existente.");
        }

        CalendarEventInfo created = await _calendarClient.CreateEventAsync(calendarId, summary, start, end, calendarTimeZone);

        _logger.LogInformation(
            "Calendar: evento creado calendarId={CalendarId} eventId={EventId} start={Start} end={End} timeZone={TimeZone} htmlLink={HtmlLink}",
            created.CalendarId,
            created.EventId,
            created.Interval.Start,
            created.Interval.End,
            calendarTimeZone,
            created.HtmlLink);

        return new CreateCalendarEventResponseDto
        {
            EventId = created.EventId,
            CalendarId = created.CalendarId,
            HtmlLink = created.HtmlLink,
            StartTime = request.StartTime!,
            EndTime = request.EndTime!,
            TimeZone = calendarTimeZone
        };
    }

    public async Task<GetCalendarEventResponseDto?> GetByIdAsync(string calendarId, string eventId)
    {
        string calendarTimeZone = await ResolveCalendarTimeZoneOrThrowAsync(calendarId);

        CalendarEventInfo? info = await _calendarClient.GetEventAsync(calendarId, eventId, calendarTimeZone);
        if (info is null) return null;

        return new GetCalendarEventResponseDto
        {
            EventId = info.EventId,
            CalendarId = info.CalendarId,
            Summary = info.Summary,
            HtmlLink = info.HtmlLink,
            StartTime = info.Interval.Start.ToString("O"),
            EndTime = info.Interval.End.ToString("O"),
            TimeZone = calendarTimeZone
        };
    }

    private static bool HasOverlap(IEnumerable<CalendarEventInterval> existingIntervals, DateTimeOffset requestedStart, DateTimeOffset requestedEnd)
    {
        // Half-open: [start, end)
        foreach (var it in existingIntervals)
        {
            if (it.Start < requestedEnd && requestedStart < it.End)
            {
                return true;
            }
        }
        return false;
    }

    private async Task<string> ResolveCalendarTimeZoneOrThrowAsync(string calendarId)
    {
        try
        {
            string tz = await _calendarClient.GetCalendarTimeZoneAsync(calendarId);
            if (string.IsNullOrWhiteSpace(tz))
            {
                throw new InvalidOperationException($"No se pudo resolver la zona horaria del calendario '{calendarId}'.");
            }
            return tz;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo resolver la zona horaria del calendario. calendarId={CalendarId}", calendarId);
            throw new ValidationProblemException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["calendar_time_zone"] = ["No se pudo resolver la zona horaria del calendario."]
            });
        }
    }

    private static Dictionary<string, string[]> ValidateCreateRequest(
        CreateCalendarEventRequestDto request,
        out string calendarId,
        out string summary,
        out DateTimeOffset start,
        out DateTimeOffset end)
    {
        Dictionary<string, string[]> errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        calendarId = request.CalendarId?.Trim() ?? string.Empty;
        summary = request.Summary?.Trim() ?? string.Empty;

        // Inicializar out params para evitar CS0177/CS0269
        start = default;
        end = default;

        if (string.IsNullOrWhiteSpace(calendarId))
            errors["calendar_id"] = ["calendar_id es obligatorio."];

        if (string.IsNullOrWhiteSpace(summary))
            errors["summary"] = ["summary es obligatorio."];


        if (string.IsNullOrWhiteSpace(request.StartTime) || !DateTimeOffset.TryParse(request.StartTime, out start))
            errors["start_time"] = ["start_time es obligatorio y debe ser una fecha válida (ISO-8601)."];

        if (string.IsNullOrWhiteSpace(request.EndTime) || !DateTimeOffset.TryParse(request.EndTime, out end))
            errors["end_time"] = ["end_time es obligatorio y debe ser una fecha válida (ISO-8601)."];

        if (!errors.ContainsKey("start_time") && !errors.ContainsKey("end_time"))
        {
            if (start >= end)
                errors["date_range"] = ["start_time debe ser menor que end_time."];
        }

        return errors;
    }

    public sealed class ValidationProblemException : Exception
    {
        public ValidationProblemException(Dictionary<string, string[]> errors)
            : base("Datos inválidos")
        {
            Errors = errors;
        }

        public Dictionary<string, string[]> Errors { get; }
    }

    public sealed class ScheduleConflictException : Exception
    {
        public ScheduleConflictException(string message) : base(message) { }
    }
}
