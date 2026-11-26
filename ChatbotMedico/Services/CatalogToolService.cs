using System.Globalization;
using System.Text;
using ChatbotMedico.DTOs.AI;
using ChatbotMedico.DTOs.Sedes;
using ChatbotMedico.Repository.Interfaces;
using ChatbotMedico.Services.Interfaces;

namespace ChatbotMedico.Services;

public sealed class CatalogToolService : ICatalogToolService
{
    private readonly ISedesRepository _sedesRepository;
    private readonly ILogger<CatalogToolService> _logger;

    public CatalogToolService(ISedesRepository sedesRepository, ILogger<CatalogToolService> logger)
    {
        _sedesRepository = sedesRepository;
        _logger = logger;
    }

    public async Task<AiToolActionResultDto> ConsultCatalogAsync(string catalogName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(catalogName))
        {
            return BuildGenericErrorReply();
        }

        try
        {
            if (string.Equals(catalogName.Trim(), "sedes", StringComparison.OrdinalIgnoreCase))
            {
                SedeDto? sede = await _sedesRepository.GetSingleAsync(ct);
                if (sede is null)
                {
                    _logger.LogWarning("Error en GetSingleAsync().");
                    return BuildGenericErrorReply();
                }

                string message = BuildSedesReplyMessage(sede);
                if (string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogWarning("Error al construir un mensaje");
                    return BuildGenericErrorReply();
                }

                return new AiToolActionResultDto
                {
                    ToolName = "reply_message",
                    ReplyMessage = message,
                    ArgumentsJson = string.Empty
                };
            }

            // MVP: cualquier catálogo no soportado devuelve mensaje genérico.
            return BuildGenericErrorReply();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando catálogo. catalogName={CatalogName}", catalogName);
            return BuildGenericErrorReply();
        }
    }

    private static AiToolActionResultDto BuildGenericErrorReply()
        => new AiToolActionResultDto
        {
            ToolName = "reply_message",
            ReplyMessage = "Por el momento no tengo la información de ubicación disponible, intenta más tarde.",
            ArgumentsJson = string.Empty
        };

    private static string BuildSedesReplyMessage(SedeDto sede)
    {
        string name = sede.Name?.Trim() ?? string.Empty;
        string address = sede.Address?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        string mapsUrl = BuildGoogleMapsUrl(sede);

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(name))
        {
            sb.AppendLine($"📍 Ubicación: {name}");
        }

        if (!string.IsNullOrWhiteSpace(address))
        {
            sb.AppendLine($"🏥 Dirección: {address}");
        }

        if (!string.IsNullOrWhiteSpace(mapsUrl))
        {
            sb.AppendLine($"🗺️ Google Maps: {mapsUrl}");
        }

        return sb.ToString().Trim();
    }

    private static string BuildGoogleMapsUrl(SedeDto sede)
    {
        string? direct = sede.GoogleMapsUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        string address = sede.Address?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        // URL encode conservador
        string encoded = Uri.EscapeDataString(address);
        return $"https://www.google.com/maps/search/?api=1&query={encoded}";
    }
}
