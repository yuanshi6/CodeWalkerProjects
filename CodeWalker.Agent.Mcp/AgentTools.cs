using CodeWalker.Agent.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace CodeWalker.Agent.Mcp;

internal static class AgentToolRuntime
{
    public static IServiceProvider Services { get; set; } = null!;
    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    public static string Json<T>(ToolResult<T> result) => JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false });
    public static string Run<T>(string tool, Func<T> work, string? operationId = null) { try { return Json(ToolResult<T>.Ok(tool, "Completed.", work(), operationId)); } catch (Exception ex) { return Json(ToolResult<T>.Fail(tool, Code(ex), Friendly(ex), "Review the tool parameters and server log.", operationId)); } }
    public static async Task<string> RunAsync<T>(string tool, Func<Task<T>> work, string? operationId = null) { try { return Json(ToolResult<T>.Ok(tool, "Completed.", await work(), operationId)); } catch (Exception ex) { return Json(ToolResult<T>.Fail(tool, Code(ex), Friendly(ex), "Review the tool parameters and server log.", operationId)); } }
    private static string Code(Exception ex) { var message = ex.Message; var colon = message.IndexOf(':'); return colon > 0 && message[..colon].All(c => char.IsUpper(c) || c == '_') ? message[..colon] : ex is FileNotFoundException ? "NOT_FOUND" : "INTERNAL_ERROR"; }
    private static string Friendly(Exception ex) => ex.Message.Contains(':') ? ex.Message[(ex.Message.IndexOf(':') + 1)..].Trim() : ex.Message;
}

[McpServerToolType]
public static class GtaTools
{
    [McpServerTool, Description("Scans a local GTA V directory and reports edition, executable, and mods status. Read-only.")]
    public static string gta_scan_installation(string? gamePath = null) => AgentToolRuntime.Run("gta_scan_installation", () => AgentToolRuntime.Get<IGameInstallationService>().Scan(gamePath));
    [McpServerTool, Description("Returns the configured GTA V installation status. Read-only.")]
    public static string gta_get_status() => AgentToolRuntime.Run("gta_get_status", () => AgentToolRuntime.Get<IGameInstallationService>().GetStatus());
    [McpServerTool, Description("Returns the configured GTA V edition. Read-only.")]
    public static string gta_detect_edition() => AgentToolRuntime.Run("gta_detect_edition", () => AgentToolRuntime.Get<IGameInstallationService>().GetStatus().Edition);
}

[McpServerToolType]
public static class RpfTools
{
    [McpServerTool, Description("Lists an RPF directory. Use recursive=false for a single level; returns paginated data. Read-only.")]
    public static Task<string> rpf_list(string archivePath, string internalPath = "", int offset = 0, int limit = 50) => AgentToolRuntime.RunAsync("rpf_list", () => AgentToolRuntime.Get<IRpfReadService>().ListAsync(archivePath, internalPath, false, offset, limit));
    [McpServerTool, Description("Lists an RPF subtree recursively with pagination. Read-only.")]
    public static Task<string> rpf_tree(string archivePath, string internalPath = "", int offset = 0, int limit = 50) => AgentToolRuntime.RunAsync("rpf_tree", () => AgentToolRuntime.Get<IRpfReadService>().ListAsync(archivePath, internalPath, true, offset, limit));
    [McpServerTool, Description("Searches an RPF tree by a case-insensitive filename/path fragment. Read-only.")]
    public static async Task<string> rpf_search(string archivePath, string query, int offset = 0, int limit = 50) => await AgentToolRuntime.RunAsync("rpf_search", async () => { var all = await AgentToolRuntime.Get<IRpfReadService>().ListAsync(archivePath, "", true, 0, 10000); var items = all.Items.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.InternalPath.Contains(query, StringComparison.OrdinalIgnoreCase)).Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 100)).ToList(); return new Page<RpfEntryInfo> { Total = all.Items.Count(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.InternalPath.Contains(query, StringComparison.OrdinalIgnoreCase)), Offset = offset, Count = items.Count, Items = items, HasMore = offset + items.Count < all.Total }; });
    [McpServerTool, Description("Gets RPF entry metadata and optional SHA-256. Read-only.")]
    public static Task<string> rpf_stat(string archivePath, string internalPath, bool includeHash = false) => AgentToolRuntime.RunAsync("rpf_stat", () => AgentToolRuntime.Get<IRpfReadService>().StatAsync(archivePath, internalPath, includeHash));
    [McpServerTool, Description("Extracts a small RPF file as base64. This tool never writes to disk. Read-only.")]
    public static async Task<string> rpf_extract(string archivePath, string internalPath, int maxBytes = 1048576) => await AgentToolRuntime.RunAsync("rpf_extract", async () => { var data = await AgentToolRuntime.Get<IRpfReadService>().ExtractAsync(archivePath, internalPath); if (data.Length > Math.Clamp(maxBytes, 1, 4 * 1024 * 1024)) throw new InvalidOperationException("FILE_TOO_LARGE: use rpf_stat and a local workflow for larger data."); return new { base64 = Convert.ToBase64String(data), size = data.Length }; });
    [McpServerTool, Description("Reads a UTF-8 text file from an RPF. Read-only.")]
    public static async Task<string> rpf_read_text(string archivePath, string internalPath, int maxBytes = 1048576) => await AgentToolRuntime.RunAsync("rpf_read_text", async () => { var data = await AgentToolRuntime.Get<IRpfReadService>().ExtractAsync(archivePath, internalPath); if (data.Length > maxBytes) throw new InvalidOperationException("FILE_TOO_LARGE: requested text exceeds maximum size."); return Encoding.UTF8.GetString(data); });
    [McpServerTool, Description("Reads XML stored in an RPF and verifies it is well formed. Read-only.")]
    public static async Task<string> rpf_read_xml(string archivePath, string internalPath) => await AgentToolRuntime.RunAsync("rpf_read_xml", async () => { var text = Encoding.UTF8.GetString(await AgentToolRuntime.Get<IRpfReadService>().ExtractAsync(archivePath, internalPath)); var doc = new System.Xml.XmlDocument(); doc.LoadXml(text); return doc.OuterXml; });
}
