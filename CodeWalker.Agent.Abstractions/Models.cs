using System;
using System.Collections.Generic;

namespace CodeWalker.Agent.Abstractions;

public enum GameEdition { Unknown, Legacy, Enhanced }
public enum OperationStatus { Created, Analyzed, Staged, Validated, Committing, Committed, Verifying, Completed, Failed, RolledBack, Cancelled }
public enum ChangeKind { Add, Replace, Delete, TextEdit, XmlEdit, AddonInstall, AddonEnable, AddonDisable }

public sealed class ToolError
{
    public string Code { get; set; } = "INTERNAL_ERROR";
    public string Message { get; set; } = "The operation failed.";
    public string? SuggestedAction { get; set; }
}

public sealed class ToolResult<T>
{
    public bool Success { get; set; }
    public string Tool { get; set; } = "";
    public string? OperationId { get; set; }
    public string Message { get; set; } = "";
    public T? Data { get; set; }
    public List<string> Warnings { get; set; } = new List<string>();
    public List<ToolError> Errors { get; set; } = new List<ToolError>();

    public static ToolResult<T> Ok(string tool, string message, T? data = default, string? operationId = null) =>
        new ToolResult<T> { Success = true, Tool = tool, Message = message, Data = data, OperationId = operationId };
    public static ToolResult<T> Fail(string tool, string code, string message, string? action = null, string? operationId = null) =>
        new ToolResult<T> { Success = false, Tool = tool, Message = message, OperationId = operationId, Errors = new List<ToolError> { new ToolError { Code = code, Message = message, SuggestedAction = action } } };
}

public sealed class Page<T>
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Count { get; set; }
    public bool HasMore { get; set; }
    public int? NextOffset { get; set; }
    public List<T> Items { get; set; } = new List<T>();
}

public sealed class AgentSettings
{
    public GameSettings Game { get; set; } = new GameSettings();
    public PathSettings Paths { get; set; } = new PathSettings();
    public SecuritySettings Security { get; set; } = new SecuritySettings();
    public McpSettings Mcp { get; set; } = new McpSettings();
}
public sealed class GameSettings { public string LegacyPath { get; set; } = ""; public string EnhancedPath { get; set; } = ""; public string PreferredEdition { get; set; } = "auto"; }
public sealed class PathSettings { public string Workspace { get; set; } = "./data/workspaces"; public string Backups { get; set; } = "./data/backups"; public string Logs { get; set; } = "./data/logs"; public string Previews { get; set; } = "./data/previews"; public string Database { get; set; } = "./data/agent.db"; }
public sealed class SecuritySettings { public bool AllowOriginalArchiveWrites { get; set; } = false; public bool RequireModsFolder { get; set; } = true; public bool RequireBackup { get; set; } = true; public bool BlockWhenGameRunning { get; set; } = true; public int MaximumFileSizeMb { get; set; } = 2048; public int MaximumNestedRpfDepth { get; set; } = 4; public int ApprovalTokenMinutes { get; set; } = 10; }
public sealed class McpSettings { public string Transport { get; set; } = "stdio"; public bool LogToolCalls { get; set; } = true; }

public sealed class GameInstallationInfo { public string GamePath { get; set; } = ""; public GameEdition Edition { get; set; } = GameEdition.Unknown; public bool Exists { get; set; } public bool ModsExists { get; set; } public string ExecutablePath { get; set; } = ""; }
public sealed class RpfEntryInfo { public string ArchivePath { get; set; } = ""; public string InternalPath { get; set; } = ""; public string Name { get; set; } = ""; public long Size { get; set; } public string ResourceType { get; set; } = "unknown"; public bool Encrypted { get; set; } public string? Sha256 { get; set; } public bool Exists { get; set; } = true; public bool IsDirectory { get; set; } }
public sealed class ResourceInfo { public string ResourceType { get; set; } = "unknown"; public long Size { get; set; } public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>(); public string? PreviewPath { get; set; } }
public class ModAnalysis { public string ModType { get; set; } = "unknown"; public double Confidence { get; set; } public string? ModelName { get; set; } public List<string> Files { get; set; } = new List<string>(); public List<string> MissingFiles { get; set; } = new List<string>(); public List<string> Conflicts { get; set; } = new List<string>(); public string? SuggestedTarget { get; set; } public bool RequiresDlclistRegistration { get; set; } }
public sealed class PedAnalysis : ModAnalysis { public bool NamesConsistent { get; set; } public bool HasYdd { get; set; } public bool HasYft { get; set; } public bool HasYmt { get; set; } public bool HasYtd { get; set; } }
public sealed class PedBuildResult { public string AddonName { get; set; } = ""; public string WorkspacePath { get; set; } = ""; public string DlcRpfPath { get; set; } = ""; public List<string> IncludedFiles { get; set; } = new List<string>(); }
public sealed class PlannedChange { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public ChangeKind Kind { get; set; } public string TargetPath { get; set; } = ""; public string? InternalPath { get; set; } public string? SourcePath { get; set; } public string? ExpectedSha256 { get; set; } public string? Content { get; set; } }
public sealed class OperationRecord { public string OperationId { get; set; } = ""; public OperationStatus Status { get; set; } public DateTimeOffset CreatedAt { get; set; } public GameEdition GameEdition { get; set; } public string GamePath { get; set; } = ""; public string? ApprovalToken { get; set; } public DateTimeOffset? ApprovalExpiresAt { get; set; } public string? PlanHash { get; set; } public string? Error { get; set; } public List<PlannedChange> PlannedChanges { get; set; } = new List<PlannedChange>(); public List<string> BackupPaths { get; set; } = new List<string>(); }
public sealed class ValidationResult { public string ApprovalToken { get; set; } = ""; public DateTimeOffset ExpiresAt { get; set; } public string PlanHash { get; set; } = ""; public List<string> Checks { get; set; } = new List<string>(); }
