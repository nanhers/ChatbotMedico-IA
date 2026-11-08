using System.Text.Json;

namespace ChatbotMedico.Client.GoogleCloudTask.Interfaces;

public interface ITaskDispatcherClient
{
    Task DispatchAsync(string taskName, JsonDocument payload, CancellationToken ct);
}
