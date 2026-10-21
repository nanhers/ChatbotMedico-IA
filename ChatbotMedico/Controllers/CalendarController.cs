using ChatbotMedico.DTOs.Calendar;
using ChatbotMedico.Services;
using ChatbotMedico.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotMedico.Controllers;

[ApiController]
[Route("calendar/events")]
public sealed class CalendarController : ControllerBase
{
    private readonly ICalendarService _calendarService;
    private readonly ILogger<CalendarController> _logger;

    public CalendarController(ICalendarService calendarService, ILogger<CalendarController> logger)
    {
        _calendarService = calendarService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CreateCalendarEventResponseDto>> Create([FromBody] CreateCalendarEventRequestDto request)
    {
        try
        {
            var created = await _calendarService.CreateEventAsync(request);

            // Nota: GetById existe para soportar CreatedAtAction.
            return CreatedAtAction(
                nameof(GetById),
                new { id = created.EventId, calendarId = created.CalendarId },
                created);
        }
        catch (CalendarService.ValidationProblemException ex)
        {
            var details = new ValidationProblemDetails(ex.Errors)
            {
                Title = "Datos inválidos",
                Status = StatusCodes.Status400BadRequest
            };
            return BadRequest(details);
        }
        catch (CalendarService.ScheduleConflictException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflicto de agenda",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al crear evento.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Dependencia externa no disponible",
                Detail = "No fue posible crear el evento en este momento.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GetCalendarEventResponseDto>> GetById(
        [FromRoute] string id,
        [FromQuery] string calendarId)
    {
        try
        {
            var ev = await _calendarService.GetByIdAsync(calendarId, id);
            if (ev is null) return NotFound();
            return Ok(ev);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al obtener evento.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Dependencia externa no disponible",
                Detail = "No fue posible obtener el evento en este momento.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
    }
}
