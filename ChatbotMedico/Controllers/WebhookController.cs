using System.Text.Json;
using ChatbotMedico.Application;
using ChatbotMedico.DTOs.AI;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotMedico.Controllers
{
    [ApiController]
    [Route("webhook")]
    public class WebhookController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ChatBot _chatBot;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(IConfiguration configuration, ChatBot chatBot, ILogger<WebhookController> logger)
        {
            _configuration = configuration;
            _chatBot = chatBot;
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
        public async Task<IActionResult> Post([FromBody] JsonElement payload, CancellationToken ct)
        {
            try
            {
                await _chatBot.ProcessWhatsAppWebhookAsync(payload, ct);
            }
            catch (Exception ex)
            {
                // Meta requiere 200; loggeamos y no re-lanzamos.
                _logger.LogError(ex, "[WEBHOOK POST] Error delegando el procesamiento del webhook.");
                return Ok();
            }

            return Ok();
        }
    }
}