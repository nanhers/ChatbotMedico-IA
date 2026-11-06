using System.IO;
using System.Text.Json;
using ChatbotMedico.DTOs.Tasks;
using ChatbotMedico.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotMedico.Controllers
{
    [ApiController]
    [Route("webhook")]
    public class WebhookController : ControllerBase
    {
        private const string SignatureHeaderName = "X-Hub-Signature-256";

        private readonly IConfiguration _configuration;
        private readonly IWebhookSignatureValidator _signatureValidator;
        private readonly ITaskDispatcher _taskDispatcher;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(
            IConfiguration configuration,
            IWebhookSignatureValidator signatureValidator,
            ITaskDispatcher taskDispatcher,
            ILogger<WebhookController> logger)
        {
            _configuration = configuration;
            _signatureValidator = signatureValidator;
            _taskDispatcher = taskDispatcher;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Get(
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? token,
            [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            string? verifyToken = _configuration["WhatsApp:VerifyToken"];

            _logger.LogInformation(
                "[WEBHOOK GET] mode={Mode} tokenProvided={TokenProvided} challengeProvided={ChallengeProvided}",
                mode,
                !string.IsNullOrWhiteSpace(token),
                !string.IsNullOrWhiteSpace(challenge));

            if (mode == "subscribe" && !string.IsNullOrWhiteSpace(verifyToken) && token == verifyToken)
            {
                return new ContentResult
                {
                    Content = challenge ?? string.Empty,
                    ContentType = "text/plain",
                    StatusCode = 200
                };
            }

            return StatusCode(403);
        }

        [HttpPost]
        public async Task<IActionResult> Post(CancellationToken ct)
        {
            string? signatureHeader = Request.Headers[SignatureHeaderName].FirstOrDefault();

            byte[] rawBody;
            try
            {
                using var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms, ct);
                rawBody = ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WEBHOOK POST] Error leyendo raw body.");
                return Unauthorized();
            }

            // 1) Validar firma (401 si inválida)
            if (!_signatureValidator.IsValid(rawBody, signatureHeader))
            {
                _logger.LogWarning("[WEBHOOK POST] Firma inválida. Respondiendo 401.");
                return Unauthorized();
            }

            // 2) Encolar trabajo asíncrono y responder 200 inmediato.
            string correlationId = Guid.NewGuid().ToString("N");
            using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["correlationId"] = correlationId
            });

            try
            {
                var dto = new ProcessWebhookTaskRequestDto
                {
                    RawBodyBase64 = Convert.ToBase64String(rawBody),
                    SignatureHeader = signatureHeader,
                    CorrelationId = correlationId
                };

                JsonDocument payload = JsonSerializer.SerializeToDocument(dto);

                await _taskDispatcher.DispatchAsync("process-webhook", payload, ct);
            }
            catch (Exception ex)
            {
                // Por requerimiento: ACK 200 aunque falle el encolado. Solo log.
                _logger.LogError(ex, "[WEBHOOK POST] Error encolando task; se responderá 200 para evitar reintentos de Meta.");
            }

            return Ok();
        }
    }
}
