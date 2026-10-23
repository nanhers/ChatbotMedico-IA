using ChatbotMedico.Client.Interfaces;

namespace ChatbotMedico.Client;

public sealed class MockMessageClient : IMessageClient
{
    private readonly ILogger<MockMessageClient> _logger;

    public MockMessageClient(ILogger<MockMessageClient> logger)
    {
        _logger = logger;
    }

    public Task SendTextAsync(string to, string message, CancellationToken ct)
    {
        _logger.LogInformation("[MOCK WHATSAPP] to={To} message={Message}", to, message);
        return Task.CompletedTask;
    }
}
