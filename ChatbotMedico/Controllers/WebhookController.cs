using Microsoft.AspNetCore.Mvc;

namespace ChatbotMedico.Controllers
{
    [ApiController]
    [Route("webhook")]
    public class WebhookController : ControllerBase
    {
        private const string VerifyToken = "MiClaveSuperSecretaConsultorio2026";

        [HttpGet]
        public IActionResult Get(
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? token,
            [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            Console.WriteLine($"[WEBHOOK GET] Mode: {mode} | Token: {token} | Challenge: {challenge}");

            if (mode == "subscribe" && token == VerifyToken)
            {
                // Retornamos el challenge asegurando texto plano estricto
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
        public IActionResult Post([FromBody] object data)
        {
            Console.WriteLine($"[WEBHOOK POST] Payload: {data}");
            return Ok();
        }
    }
}