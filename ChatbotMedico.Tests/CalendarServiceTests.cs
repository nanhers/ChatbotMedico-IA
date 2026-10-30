using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class CalendarServiceTests
{
    private const string DefaultCalendarId = "primary";
    private const string DefaultTimeZone = "America/Mexico_City";

    private static CalendarService CreateSut(Mock<ICalendarClient> clientMock, string calendarId = DefaultCalendarId)
    {
        var logger = Mock.Of<ILogger<CalendarService>>();

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Calendar:CalendarId"] = calendarId
            })
            .Build();

        return new CalendarService(clientMock.Object, logger, config);
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

        var start = DateTimeOffset.Now.AddMinutes(5);
        var end = start.AddMinutes(45);

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
            Summary = "Cita",
            StartTime = "2026-10-09T10:00:00-06:00",
            EndTime = "2026-10-09T10:00:00-06:00",
            TimeZone = null
        };

        await Assert.ThrowsAsync<CalendarService.ValidationProblemException>(() => sut.CreateEventAsync(req));
    }

    [Fact]
    public async Task CreateEventAsync_WhenStartIsInPast_ThrowsValidationProblemException_AndDoesNotCallClient()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);

        // Fecha muy en el pasado para evitar flakiness.
        var start = DateTimeOffset.UtcNow.AddDays(-10);
        var end = start.AddMinutes(45);

        SetupCalendarTimeZone(clientMock);

        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
            Summary = "Cita",
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            TimeZone = null
        };

        await Assert.ThrowsAsync<CalendarService.ValidationProblemException>(() => sut.CreateEventAsync(req));

        clientMock.Verify(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()), Times.Never);
        clientMock.Verify(c => c.CreateEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateEventAsync_WhenStartIsWithinGrace_AllowsFlow()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);

        // Dentro de tolerancia (60s). Usamos 30s para evitar borde.
        var start = DateTimeOffset.UtcNow.AddSeconds(-30);
        var end = start.AddMinutes(45);

        SetupCalendarTimeZone(clientMock);

        clientMock
            .Setup(c => c.ListEventsAsync(DefaultCalendarId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), DefaultTimeZone))
            .ReturnsAsync(Array.Empty<CalendarEventInfo>());

        clientMock
            .Setup(c => c.CreateEventAsync(DefaultCalendarId, "Cita", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), DefaultTimeZone))
            .ReturnsAsync(new CalendarEventInfo(
                EventId: "evt123",
                CalendarId: DefaultCalendarId,
                Summary: "Cita",
                HtmlLink: "https://example.invalid",
                Interval: new CalendarEventInterval(start, end)));

        var sut = CreateSut(clientMock);

        var createReq = new CreateCalendarEventRequestDto
        {
            Summary = "Cita",
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            TimeZone = null
        };

        var result = await sut.CreateEventAsync(createReq);

        Assert.Equal("evt123", result.EventId);
        clientMock.VerifyAll();
    }

    [Fact]
    public async Task CreateEventAsync_WhenOverlapDetected_ThrowsScheduleConflictException_AndDoesNotCreate()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);

        var start = DateTimeOffset.Now.AddMinutes(5);
        var end = start.AddMinutes(45);

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
                        Start: start.AddMinutes(30),
                        End: start.AddMinutes(60)))
            });

        // No debe llamarse CreateEventAsync cuando hay conflicto
        var sut = CreateSut(clientMock);

        var req = new CreateCalendarEventRequestDto
        {
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

        var start = DateTimeOffset.Now.AddMinutes(5);
        var end = start.AddMinutes(45);

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
            Summary = "Cita",
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            TimeZone = null
        };

        var result = await sut.CreateEventAsync(req);

        Assert.Equal("evt123", result.EventId);
        clientMock.Verify(c => c.CreateEventAsync(DefaultCalendarId, "Cita", start, end, DefaultTimeZone), Times.Once);
    }

    [Fact]
    public void Constructor_WhenCalendarIdConfigIsMissing_ThrowsInvalidOperationException()
    {
        var clientMock = new Mock<ICalendarClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<CalendarService>>();

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Calendar:CalendarId"] = " "
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new CalendarService(clientMock.Object, logger, config));
    }
}
