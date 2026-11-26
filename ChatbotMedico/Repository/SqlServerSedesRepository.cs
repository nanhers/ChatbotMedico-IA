using System.Data;
using ChatbotMedico.DTOs.Sedes;
using ChatbotMedico.Repository.Interfaces;
using Microsoft.Data.SqlClient;

namespace ChatbotMedico.Repository;

public sealed class SqlServerSedesRepository : ISedesRepository
{
    private const string SedesTable = "dbo.Sedes";

    private readonly string _connectionString;
    private readonly ILogger<SqlServerSedesRepository> _logger;

    public SqlServerSedesRepository(IConfiguration configuration, ILogger<SqlServerSedesRepository> logger)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
                            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection");
    }

    public async Task<SedeDto?> GetSingleAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            string sql = $@"
SELECT TOP (1)
    Name,
    Address,
    GoogleMapsUrl
FROM {SedesTable}
ORDER BY IsPrimary DESC, Id ASC;";

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);

            if (!await reader.ReadAsync(ct))
                return null;

            string name = reader.IsDBNull(reader.GetOrdinal("Name"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("Name"));

            string address = reader.IsDBNull(reader.GetOrdinal("Address"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("Address"));

            string? googleMapsUrl = reader.IsDBNull(reader.GetOrdinal("GoogleMapsUrl"))
                ? null
                : reader.GetString(reader.GetOrdinal("GoogleMapsUrl"));

            return new SedeDto
            {
                Name = name ?? string.Empty,
                Address = address ?? string.Empty,
                GoogleMapsUrl = googleMapsUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQL: no se pudo leer sedes");
            throw;
        }
    }
}
