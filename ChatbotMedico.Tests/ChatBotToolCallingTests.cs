using ChatbotMedico.Application;
using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.AI;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.DTOs.LLM;
using ChatbotMedico.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class ChatBotToolCallingTests
{
    [Fact]
    public async Task InterpretAndScheduleAsync_WhenLlmFails_PropagatesMessageAsInvalidOperationException()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("429"));

        var sut = new ChatBot(llm.Object, calendar.Object, logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.InterpretAndScheduleAsync(new AiInterpretRequestDto
            {
                Text = "Hola soy Nancy quiero agendar una cita para el 16 de octubre del 2026 a las 10:00 am en el calendario nanhe2005@gmail.com."
            }, CancellationToken.None));

        Assert.Equal("429", ex.Message);
    }

    [Fact]
    public async Task InterpretAndScheduleAsync_WhenNoToolCall_ReturnsValidationError()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto { Provider = "fake" });

        var sut = new ChatBot(llm.Object, calendar.Object, logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.InterpretAndScheduleAsync(new AiInterpretRequestDto
            {
                Text = "Hola soy Nancy quiero agendar una cita para el 16 de octubre del 2026 a las 10:00 am en el calendario nanhe2005@gmail.com."
            }, CancellationToken.None));

        Assert.Equal("No se pudo interpretar el mensaje o faltan datos para agendar.", ex.Message);
    }

    [Fact]
    public async Task InterpretAndScheduleAsync_WhenToolCallPresent_SchedulesEvent()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto
            {
                Provider = "fake",
                ToolCalls =
                [
                    new LlmToolCallDto
                    {
                        Name = "schedule_appointment",
                        ArgumentsJson = "{\"calendarId\":\"nanhe2005@gmail.com\",\"summary\":\"Nancy\",\"startTime\":\"2026-10-16T10:00:00-06:00\"}"
                    }
                ]
            });

        calendar.Setup(x => x.CreateEventAsync(It.Is<CreateCalendarEventRequestDto>(r =>
                r.CalendarId == "nanhe2005@gmail.com" &&
                r.Summary == "Nancy" &&
                r.TimeZone == "America/Mexico_City" &&
                r.StartTime.StartsWith("2026-10-16T10:00:00") &&
                r.EndTime.StartsWith("2026-10-16T10:45:00"))))
            .ReturnsAsync(new CreateCalendarEventResponseDto
            {
                EventId = "evt1",
                CalendarId = "nanhe2005@gmail.com",
                StartTime = "2026-10-16T10:00:00-06:00",
                EndTime = "2026-10-16T10:45:00-06:00",
                TimeZone = "America/Mexico_City"
            });

        var sut = new ChatBot(llm.Object, calendar.Object, logger);

        var result = await sut.InterpretAndScheduleAsync(new AiInterpretRequestDto
        {
            Text = "Hola soy Nancy quiero agendar una cita para el 16 de octubre del 2026 a las 10:00 am en el calendario nanhe2005@gmail.com."
        }, CancellationToken.None);

        Assert.Equal("evt1", result.EventId);
        calendar.VerifyAll();
    }
}
