using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class CalendarServiceTests
{
    private const string DefaultCalendarId = "primary";
    private const string DefaultTimeZone = "America/Mexico_City";

    private static CalendarService CreateSut(Mock<ICalendarClient> clientMock)
    {
        var logger = Mock.Of<ILogger<CalendarService>>();
        return new CalendarService(clientMock.Object, logger);
    }

    private static void SetupCalendarTimeZone(Mock<ICalendarClient> clientMock, string calendarId = DefaultCalendarId, string timeZone = DefaultTimeZone)
    {
        clientMock
            .Setup(c => c.GetCalendarTimeZoneAsync(calendarId))
            .ReturnsAsync(timeZone);
    }

    [Fact]
    public async Task CreateEventAsync_WhenTimeZoneIsEmpty_DoesNotFailValidation_ButResolvesFromCalendarId()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);

        var start = DateTimeOffset.Parse("2026-10-09T10:00:00-06:00");
        var end = DateTimeOffset.Parse("2026-10-09T10:45:00-06:00");

        SetupCalendarTimeZone(clientMock);

        clientMock
            .Setup(c => c.ListEventsAsync(DefaultCalendarId, start, end, DefaultTimeZone))
            .ReturnsAsync(Array.Empty<CalendarEventInfo>());

        clientMock
            .Setup(c => c.CreateEventAsync(DefaultCalendarId, "Cita", start, end, DefaultTimeZone))
            .ReturnsAsync(new CalendarEventInfo(
                EventId: "evt123",
                CalendarId: DefaultCalendarId,
                Summary: "Cita",
                HtmlLink: "https://example.invalid",
                Interval: new CalendarEventInterval(start, end)));

        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
            CalendarId = DefaultCalendarId,
            Summary = "Cita",
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            TimeZone = " "
        };

        var result = await sut.CreateEventAsync(req);

        Assert.Equal(DefaultTimeZone, result.TimeZone);
        clientMock.VerifyAll();
    }

    [Fact]
    public async Task CreateEventAsync_WhenStartIsAfterOrEqualEnd_ThrowsValidationProblemException()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);
        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
            CalendarId = DefaultCalendarId,
            Summary = "Cita",
            StartTime = "2026-10-09T10:00:00-06:00",
            EndTime = "2026-10-09T10:00:00-06:00",
            TimeZone = null
        };

        await Assert.ThrowsAsync<CalendarService.ValidationProblemException>(() => sut.CreateEventAsync(req));
    }

    [Fact]
    public async Task CreateEventAsync_WhenOverlapDetected_ThrowsScheduleConflictException_AndDoesNotCreate()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);

        var start = DateTimeOffset.Parse("2026-10-09T10:00:00-06:00");
        var end = DateTimeOffset.Parse("2026-10-09T10:45:00-06:00");

        SetupCalendarTimeZone(clientMock);

        clientMock
            .Setup(c => c.ListEventsAsync(DefaultCalendarId, start, end, DefaultTimeZone))
            .ReturnsAsync(new List<CalendarEventInfo>
            {
                new(
                    EventId: "e1",
                    CalendarId: DefaultCalendarId,
                    Summary: "Otro",
                    HtmlLink: null,
                    Interval: new CalendarEventInterval(
                        Start: DateTimeOffset.Parse("2026-10-09T10:30:00-06:00"),
                        End: DateTimeOffset.Parse("2026-10-09T11:00:00-06:00")))
            });

        // No debe llamarse CreateEventAsync cuando hay conflicto
        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
            CalendarId = DefaultCalendarId,
            Summary = "Cita",
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            TimeZone = null
        };

        await Assert.ThrowsAsync<CalendarService.ScheduleConflictException>(() => sut.CreateEventAsync(req));

        clientMock.Verify(c => c.CreateEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateEventAsync_WhenNoOverlap_CreatesEventOnce_AndReturnsEventId()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);

        var start = DateTimeOffset.Parse("2026-10-09T10:00:00-06:00");
        var end = DateTimeOffset.Parse("2026-10-09T10:45:00-06:00");

        SetupCalendarTimeZone(clientMock);

        clientMock
            .Setup(c => c.ListEventsAsync(DefaultCalendarId, start, end, DefaultTimeZone))
            .ReturnsAsync(Array.Empty<CalendarEventInfo>());

        clientMock
            .Setup(c => c.CreateEventAsync(DefaultCalendarId, "Cita", start, end, DefaultTimeZone))
            .ReturnsAsync(new CalendarEventInfo(
                EventId: "evt123",
                CalendarId: DefaultCalendarId,
                Summary: "Cita",
                HtmlLink: "https://example.invalid",
                Interval: new CalendarEventInterval(start, end)));

        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
            CalendarId = DefaultCalendarId,
            Summary = "Cita",
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            TimeZone = null
        };

        var result = await sut.CreateEventAsync(req);

        Assert.Equal("evt123", result.EventId);
        clientMock.Verify(c => c.CreateEventAsync(DefaultCalendarId, "Cita", start, end, DefaultTimeZone), Times.Once);
    }
}
