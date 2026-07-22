using CodeWalker.Agent.Abstractions;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeWalker.Agent.Storage;

public sealed class OperationStore
{
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    public OperationStore(AgentSettings settings, string configDirectory)
    {
        var database = Resolve(configDirectory, settings.Paths.Database);
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        Initialize();
    }
    public async Task SaveAsync(OperationRecord operation, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO operations(operation_id,status,created_at,json) VALUES($id,$status,$created,$json) ON CONFLICT(operation_id) DO UPDATE SET status=$status,json=$json";
        command.Parameters.AddWithValue("$id", operation.OperationId); command.Parameters.AddWithValue("$status", operation.Status.ToString()); command.Parameters.AddWithValue("$created", operation.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(operation, Json));
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<OperationRecord?> GetAsync(string operationId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(ct); var command = connection.CreateCommand(); command.CommandText = "SELECT json FROM operations WHERE operation_id=$id"; command.Parameters.AddWithValue("$id", operationId);
        var value = await command.ExecuteScalarAsync(ct); return value is string json ? JsonSerializer.Deserialize<OperationRecord>(json, Json) : null;
    }
    public async Task<Page<OperationRecord>> ListAsync(int offset, int limit, CancellationToken ct = default)
    {
        offset = Math.Max(0, offset); limit = Math.Clamp(limit, 1, 100);
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(ct);
        var total = connection.CreateCommand(); total.CommandText = "SELECT COUNT(*) FROM operations"; var count = Convert.ToInt32(await total.ExecuteScalarAsync(ct));
        var command = connection.CreateCommand(); command.CommandText = "SELECT json FROM operations ORDER BY created_at DESC LIMIT $limit OFFSET $offset"; command.Parameters.AddWithValue("$limit", limit); command.Parameters.AddWithValue("$offset", offset);
        var page = new Page<OperationRecord> { Total = count, Offset = offset };
        await using var reader = await command.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) { var item = JsonSerializer.Deserialize<OperationRecord>(reader.GetString(0), Json); if (item != null) page.Items.Add(item); }
        page.Count = page.Items.Count; page.HasMore = offset + page.Count < count; page.NextOffset = page.HasMore ? offset + page.Count : null; return page;
    }
    public async Task AuditAsync(string tool, string arguments, bool success, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(ct); var command = connection.CreateCommand(); command.CommandText = "INSERT INTO audit(created_at,tool,arguments,success) VALUES($created,$tool,$arguments,$success)"; command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$tool", tool); command.Parameters.AddWithValue("$arguments", arguments); command.Parameters.AddWithValue("$success", success ? 1 : 0); await command.ExecuteNonQueryAsync(ct);
    }
    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS operations(operation_id TEXT PRIMARY KEY,status TEXT NOT NULL,created_at TEXT NOT NULL,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS audit(id INTEGER PRIMARY KEY AUTOINCREMENT,created_at TEXT NOT NULL,tool TEXT NOT NULL,arguments TEXT NOT NULL,success INTEGER NOT NULL); PRAGMA user_version=1;"; command.ExecuteNonQuery();
    }
    private static string Resolve(string basePath, string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(basePath, path));
}
