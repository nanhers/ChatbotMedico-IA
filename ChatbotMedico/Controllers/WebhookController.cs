using System.IO;
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
        private readonly IWebhookGatewayService _webhookGatewayService;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(
            IConfiguration configuration,
            IWebhookGatewayService webhookGatewayService,
            ILogger<WebhookController> logger)
        {
            _configuration = configuration;
            _webhookGatewayService = webhookGatewayService;
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

            WebhookGatewayResult result = await _webhookGatewayService.HandleAsync(rawBody, signatureHeader, ct);

            return result == WebhookGatewayResult.Unauthorized
                ? Unauthorized()
                : Ok();
        }
    }
}
