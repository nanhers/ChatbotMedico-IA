using System.Text.Json;

namespace ChatbotMedico.Services.Interfaces;

public interface ITaskDispatcher
{
    Task DispatchAsync(string taskName, JsonDocument payload, CancellationToken ct);
}
