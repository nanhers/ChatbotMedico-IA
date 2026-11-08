using System.Text;
using System.Text.Json;
using ChatbotMedico.Client.GoogleCloudTask.Interfaces;
using Google.Cloud.Tasks.V2;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Task = System.Threading.Tasks.Task;

namespace ChatbotMedico.Client.GoogleCloudTask;

public sealed class GoogleCloudTasksDispatcherClient : ITaskDispatcherClient
{
    private readonly CloudTasksClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleCloudTasksDispatcherClient> _logger;

    public GoogleCloudTasksDispatcherClient(
        CloudTasksClient client,
        IConfiguration configuration,
        ILogger<GoogleCloudTasksDispatcherClient> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task DispatchAsync(string taskName, JsonDocument payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(taskName))
            throw new ArgumentException("taskName es obligatorio", nameof(taskName));

        string baseUrl = _configuration["TaskDispatcher:BaseUrl"] ?? string.Empty;
        string? route = _configuration[$"TaskDispatcher:Routes:{taskName}"];

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Falta configuración TaskDispatcher:BaseUrl");

        if (string.IsNullOrWhiteSpace(route))
            throw new InvalidOperationException($"No hay ruta configurada para taskName={taskName}. Key=TaskDispatcher:Routes:{taskName}");

        string targetUrl = CombineUrl(baseUrl, route);

        string projectId = _configuration["TaskDispatcher:ProjectId"] ?? string.Empty;
        string locationId = _configuration["TaskDispatcher:LocationId"] ?? string.Empty;
        string queueId = _configuration["TaskDispatcher:QueueId"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(locationId) || string.IsNullOrWhiteSpace(queueId))
            throw new InvalidOperationException("Falta configuración TaskDispatcher:ProjectId/LocationId/QueueId");

        string serviceAccountEmail = _configuration["TaskDispatcher:Oidc:ServiceAccountEmail"] ?? string.Empty;
        string audience = _configuration["TaskDispatcher:Oidc:Audience"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(serviceAccountEmail) || string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Falta configuración TaskDispatcher:Oidc:ServiceAccountEmail/Audience");

        QueueName parent = new QueueName(projectId, locationId, queueId);

        string bodyJson = payload.RootElement.GetRawText();

        var httpRequest = new Google.Cloud.Tasks.V2.HttpRequest
        {
            HttpMethod = Google.Cloud.Tasks.V2.HttpMethod.Post,
            Url = targetUrl,
            Headers = { { "Content-Type", "application/json" } },
            Body = ByteString.CopyFrom(bodyJson, Encoding.UTF8),
            OidcToken = new OidcToken
            {
                ServiceAccountEmail = serviceAccountEmail,
                Audience = audience
            }
        };

        var task = new Google.Cloud.Tasks.V2.Task
        {
            HttpRequest = httpRequest
        };

        Google.Cloud.Tasks.V2.Task created = await _client.CreateTaskAsync(parent, task, cancellationToken: ct);

        _logger.LogInformation(
            "CloudTask creada. taskName={TaskName} url={Url} taskId={TaskId}",
            taskName,
            targetUrl,
            created.Name);
    }

    private static string CombineUrl(string baseUrl, string route)
    {
        baseUrl = baseUrl.Trim();
        route = route.Trim();

        if (string.IsNullOrWhiteSpace(route)) return baseUrl;

        bool baseEndsWithSlash = baseUrl.EndsWith('/');
        bool routeStartsWithSlash = route.StartsWith('/');

        if (baseEndsWithSlash && routeStartsWithSlash)
            return baseUrl + route[1..];

        if (!baseEndsWithSlash && !routeStartsWithSlash)
            return baseUrl + "/" + route;

        return baseUrl + route;
    }
}
