namespace ChatbotMedico.DTOs.Sedes;

public sealed class SedeDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? GoogleMapsUrl { get; set; }
}
