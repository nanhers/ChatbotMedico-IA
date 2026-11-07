using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatbotMedico.Services;
using ChatbotMedico.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ChatbotMedico.Tests;

public sealed class WebhookGatewayServiceTests
{
    [Fact]
    public async Task HandleAsync_WhenSignatureInvalid_ReturnsUnauthorized_AndDoesNotDispatch()
    {
        var signatureValidator = new Mock<IWebhookSignatureValidator>(MockBehavior.Strict);
        signatureValidator
            .Setup(v => v.IsValid(It.IsAny<byte[]>(), It.IsAny<string?>()))
            .Returns(false);

        var dispatcher = new Mock<ITaskDispatcher>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<WebhookGatewayService>>();

        var sut = new WebhookGatewayService(signatureValidator.Object, dispatcher.Object, logger);

        WebhookGatewayResult result = await sut.HandleAsync(Encoding.UTF8.GetBytes("{}"), "sha256=deadbeef", CancellationToken.None);

        Assert.Equal(WebhookGatewayResult.Unauthorized, result);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<JsonDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenSignatureValid_DispatchesProcessWebhook_AndReturnsOk()
    {
        var signatureValidator = new Mock<IWebhookSignatureValidator>(MockBehavior.Strict);
        signatureValidator
            .Setup(v => v.IsValid(It.IsAny<byte[]>(), It.IsAny<string?>()))
            .Returns(true);

        var dispatcher = new Mock<ITaskDispatcher>(MockBehavior.Strict);
        JsonDocument? capturedPayload = null;

        dispatcher
            .Setup(d => d.DispatchAsync("process-webhook", It.IsAny<JsonDocument>(), It.IsAny<CancellationToken>()))
            .Callback<string, JsonDocument, CancellationToken>((_, payload, __) => capturedPayload = payload)
            .Returns(Task.CompletedTask);

        var logger = Mock.Of<ILogger<WebhookGatewayService>>();
        var sut = new WebhookGatewayService(signatureValidator.Object, dispatcher.Object, logger);

        byte[] rawBody = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        string signatureHeader = "sha256=abc";

        WebhookGatewayResult result = await sut.HandleAsync(rawBody, signatureHeader, CancellationToken.None);

        Assert.Equal(WebhookGatewayResult.Ok, result);
        dispatcher.Verify(d => d.DispatchAsync("process-webhook", It.IsAny<JsonDocument>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(capturedPayload);
        JsonElement root = capturedPayload!.RootElement;

        Assert.True(root.TryGetProperty("RawBodyBase64", out JsonElement rawBodyBase64El));
        Assert.True(root.TryGetProperty("SignatureHeader", out JsonElement signatureHeaderEl));
        Assert.True(root.TryGetProperty("CorrelationId", out JsonElement correlationIdEl));

        Assert.Equal(Convert.ToBase64String(rawBody), rawBodyBase64El.GetString());
        Assert.Equal(signatureHeader, signatureHeaderEl.GetString());
        Assert.False(string.IsNullOrWhiteSpace(correlationIdEl.GetString()));
    }

    [Fact]
    public async Task HandleAsync_WhenSignatureValid_AndDispatchThrows_ReturnsOk()
    {
        var signatureValidator = new Mock<IWebhookSignatureValidator>(MockBehavior.Strict);
        signatureValidator
            .Setup(v => v.IsValid(It.IsAny<byte[]>(), It.IsAny<string?>()))
            .Returns(true);

        var dispatcher = new Mock<ITaskDispatcher>(MockBehavior.Strict);
        dispatcher
            .Setup(d => d.DispatchAsync("process-webhook", It.IsAny<JsonDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var logger = Mock.Of<ILogger<WebhookGatewayService>>();
        var sut = new WebhookGatewayService(signatureValidator.Object, dispatcher.Object, logger);

        WebhookGatewayResult result = await sut.HandleAsync(Encoding.UTF8.GetBytes("{}"), "sha256=abc", CancellationToken.None);

        Assert.Equal(WebhookGatewayResult.Ok, result);
        dispatcher.Verify(d => d.DispatchAsync("process-webhook", It.IsAny<JsonDocument>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
