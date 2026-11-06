using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChatbotMedico.Services.Interfaces;

namespace ChatbotMedico.Services;

public sealed class MockTaskDispatcher : ITaskDispatcher
{
    public sealed record DispatchedTask(
        string TaskName,
        string PayloadJson,
        DateTimeOffset DispatchedAtUtc,
        string TargetUrl);

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MockTaskDispatcher> _logger;

    private readonly ConcurrentQueue<DispatchedTask> _dispatched = new();

    public MockTaskDispatcher(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<MockTaskDispatcher> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IReadOnlyCollection<DispatchedTask> DispatchedTasks => _dispatched.ToArray();

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

        string bodyJson = payload.RootElement.GetRawText();

        _dispatched.Enqueue(new DispatchedTask(
            TaskName: taskName,
            PayloadJson: bodyJson,
            DispatchedAtUtc: DateTimeOffset.UtcNow,
            TargetUrl: targetUrl));

        var client = _httpClientFactory.CreateClient(nameof(MockTaskDispatcher));

        using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MockTaskDispatcher] Error enviando task. taskName={TaskName} url={Url}", taskName, targetUrl);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            string responseBody = string.Empty;
            try
            {
                responseBody = await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                // ignore
            }

            _logger.LogError(
                "[MockTaskDispatcher] Task falló. taskName={TaskName} url={Url} status={Status} body={Body}",
                taskName,
                targetUrl,
                (int)response.StatusCode,
                responseBody);

            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation(
            "[MockTaskDispatcher] Task ejecutada (local). taskName={TaskName} url={Url} status={Status}",
            taskName,
            targetUrl,
            (int)response.StatusCode);
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
