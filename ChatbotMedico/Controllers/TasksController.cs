using System.Text.Json;
using ChatbotMedico.Application;
using ChatbotMedico.DTOs.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotMedico.Controllers;

[ApiController]
[Route("tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly ChatBot _chatBot;
    private readonly ILogger<TasksController> _logger;

    public TasksController(ChatBot chatBot, ILogger<TasksController> logger)
    {
        _chatBot = chatBot;
        _logger = logger;
    }

    [HttpPost("process-webhook")]
    public async Task<IActionResult> ProcessWebhook([FromBody] ProcessWebhookTaskRequestDto request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest();

        string correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;

        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId
        });

        byte[] rawBody;
        try
        {
            rawBody = Convert.FromBase64String(request.RawBodyBase64 ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Task payload inválido: RawBodyBase64 no es Base64.");
            return BadRequest();
        }

        try
        {
           
            bool ok = await _chatBot.ProcessWhatsAppWebhookAsync(rawBody, request.SignatureHeader, ct);

            if (!ok)
            {
                // Firma inválida: para Cloud Tasks esto es un fallo lógico.
                // Respondemos 200 para evitar retries infinitos, pero queda log.
                _logger.LogWarning("Task procesada pero firma inválida según ChatBot. Se responde 200 para no reintentar.");
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando webhook en worker.");
            // 500 => Cloud Tasks reintenta según policy.
            return StatusCode(500);
        }
    }
}
