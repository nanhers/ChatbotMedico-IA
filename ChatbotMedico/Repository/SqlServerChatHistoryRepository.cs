using System.Data;
using ChatbotMedico.DTOs.Messaging;
using ChatbotMedico.Repository.Interfaces;
using Microsoft.Data.SqlClient;

namespace ChatbotMedico.Repository;

public sealed class SqlServerChatHistoryRepository : IChatHistoryRepository
{
    private const string ConversationsTable = "dbo.ChatConversations";
    private const string MessagesTable = "dbo.ChatMessages";

    private readonly string _connectionString;
    private readonly ILogger<SqlServerChatHistoryRepository> _logger;

    public SqlServerChatHistoryRepository(IConfiguration configuration, ILogger<SqlServerChatHistoryRepository> logger)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("ChatHistorySql")
                            ?? throw new InvalidOperationException("Falta ConnectionStrings:ChatHistorySql");
    }

    public async Task<ChatHistoryConversationDto?> GetAsync(string phoneDigits, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            const string conversationSql = $@"
SELECT PhoneDigits, PhoneNumber
FROM {ConversationsTable}
WHERE PhoneDigits = @PhoneDigits;";

            await using (var cmd = new SqlCommand(conversationSql, conn))
            {
                cmd.Parameters.Add(new SqlParameter("@PhoneDigits", SqlDbType.VarChar, 32) { Value = phoneDigits });

                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
                if (!await reader.ReadAsync(ct))
                    return null;

                string phoneNumber = reader.GetString(reader.GetOrdinal("PhoneNumber"));

                // Cerrar reader para poder ejecutar segunda consulta en la misma conexión.
                await reader.CloseAsync();

                List<ChatHistoryMessageDto> messages = await ReadMessagesAsync(conn, phoneDigits, ct);

                return new ChatHistoryConversationDto
                {
                    PhoneNumber = phoneNumber,
                    Messages = messages
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQL: no se pudo leer historial. phone={Phone}", phoneDigits);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string phoneDigits, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            const string sql = $@"
SELECT 1
FROM {ConversationsTable}
WHERE PhoneDigits = @PhoneDigits;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@PhoneDigits", SqlDbType.VarChar, 32) { Value = phoneDigits });

            object? result = await cmd.ExecuteScalarAsync(ct);
            return result is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQL: no se pudo verificar existencia. phone={Phone}", phoneDigits);
            throw;
        }
    }

    public async Task CreateAsync(ChatHistoryConversationDto conversation, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // 1) Insert conversación (falla si ya existe)
            const string insertConversationSql = $@"
INSERT INTO {ConversationsTable} (PhoneDigits, PhoneNumber, UpdatedAtUtc)
VALUES (@PhoneDigits, @PhoneNumber, SYSUTCDATETIME());";

            await using (var cmd = new SqlCommand(insertConversationSql, conn))
            {
                cmd.Parameters.Add(new SqlParameter("@PhoneDigits", SqlDbType.VarChar, 32) { Value = conversation.PhoneNumber });
                cmd.Parameters.Add(new SqlParameter("@PhoneNumber", SqlDbType.VarChar, 32) { Value = conversation.PhoneNumber });

                try
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException ex) when (ex.Number is 2627 or 2601)
                {
                    throw new InvalidOperationException($"La conversación ya existe. phone={conversation.PhoneNumber}", ex);
                }
            }

            // 2) Insert mensajes
            await ReplaceMessagesAsync(conn, conversation.PhoneNumber, conversation.Messages, ct);
        }
        catch
        {
            throw;
        }
    }

    public async Task UpdateAsync(ChatHistoryConversationDto conversation, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            const string updateConversationSql = $@"
UPDATE {ConversationsTable}
SET PhoneNumber = @PhoneNumber,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE PhoneDigits = @PhoneDigits;";

            int affected;
            await using (var cmd = new SqlCommand(updateConversationSql, conn))
            {
                cmd.Parameters.Add(new SqlParameter("@PhoneDigits", SqlDbType.VarChar, 32) { Value = conversation.PhoneNumber });
                cmd.Parameters.Add(new SqlParameter("@PhoneNumber", SqlDbType.VarChar, 32) { Value = conversation.PhoneNumber });
                affected = await cmd.ExecuteNonQueryAsync(ct);
            }

            if (affected == 0)
                throw new InvalidOperationException($"No existe la conversación a actualizar. phone={conversation.PhoneNumber}");

            await ReplaceMessagesAsync(conn, conversation.PhoneNumber, conversation.Messages, ct);
        }
        catch
        {
            throw;
        }
    }

    public async Task DeleteAsync(string phoneDigits, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            const string sql = $@"
DELETE FROM {ConversationsTable}
WHERE PhoneDigits = @PhoneDigits;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@PhoneDigits", SqlDbType.VarChar, 32) { Value = phoneDigits });

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQL: no se pudo borrar historial. phone={Phone}", phoneDigits);
            throw;
        }
    }

    private static async Task<List<ChatHistoryMessageDto>> ReadMessagesAsync(SqlConnection conn, string phoneDigits, CancellationToken ct)
    {
        const string sql = $@"
SELECT Seq, Role, Content, CreatedAtUtc
FROM {MessagesTable}
WHERE PhoneDigits = @PhoneDigits
ORDER BY Seq;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@PhoneDigits", SqlDbType.VarChar, 32) { Value = phoneDigits });

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var messages = new List<ChatHistoryMessageDto>();
        while (await reader.ReadAsync(ct))
        {
            var msg = new ChatHistoryMessageDto
            {
                Role = reader.GetString(reader.GetOrdinal("Role")),
                Text = reader.GetString(reader.GetOrdinal("Content")),
                Timestamp = Google.Cloud.Firestore.Timestamp.FromDateTime(reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")).ToUniversalTime())
            };
            messages.Add(msg);
        }

        return messages;
    }

    private static async Task ReplaceMessagesAsync(SqlConnection conn, string phoneDigits, List<ChatHistoryMessageDto>? messages, CancellationToken ct)
    {
        // 1) borrar
        const string deleteSql = $@"
DELETE FROM {MessagesTable}
WHERE PhoneDigits = @PhoneDigits;";

        await using (var deleteCmd = new SqlCommand(deleteSql, conn))
        {
            deleteCmd.Parameters.Add(new SqlParameter("@PhoneDigits", SqlDbType.VarChar, 32) { Value = phoneDigits });
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        // 2) insertar
        if (messages is null || messages.Count == 0)
            return;

        const string insertSql = $@"
INSERT INTO {MessagesTable} (PhoneDigits, Seq, Role, Content)
VALUES (@PhoneDigits, @Seq, @Role, @Content);";

        for (int i = 0; i < messages.Count; i++)
        {
            ChatHistoryMessageDto m = messages[i];

            await using var insertCmd = new SqlCommand(insertSql, conn);
            insertCmd.Parameters.Add(new SqlParameter("@PhoneDigits", SqlDbType.VarChar, 32) { Value = phoneDigits });
            insertCmd.Parameters.Add(new SqlParameter("@Seq", SqlDbType.Int) { Value = i });
            insertCmd.Parameters.Add(new SqlParameter("@Role", SqlDbType.NVarChar, 32) { Value = (object?)m.Role ?? string.Empty });
            insertCmd.Parameters.Add(new SqlParameter("@Content", SqlDbType.NVarChar) { Value = (object?)m.Text ?? string.Empty });

            await insertCmd.ExecuteNonQueryAsync(ct);
        }
    }
}
