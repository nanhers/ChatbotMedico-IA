using ChatbotMedico.DTOs.Calendar;

namespace ChatbotMedico.Services.Interfaces;

public interface ICalendarService
{
    Task<CreateCalendarEventResponseDto> CreateEventAsync(CreateCalendarEventRequestDto request);

    Task<GetCalendarEventResponseDto?> GetByIdAsync(string calendarId, string eventId, string timeZone);
}
