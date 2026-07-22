using CodeWalker.Agent.Abstractions;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace CodeWalker.Agent.Mcp;

[McpServerToolType]
public static class PackageTools
{
    [McpServerTool, Description("Inspects a mod folder, zip, rar, 7z, OIV, or RPF and classifies it without installing. Read-only.")]
    public static Task<string> mod_inspect(string sourcePath) => AgentToolRuntime.RunAsync("mod_inspect", () => AgentToolRuntime.Get<IModPackageService>().InspectAsync(sourcePath));
    [McpServerTool, Description("Analyzes an Add-on Ped package for required files and filename consistency. Read-only.")]
    public static Task<string> ped_analyze(string sourcePath) => AgentToolRuntime.RunAsync("ped_analyze", () => AgentToolRuntime.Get<IPedInstallationService>().AnalyzeAsync(sourcePath));
    [McpServerTool, Description("Builds an isolated agent_<model> DLC RPF in a temporary workspace. It never writes to GTA or mods.")]
    public static Task<string> ped_build_addon(string sourcePath, string addonName) => AgentToolRuntime.RunAsync("ped_build_addon", () => AgentToolRuntime.Get<IPedInstallationService>().BuildAddonAsync(sourcePath, addonName));
    [McpServerTool, Description("Inspects a GTA resource and reports its type and file metadata. Read-only.")]
    public static async Task<string> resource_inspect(string archivePath, string internalPath) => await AgentToolRuntime.RunAsync("resource_inspect", async () => { var stat = await AgentToolRuntime.Get<IRpfReadService>().StatAsync(archivePath, internalPath, true); return new ResourceInfo { ResourceType = stat.ResourceType, Size = stat.Size, Properties = new Dictionary<string, string> { ["sha256"] = stat.Sha256 ?? "", ["encrypted"] = stat.Encrypted.ToString() } }; });
    [McpServerTool, Description("Returns preview availability and resource metadata. Rendering worker integration is used when configured. Read-only.")]
    public static async Task<string> resource_preview(string archivePath, string internalPath) => await AgentToolRuntime.RunAsync("resource_preview", async () => { var stat = await AgentToolRuntime.Get<IRpfReadService>().StatAsync(archivePath, internalPath, false); return new ResourceInfo { ResourceType = stat.ResourceType, Size = stat.Size, Properties = new Dictionary<string, string> { ["status"] = "preview worker must be configured for raster output" } }; });
}

[McpServerToolType]
public static class TransactionTools
{
    [McpServerTool, Description("Creates a write transaction. It changes nothing until staged, validated, and committed.")]
    public static Task<string> operation_create(string gamePath) => AgentToolRuntime.RunAsync("operation_create", () => AgentToolRuntime.Get<IOperationService>().CreateAsync(gamePath));
    [McpServerTool, Description("Gets one transaction, including staged changes and status. Read-only.")]
    public static Task<string> operation_get(string operationId) => AgentToolRuntime.RunAsync("operation_get", async () => await AgentToolRuntime.Get<IOperationService>().GetAsync(operationId) ?? throw new InvalidOperationException("OPERATION_NOT_FOUND: no transaction matches this id."), operationId);
    [McpServerTool, Description("Lists transactions with pagination. Read-only.")]
    public static Task<string> operation_list(int offset = 0, int limit = 50) => AgentToolRuntime.RunAsync("operation_list", () => AgentToolRuntime.Get<IOperationService>().ListAsync(offset, limit));
    [McpServerTool, Description("Stages adding a local file to a mods path or an RPF internal path. Does not write yet.")]
    public static Task<string> rpf_stage_add(string operationId, string targetPath, string sourcePath, string? internalPath = null) => Stage(operationId, ChangeKind.Add, targetPath, sourcePath, internalPath, null);
    [McpServerTool, Description("Stages replacing a local file or RPF internal file under mods. Does not write yet.")]
    public static Task<string> rpf_stage_replace(string operationId, string targetPath, string sourcePath, string? internalPath = null) => Stage(operationId, ChangeKind.Replace, targetPath, sourcePath, internalPath, null);
    [McpServerTool, Description("Stages deletion of a file or RPF internal entry under mods. Does not write yet.")]
    public static Task<string> rpf_stage_delete(string operationId, string targetPath, string? internalPath = null) => Stage(operationId, ChangeKind.Delete, targetPath, null, internalPath, null);
    [McpServerTool, Description("Stages replacement of a UTF-8 text file or RPF text entry. Does not write yet.")]
    public static Task<string> rpf_stage_text_edit(string operationId, string targetPath, string content, string? internalPath = null) => Stage(operationId, ChangeKind.TextEdit, targetPath, null, internalPath, content);
    [McpServerTool, Description("Stages replacement of well-formed XML. Does not write yet.")]
    public static Task<string> rpf_stage_xml_edit(string operationId, string targetPath, string content, string? internalPath = null) { var doc = new System.Xml.XmlDocument(); doc.LoadXml(content); return Stage(operationId, ChangeKind.XmlEdit, targetPath, null, internalPath, doc.OuterXml); }
    [McpServerTool, Description("Validates all staged changes, creates backups on commit, and returns a one-time ten-minute confirmation token.")]
    public static Task<string> operation_validate(string operationId) => AgentToolRuntime.RunAsync("operation_validate", () => AgentToolRuntime.Get<IOperationService>().ValidateAsync(operationId), operationId);
    [McpServerTool, Description("Commits a validated transaction. Requires the unexpired token from operation_validate.")]
    public static Task<string> operation_commit(string operationId, string approvalToken) => AgentToolRuntime.RunAsync("operation_commit", () => AgentToolRuntime.Get<IOperationService>().CommitAsync(operationId, approvalToken), operationId);
    [McpServerTool, Description("Restores backups for a transaction. This only writes within mods.")]
    public static Task<string> operation_rollback(string operationId) => AgentToolRuntime.RunAsync("operation_rollback", () => AgentToolRuntime.Get<IOperationService>().RollbackAsync(operationId), operationId);
    [McpServerTool, Description("Cancels an uncommitted transaction and invalidates its approval token.")]
    public static Task<string> operation_cancel(string operationId) => AgentToolRuntime.RunAsync("operation_cancel", () => AgentToolRuntime.Get<IOperationService>().CancelAsync(operationId), operationId);
    private static Task<string> Stage(string operationId, ChangeKind kind, string target, string? source, string? internalPath, string? content) => AgentToolRuntime.RunAsync("rpf_stage_" + kind.ToString().ToLowerInvariant(), () => AgentToolRuntime.Get<IOperationService>().StageAsync(operationId, new PlannedChange { Kind = kind, TargetPath = target, SourcePath = source, InternalPath = internalPath, Content = content }), operationId);
}
