using System.Text.Json;
using ChatbotMedico.Application;
using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.AI;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.DTOs.LLM;
using ChatbotMedico.Services.Interfaces;
using ChatbotMedico.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class ChatBotTests
{
    private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement BuildWebhookPayload(params string[] messageBodies)
    {
        var messagesJson = string.Join(",",
            messageBodies.Select((b, i) =>
                $"{{\"from\":\"user{i + 1}\",\"type\":\"text\",\"text\":{{\"body\":{JsonSerializer.Serialize(b)} }} }}"));

        string json = $"{{\"entry\":[{{\"changes\":[{{\"value\":{{\"messages\":[{messagesJson}]}}}}]}}]}}";
        return ParseJson(json);
    }

    private static JsonElement BuildWebhookPayloadWithMessagesJson(string messagesJson)
    {
        string json = $"{{\"entry\":[{{\"changes\":[{{\"value\":{{\"messages\":[{messagesJson}]}}}}]}}]}}";
        return ParseJson(json);
    }

    private static LlmResponseDto ToolCallResponse(string summary = "Juan Perez", string startTime = "2026-10-16T10:00:00-06:00")
        => new LlmResponseDto
        {
            Provider = "fake",
            ToolCalls =
            [
                new LlmToolCallDto
                {
                    Name = "schedule_appointment",
                    ArgumentsJson = $"{{\"summary\":\"{summary}\",\"startTime\":\"{startTime}\"}}"
                }
            ]
        };

    private static CreateCalendarEventResponseDto CalendarOk(string eventId, string startTime, string endTime)
        => new CreateCalendarEventResponseDto
        {
            EventId = eventId,
            StartTime = startTime,
            EndTime = endTime,
            TimeZone = "America/Mexico_City"
        };
    [Fact]
    public async Task InterpretAndScheduleAsync_WhenLlmFails_PropagatesMessageAsInvalidOperationException()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("429"));

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var signatureValidator = new Mock<ChatbotMedico.Services.Interfaces.IWebhookSignatureValidator>(MockBehavior.Strict);
        signatureValidator.Setup(x => x.IsValid(It.IsAny<byte[]>(), It.IsAny<string?>())).Returns(true);

        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, signatureValidator.Object, logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.InterpretAndScheduleAsync(new AiInterpretRequestDto
            {
                PhoneNumber = "user1",
                Text = "Hola soy Juan Perez quiero agendar una cita para el 16 de octubre del 2026 a las 10:00 am en el calendario fake-calendar@invalid."
            }, CancellationToken.None));

        Assert.Equal("429", ex.Message);
    }

    [Fact]
    public async Task InterpretAndScheduleAsync_WhenNoToolCall_ReturnsFallbackReplyMessage()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto { Provider = "fake" });

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var signatureValidator = new Mock<ChatbotMedico.Services.Interfaces.IWebhookSignatureValidator>(MockBehavior.Strict);
        signatureValidator.Setup(x => x.IsValid(It.IsAny<byte[]>(), It.IsAny<string?>())).Returns(true);

        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, signatureValidator.Object, logger);

        AiToolActionResultDto result = await sut.InterpretAndScheduleAsync(new AiInterpretRequestDto
        {
            PhoneNumber = "user1",
            Text = "Hola soy Juan Perez quiero agendar una cita para el 16 de octubre del 2026 a las 10:00 am en el calendario fake-calendar@invalid."
        }, CancellationToken.None);

        Assert.Equal("No pude interpretar tu mensaje para agendar cita.", result.ReplyMessage);
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
                        ArgumentsJson = "{\"summary\":\"Juan Perez\",\"startTime\":\"2026-10-16T10:00:00-06:00\"}"
                    }
                ]
            });

        calendar.Setup(x => x.CreateEventAsync(It.Is<CreateCalendarEventRequestDto>(r =>
                r.Summary == "Juan Perez" &&
                string.IsNullOrWhiteSpace(r.TimeZone) &&
                r.StartTime.StartsWith("2026-10-16T10:00:00") &&
                r.EndTime.StartsWith("2026-10-16T10:45:00"))))
            .ReturnsAsync(new CreateCalendarEventResponseDto
            {
                EventId = "evt1",
                StartTime = "2026-10-16T10:00:00-06:00",
                EndTime = "2026-10-16T10:45:00-06:00",
                TimeZone = "America/Mexico_City"
            });

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        var result = await sut.InterpretAndScheduleAsync(new AiInterpretRequestDto
        {
            PhoneNumber = "user1",
            Text = "Hola soy Juan Perez quiero agendar una cita para el 16 de octubre del 2026 a las 10:00 am en el calendario fake-calendar@invalid."
        }, CancellationToken.None);

        Assert.NotNull(result.CalendarEventResponse);
        Assert.Equal("evt1", result.CalendarEventResponse!.EventId);
        calendar.VerifyAll();
    }

    [Fact]
    public async Task InterpretAndScheduleAsync_WhenGeminiFlashToolCallPresent_SchedulesEvent()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto
            {
                Provider = "gemini-3.6-flash",
                ToolCalls =
                [
                    new LlmToolCallDto
                    {
                        Name = "schedule_appointment",
                        ArgumentsJson = "{\"startTime\":\"2026-10-25T10:00:00-06:00\",\"summary\":\"Juan Perez\"}"
                    }
                ]
            });

        calendar.Setup(x => x.CreateEventAsync(It.Is<CreateCalendarEventRequestDto>(r =>
                r.Summary == "Juan Perez" &&
                string.IsNullOrWhiteSpace(r.TimeZone) &&
                r.StartTime.StartsWith("2026-10-25T10:00:00") &&
                r.EndTime.StartsWith("2026-10-25T10:45:00"))))
            .ReturnsAsync(new CreateCalendarEventResponseDto
            {
                EventId = "evt2",
                StartTime = "2026-10-25T10:00:00-06:00",
                EndTime = "2026-10-25T10:45:00-06:00",
                TimeZone = "America/Mexico_City"
            });

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        var result = await sut.InterpretAndScheduleAsync(new AiInterpretRequestDto
        {
            PhoneNumber = "user1",
            Text = "Hola soy Juan Perez quiero agendar una cita para el 25 de octubre del 2026 a las 10:00 am en el calendario fake-calendar@invalid."
        }, CancellationToken.None);

        Assert.NotNull(result.CalendarEventResponse);
        Assert.Equal("evt2", result.CalendarEventResponse!.EventId);
        calendar.VerifyAll();
    }

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenPayloadIsNotObject_Returns0()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        JsonElement payload = ParseJson("[]");
        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(0, processed);
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenMissingEntryArray_Returns0()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        JsonElement payload = ParseJson("{}");
        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(0, processed);
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenMessageTypeIsNotText_IgnoresMessage()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        JsonElement payload = BuildWebhookPayloadWithMessagesJson("{\"from\":\"user1\",\"type\":\"image\"}");
        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(0, processed);
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenTextObjectMissing_IgnoresMessage()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        JsonElement payload = BuildWebhookPayloadWithMessagesJson("{\"from\":\"user1\",\"type\":\"text\"}");
        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(0, processed);
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessWhatsAppWebhookAsync_WhenTextBodyIsEmptyOrWhitespace_IgnoresMessage(string body)
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        JsonElement payload = BuildWebhookPayload(body);
        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(0, processed);
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenSingleValidTextMessage_ProcessesAndReturns1()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCallResponse());

        calendar.Setup(x => x.CreateEventAsync(It.Is<CreateCalendarEventRequestDto>(r =>
                r.Summary == "Juan Perez" &&
                string.IsNullOrWhiteSpace(r.TimeZone) &&
                r.StartTime.StartsWith("2026-10-16T10:00:00") &&
                r.EndTime.StartsWith("2026-10-16T10:45:00"))))
            .ReturnsAsync(CalendarOk(
                eventId: "evt_webhook_1",
                startTime: "2026-10-16T10:00:00-06:00",
                endTime: "2026-10-16T10:45:00-06:00"));

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        JsonElement payload = BuildWebhookPayload("Hola quiero una cita para el 16 de octubre del 2026 a las 10:00 am en el calendario fake-calendar@invalid");
        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(1, processed);
        llm.Verify(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
        calendar.Verify(x => x.CreateEventAsync(It.IsAny<CreateCalendarEventRequestDto>()), Times.Once);
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenMultipleValidTextMessages_ProcessesAllAndReturnsCount()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.SetupSequence(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCallResponse(startTime: "2026-10-16T10:00:00-06:00"))
            .ReturnsAsync(ToolCallResponse(startTime: "2026-10-16T11:00:00-06:00"));

        calendar.SetupSequence(x => x.CreateEventAsync(It.IsAny<CreateCalendarEventRequestDto>()))
            .ReturnsAsync(CalendarOk(
                eventId: "evt_webhook_2a",
                startTime: "2026-10-16T10:00:00-06:00",
                endTime: "2026-10-16T10:45:00-06:00"))
            .ReturnsAsync(CalendarOk(
                eventId: "evt_webhook_2b",
                startTime: "2026-10-16T11:00:00-06:00",
                endTime: "2026-10-16T11:45:00-06:00"));

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        JsonElement payload = BuildWebhookPayload(
            "Mensaje 1",
            "Mensaje 2");

        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(2, processed);
        llm.Verify(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        calendar.Verify(x => x.CreateEventAsync(It.IsAny<CreateCalendarEventRequestDto>()), Times.Exactly(2));
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenOneMessageThrows_ContinuesAndReturnsOnlySuccessfulCount()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.SetupSequence(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"))
            .ReturnsAsync(ToolCallResponse(startTime: "2026-10-16T10:00:00-06:00"));

        calendar.Setup(x => x.CreateEventAsync(It.Is<CreateCalendarEventRequestDto>(r =>
                string.IsNullOrWhiteSpace(r.TimeZone))))
            .ReturnsAsync(CalendarOk(
                eventId: "evt_webhook_3",
                startTime: "2026-10-16T10:00:00-06:00",
                endTime: "2026-10-16T10:45:00-06:00"));

        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, new AlwaysValidWebhookSignatureValidator(), logger);

        JsonElement payload = BuildWebhookPayload(
            "Mensaje que truena",
            "Mensaje que sí agenda");

        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(1, processed);
        llm.Verify(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        calendar.Verify(x => x.CreateEventAsync(It.IsAny<CreateCalendarEventRequestDto>()), Times.Once);
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
    }
}
