using ChatbotMedico.Application;
using ChatbotMedico.DTOs.AI;
using ChatbotMedico.DTOs.Calendar;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotMedico.Controllers;

[ApiController]
[Route("ai")]
public sealed class AiController : ControllerBase
{
    private readonly ChatBot _chatBot;
    private readonly ILogger<AiController> _logger;

    public AiController(ChatBot chatBot, ILogger<AiController> logger)
    {
        _chatBot = chatBot;
        _logger = logger;
    }

    [HttpPost("interpret")]
    public async Task<ActionResult<CreateCalendarEventResponseDto>> Interpret([FromBody] AiInterpretRequestDto request, CancellationToken ct)
    {
        var traceId = HttpContext.TraceIdentifier;
        var textLength = request.Text?.Length ?? 0;
        _logger.LogInformation("AI Interpret: request recibido traceId={TraceId} textLength={TextLength}", traceId, textLength);

        try
        {
            var result = await _chatBot.InterpretAndScheduleAsync(request, ct);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error en /ai/interpret");
            return BadRequest(new ProblemDetails
            {
                Title = "Error",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}
