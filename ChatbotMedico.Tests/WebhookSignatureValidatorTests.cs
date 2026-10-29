using System.Security.Cryptography;
using System.Text;
using ChatbotMedico.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class WebhookSignatureValidatorTests
{
    private const string Secret = "test_secret";

    [Fact]
    public void IsValid_WhenSignatureMatches_ReturnsTrue()
    {
        var config = new Mock<IConfiguration>(MockBehavior.Strict);
        config.Setup(c => c["WhatsApp:AppSecret"]).Returns(Secret);

        var logger = Mock.Of<ILogger<MetaWebhookSignatureValidator>>();
        var sut = new MetaWebhookSignatureValidator(config.Object, logger);

        byte[] body = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        string header = BuildSignatureHeader(body, Secret);

        Assert.True(sut.IsValid(body, header));
    }

    [Fact]
    public void IsValid_WhenHeaderMissing_ReturnsFalse()
    {
        var config = new Mock<IConfiguration>(MockBehavior.Strict);
        config.Setup(c => c["WhatsApp:AppSecret"]).Returns(Secret);

        var logger = Mock.Of<ILogger<MetaWebhookSignatureValidator>>();
        var sut = new MetaWebhookSignatureValidator(config.Object, logger);

        byte[] body = Encoding.UTF8.GetBytes("{}");
        Assert.False(sut.IsValid(body, null));
    }

    [Fact]
    public void IsValid_WhenSecretMissing_ReturnsFalse()
    {
        var config = new Mock<IConfiguration>(MockBehavior.Strict);
        config.Setup(c => c["WhatsApp:AppSecret"]).Returns((string?)null);

        var logger = Mock.Of<ILogger<MetaWebhookSignatureValidator>>();
        var sut = new MetaWebhookSignatureValidator(config.Object, logger);

        byte[] body = Encoding.UTF8.GetBytes("{}");
        string header = BuildSignatureHeader(body, Secret);

        Assert.False(sut.IsValid(body, header));
    }

    private static string BuildSignatureHeader(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(body);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
