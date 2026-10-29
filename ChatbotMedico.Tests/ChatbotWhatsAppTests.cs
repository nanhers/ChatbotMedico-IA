using System.Text.Json;
using ChatbotMedico.Application;
using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.DTOs.LLM;
using ChatbotMedico.Services.Interfaces;
using ChatbotMedico.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class ChatbotWhatsAppTests
{
    private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement BuildWebhookPayload(string from, string body)
    {
        string json =
            $"{{\"entry\":[{{\"changes\":[{{\"value\":{{\"messages\":[{{\"from\":{JsonSerializer.Serialize(from)},\"type\":\"text\",\"text\":{{\"body\":{JsonSerializer.Serialize(body)} }} }}]}}}}]}}]}}";

        return ParseJson(json);
    }

    private static LlmResponseDto ToolCallResponse(
        string summary = "Juan Perez",
        string startTime = "2026-10-16T10:00:00-06:00")
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

    private static CreateCalendarEventResponseDto CalendarOk(
        string eventId,
        string startTime,
        string endTime)
        => new CreateCalendarEventResponseDto
        {
            EventId = eventId,
            StartTime = startTime,
            EndTime = endTime,
            TimeZone = "America/Mexico_City"
        };

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenSchedulesEvent_SendsGenericConfirmationMessage()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCallResponse());

        calendar.Setup(x => x.CreateEventAsync(It.IsAny<CreateCalendarEventRequestDto>()))
            .ReturnsAsync(CalendarOk(
                eventId: "evt_whatsapp_ok",
                startTime: "2026-10-16T10:00:00-06:00",
                endTime: "2026-10-16T10:45:00-06:00"));

        messageClient
            .Setup(x => x.SendTextAsync("5215550000001", "Su cita quedó agendada.", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var signatureValidator = new Mock<ChatbotMedico.Services.Interfaces.IWebhookSignatureValidator>(MockBehavior.Strict);
        signatureValidator.Setup(x => x.IsValid(It.IsAny<byte[]>(), It.IsAny<string?>())).Returns(true);

        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, signatureValidator.Object, logger);

        JsonElement payload = BuildWebhookPayload(
            from: "5215550000001",
            body: "Hola quiero una cita para el 16 de octubre del 2026 a las 10:00 am en el calendario fake-calendar@invalid");

        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(1, processed);

        messageClient.Verify(
            x => x.SendTextAsync("5215550000001", "Su cita quedó agendada.", It.IsAny<CancellationToken>()),
            Times.Once);

        messageClient.Verify(
            x => x.SendTextAsync("5215550000001", "Hubo un error al agendar su cita.", It.IsAny<CancellationToken>()),
            Times.Never);

        llm.Verify(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
        calendar.Verify(x => x.CreateEventAsync(It.IsAny<CreateCalendarEventRequestDto>()), Times.Once);
        llm.VerifyNoOtherCalls();
        calendar.VerifyNoOtherCalls();
        messageClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessWhatsAppWebhookAsync_WhenDoesNotSchedule_SendsFallbackMessage()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        // No tool-call => InterpretAndScheduleAsync regresa fallback (no excepción)
        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto { Provider = "fake" });

        messageClient
            .Setup(x => x.SendTextAsync("5215550000002", "No pude interpretar tu mensaje para agendar cita.", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var signatureValidator = new Mock<ChatbotMedico.Services.Interfaces.IWebhookSignatureValidator>(MockBehavior.Strict);
        signatureValidator.Setup(x => x.IsValid(It.IsAny<byte[]>(), It.IsAny<string?>())).Returns(true);

        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, signatureValidator.Object, logger);

        JsonElement payload = BuildWebhookPayload(
            from: "5215550000002",
            body: "Hola");

        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(1, processed);

        messageClient.Verify(
            x => x.SendTextAsync("5215550000002", "No pude interpretar tu mensaje para agendar cita.", It.IsAny<CancellationToken>()),
            Times.Once);

        messageClient.Verify(
            x => x.SendTextAsync("5215550000002", "Su cita quedó agendada.", It.IsAny<CancellationToken>()),
            Times.Never);

        messageClient.Verify(
            x => x.SendTextAsync("5215550000002", "Hubo un error al agendar su cita.", It.IsAny<CancellationToken>()),
            Times.Never);

        llm.Verify(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
        calendar.VerifyNoOtherCalls();
        llm.VerifyNoOtherCalls();
        messageClient.VerifyNoOtherCalls();
    }
}
