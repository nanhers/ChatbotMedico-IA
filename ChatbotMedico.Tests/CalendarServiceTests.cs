using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class CalendarServiceTests
{
    private static CalendarService CreateSut(Mock<ICalendarClient> clientMock)
    {
        var logger = Mock.Of<ILogger<CalendarService>>();
        return new CalendarService(clientMock.Object, logger);
    }

    [Fact]
    public async Task CreateEventAsync_WhenTimeZoneIsEmpty_ThrowsValidationProblemException()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);
        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
            CalendarId = "primary",
            Summary = "Cita",
            StartTime = "2026-10-09T10:00:00-06:00",
            EndTime = "2026-10-09T10:45:00-06:00",
            TimeZone = " "
        };

        await Assert.ThrowsAsync<CalendarService.ValidationProblemException>(() => sut.CreateEventAsync(req));
    }

    [Fact]
    public async Task CreateEventAsync_WhenStartIsAfterOrEqualEnd_ThrowsValidationProblemException()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);
        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
            CalendarId = "primary",
            Summary = "Cita",
            StartTime = "2026-10-09T10:00:00-06:00",
            EndTime = "2026-10-09T10:00:00-06:00",
            TimeZone = "America/Mexico_City"
        };

        await Assert.ThrowsAsync<CalendarService.ValidationProblemException>(() => sut.CreateEventAsync(req));
    }

    [Fact]
    public async Task CreateEventAsync_WhenOverlapDetected_ThrowsScheduleConflictException_AndDoesNotCreate()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);

        var start = DateTimeOffset.Parse("2026-10-09T10:00:00-06:00");
        var end = DateTimeOffset.Parse("2026-10-09T10:45:00-06:00");

        clientMock
            .Setup(c => c.ListEventsAsync("primary", start, end, "America/Mexico_City"))
            .ReturnsAsync(new List<CalendarEventInfo>
            {
                new(
                    EventId: "e1",
                    CalendarId: "primary",
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
            CalendarId = "primary",
            Summary = "Cita",
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            TimeZone = "America/Mexico_City"
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

        clientMock
            .Setup(c => c.ListEventsAsync("primary", start, end, "America/Mexico_City"))
            .ReturnsAsync(Array.Empty<CalendarEventInfo>());

        clientMock
            .Setup(c => c.CreateEventAsync("primary", "Cita", start, end, "America/Mexico_City"))
            .ReturnsAsync(new CalendarEventInfo(
                EventId: "evt123",
                CalendarId: "primary",
                Summary: "Cita",
                HtmlLink: "https://example.invalid",
                Interval: new CalendarEventInterval(start, end)));

        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
            CalendarId = "primary",
            Summary = "Cita",
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            TimeZone = "America/Mexico_City"
        };

        var result = await sut.CreateEventAsync(req);

        Assert.Equal("evt123", result.EventId);
        clientMock.Verify(c => c.CreateEventAsync("primary", "Cita", start, end, "America/Mexico_City"), Times.Once);
    }
}
