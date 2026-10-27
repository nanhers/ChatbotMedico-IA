using System.Text.Json;
using ChatbotMedico.Application;
using ChatbotMedico.Client.Interfaces;
using ChatbotMedico.DTOs.AI;
using ChatbotMedico.DTOs.LLM;
using ChatbotMedico.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class ChatbotReplyMessageToolTests
{
    private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement BuildWebhookPayload(string from, string body)
    {
        string json = $"{{\"entry\":[{{\"changes\":[{{\"value\":{{\"messages\":[{{\"from\":{JsonSerializer.Serialize(from)},\"type\":\"text\",\"text\":{{\"body\":{JsonSerializer.Serialize(body)} }} }}]}}}}]}}]}}";
        return ParseJson(json);
    }

    private static LlmResponseDto ReplyToolCallResponse(string message)
        => new LlmResponseDto
        {
            Provider = "fake",
            ToolCalls =
            [
                new LlmToolCallDto
                {
                    Name = "reply_message",
                    ArgumentsJson = $"{{\"message\":{JsonSerializer.Serialize(message)} }}"
                }
            ]
        };

    [Fact]
    public async Task Chatbot_ReplyMessageTool_WhenLlmReturnsReplyMessage_ReturnsReplyActionWithMessage()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplyToolCallResponse("¿Para qué fecha y hora desea la cita?"));

        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, logger);

        AiToolActionResultDto result = await sut.InterpretAndScheduleAsync(
            new ChatbotMedico.DTOs.AI.AiInterpretRequestDto { Text = "Quiero una cita" },
            CancellationToken.None);

        Assert.Equal("reply_message", result.ToolName);
        Assert.Equal("¿Para qué fecha y hora desea la cita?", result.ReplyMessage);
        Assert.Null(result.CalendarEventResponse);

        llm.VerifyAll();
        calendar.VerifyNoOtherCalls();
        messageClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chatbot_ReplyMessageTool_WhenLlmReturnsNoToolCalls_ReturnsFallbackReplyMessage()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto { Provider = "fake", ToolCalls = [] });

        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, logger);

        AiToolActionResultDto result = await sut.InterpretAndScheduleAsync(
            new ChatbotMedico.DTOs.AI.AiInterpretRequestDto { Text = "Hola" },
            CancellationToken.None);

        Assert.Equal("No pude interpretar tu mensaje para agendar cita.", result.ReplyMessage);

        llm.VerifyAll();
        calendar.VerifyNoOtherCalls();
        messageClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chatbot_ReplyMessageTool_WhenLlmReturnsUnknownTool_ReturnsFallbackReplyMessage()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto
            {
                Provider = "fake",
                ToolCalls =
                [
                    new LlmToolCallDto { Name = "unknown_tool", ArgumentsJson = "{}" }
                ]
            });

        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, logger);

        AiToolActionResultDto result = await sut.InterpretAndScheduleAsync(
            new ChatbotMedico.DTOs.AI.AiInterpretRequestDto { Text = "Hola" },
            CancellationToken.None);

        Assert.Equal("unknown_tool", result.ToolName);
        Assert.Equal("No pude interpretar tu mensaje para agendar cita.", result.ReplyMessage);

        llm.VerifyAll();
        calendar.VerifyNoOtherCalls();
        messageClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chatbot_ReplyMessageTool_WhenLlmReturnsMultipleToolCalls_TakesFirst()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto
            {
                Provider = "fake",
                ToolCalls =
                [
                    new LlmToolCallDto { Name = "reply_message", ArgumentsJson = "{\"message\":\"primero\"}" },
                    new LlmToolCallDto { Name = "schedule_appointment", ArgumentsJson = "{\"calendarId\":\"x\",\"summary\":\"y\",\"startTime\":\"2026-10-16T10:00:00-06:00\"}" }
                ]
            });

        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, logger);

        AiToolActionResultDto result = await sut.InterpretAndScheduleAsync(
            new ChatbotMedico.DTOs.AI.AiInterpretRequestDto { Text = "Hola" },
            CancellationToken.None);

        Assert.Equal("reply_message", result.ToolName);
        Assert.Equal("primero", result.ReplyMessage);

        llm.VerifyAll();
        calendar.VerifyNoOtherCalls();
        messageClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chatbot_ReplyMessageTool_Webhook_WhenReplyMessage_SendsReplyToUser()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplyToolCallResponse("Necesito fecha y hora"));

        messageClient.Setup(x => x.SendTextAsync("user1", "Necesito fecha y hora", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, logger);

        JsonElement payload = BuildWebhookPayload("user1", "Quiero una cita");
        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(1, processed);

        llm.VerifyAll();
        messageClient.VerifyAll();
        calendar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chatbot_ReplyMessageTool_Webhook_WhenFallback_SendsFallbackToUser()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
        var messageClient = new Mock<IMessageClient>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<ChatBot>>();

        llm.Setup(x => x.GetResponseAsync(It.IsAny<LlmRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponseDto { Provider = "fake", ToolCalls = [] });

        messageClient.Setup(x => x.SendTextAsync("user1", "No pude interpretar tu mensaje para agendar cita.", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chatHistoryService = new Mock<IChatHistoryService>(MockBehavior.Loose);
        var sut = new ChatBot(llm.Object, calendar.Object, messageClient.Object, chatHistoryService.Object, logger);

        JsonElement payload = BuildWebhookPayload("user1", "mensaje raro");
        int processed = await sut.ProcessWhatsAppWebhookAsync(payload, CancellationToken.None);

        Assert.Equal(1, processed);

        llm.VerifyAll();
        messageClient.VerifyAll();
        calendar.VerifyNoOtherCalls();
    }
}
