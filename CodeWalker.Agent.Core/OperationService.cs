using CodeWalker.Agent.Abstractions;
using CodeWalker.Agent.Security;
using CodeWalker.Agent.Storage;
using CodeWalker.GameFiles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeWalker.Agent.Core;

public sealed class OperationService : IOperationService
{
    private readonly OperationStore _store; private readonly AgentSettings _settings; private readonly string _configDirectory; private readonly PathPolicy _paths; private readonly ProcessGuard _process; private readonly ArchiveLockManager _locks; private readonly IGameInstallationService _games;
    public OperationService(OperationStore store, AgentSettings settings, string configDirectory, PathPolicy paths, ProcessGuard process, ArchiveLockManager locks, IGameInstallationService games) { _store = store; _settings = settings; _configDirectory = configDirectory; _paths = paths; _process = process; _locks = locks; _games = games; }
    public async Task<OperationRecord> CreateAsync(string gamePath, CancellationToken ct = default) { var game = _games.Scan(gamePath); if (!game.Exists) throw new InvalidOperationException("GAME_NOT_FOUND: a valid GTA V installation is required."); var op = new OperationRecord { OperationId = $"gta-op-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..35], Status = OperationStatus.Created, CreatedAt = DateTimeOffset.UtcNow, GameEdition = game.Edition, GamePath = game.GamePath }; await _store.SaveAsync(op, ct); return op; }
    public Task<OperationRecord?> GetAsync(string id, CancellationToken ct = default) => _store.GetAsync(id, ct);
    public Task<Page<OperationRecord>> ListAsync(int offset, int limit, CancellationToken ct = default) => _store.ListAsync(offset, limit, ct);
    public async Task<OperationRecord> StageAsync(string id, PlannedChange change, CancellationToken ct = default) { var op = await Required(id, ct); RequireState(op, OperationStatus.Created, OperationStatus.Analyzed, OperationStatus.Staged); _paths.RequireModsPath(op.GamePath, change.TargetPath); if (!string.IsNullOrWhiteSpace(change.InternalPath)) _paths.RequireSafeRelativePath(change.InternalPath); if (change.Kind is ChangeKind.Add or ChangeKind.Replace && (string.IsNullOrWhiteSpace(change.SourcePath) || !File.Exists(change.SourcePath))) throw new FileNotFoundException("Stage source file was not found.", change.SourcePath); op.PlannedChanges.Add(change); op.Status = OperationStatus.Staged; await _store.SaveAsync(op, ct); return op; }
    public async Task<ValidationResult> ValidateAsync(string id, CancellationToken ct = default)
    {
        var op = await Required(id, ct); RequireState(op, OperationStatus.Staged); if (op.PlannedChanges.Count == 0) throw new InvalidOperationException("OPERATION_INVALID: no staged changes."); if (_settings.Security.BlockWhenGameRunning) _process.EnsureGameStopped();
        foreach (var change in op.PlannedChanges) { var target = _paths.RequireModsPath(op.GamePath, change.TargetPath); if (change.Kind is ChangeKind.Add or ChangeKind.Replace && new FileInfo(change.SourcePath!).Length > _settings.Security.MaximumFileSizeMb * 1024L * 1024L) throw new InvalidOperationException("FILE_TOO_LARGE: source exceeds configured maximum size."); var root = Path.GetPathRoot(target)!; if (new DriveInfo(root).AvailableFreeSpace < 10 * 1024 * 1024) throw new InvalidOperationException("DISK_SPACE_LOW: at least 10 MB free space is required."); }
        op.PlanHash = Hash(op.PlannedChanges); op.ApprovalToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(); op.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.Security.ApprovalTokenMinutes); op.Status = OperationStatus.Validated; await _store.SaveAsync(op, ct); return new ValidationResult { ApprovalToken = op.ApprovalToken, ExpiresAt = op.ApprovalExpiresAt.Value, PlanHash = op.PlanHash, Checks = new List<string> { "mods path allowed", "game process stopped", "disk space available", "approval token issued" } };
    }
    public async Task<OperationRecord> CommitAsync(string id, string token, CancellationToken ct = default)
    {
        var op = await Required(id, ct); RequireState(op, OperationStatus.Validated); if (!string.Equals(token, op.ApprovalToken, StringComparison.Ordinal) || op.ApprovalExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("APPROVAL_REQUIRED: validate again and provide the unexpired approval token."); if (_settings.Security.BlockWhenGameRunning) _process.EnsureGameStopped();
        op.Status = OperationStatus.Committing; await _store.SaveAsync(op, ct);
        try { foreach (var change in op.PlannedChanges) { var target = _paths.RequireModsPath(op.GamePath, change.TargetPath); using var held = await _locks.AcquireAsync(target, ct); Backup(op, target); await _store.SaveAsync(op, ct); Apply(change, target); Verify(change, target); } op.Status = OperationStatus.Completed; op.ApprovalToken = null; await _store.SaveAsync(op, ct); return op; }
        catch (Exception ex) { op.Error = ex.Message; op.Status = OperationStatus.Failed; await _store.SaveAsync(op, ct); return await RollbackAsync(id, ct); }
    }
    public async Task<OperationRecord> RollbackAsync(string id, CancellationToken ct = default) { var op = await Required(id, ct); foreach (var item in op.BackupPaths.AsEnumerable().Reverse()) { var pair = item.Split('\t'); var target = pair[0]; var backup = pair.Length > 1 ? pair[1] : ""; if (string.IsNullOrEmpty(backup)) { if (File.Exists(target)) File.Delete(target); } else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(backup, target, true); } } op.Status = OperationStatus.RolledBack; await _store.SaveAsync(op, ct); return op; }
    public async Task<OperationRecord> CancelAsync(string id, CancellationToken ct = default) { var op = await Required(id, ct); RequireState(op, OperationStatus.Created, OperationStatus.Analyzed, OperationStatus.Staged, OperationStatus.Validated); op.Status = OperationStatus.Cancelled; op.ApprovalToken = null; await _store.SaveAsync(op, ct); return op; }
    private async Task<OperationRecord> Required(string id, CancellationToken ct) => await _store.GetAsync(id, ct) ?? throw new InvalidOperationException("OPERATION_NOT_FOUND: use operation_list to find an existing operation.");
    private static void RequireState(OperationRecord op, params OperationStatus[] allowed) { if (!allowed.Contains(op.Status)) throw new InvalidOperationException($"OPERATION_INVALID_STATE: current state is {op.Status}."); }
    private void Backup(OperationRecord op, string target) { var root = Resolve(_settings.Paths.Backups); var dir = Path.Combine(root, op.OperationId); Directory.CreateDirectory(dir); var backup = File.Exists(target) ? Path.Combine(dir, $"{op.BackupPaths.Count:D4}-{Path.GetFileName(target)}") : ""; if (!string.IsNullOrEmpty(backup)) File.Copy(target, backup, true); op.BackupPaths.Add(target + "\t" + backup); }
    private static void Apply(PlannedChange change, string target)
    {
        if (string.IsNullOrWhiteSpace(change.InternalPath)) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); if (change.Kind == ChangeKind.Delete) { if (File.Exists(target)) File.Delete(target); } else if (change.Kind is ChangeKind.TextEdit or ChangeKind.XmlEdit) File.WriteAllText(target, change.Content ?? "", new UTF8Encoding(false)); else File.Copy(change.SourcePath!, target, true); return; }
        var rpf = RpfReadService.Open(target); var internalPath = change.InternalPath!.Replace('/', '\\').TrimStart('\\'); var directoryPath = Path.GetDirectoryName(internalPath) ?? ""; var fileName = Path.GetFileName(internalPath); var existing = RpfReadService.Find(rpf.Root, internalPath) as RpfFileEntry;
        if (change.Kind == ChangeKind.Delete) { if (existing != null) RpfFile.DeleteEntry(existing); return; }
        var data = change.Kind is ChangeKind.TextEdit or ChangeKind.XmlEdit ? new UTF8Encoding(false).GetBytes(change.Content ?? "") : File.ReadAllBytes(change.SourcePath!); var directory = RpfReadService.EnsureDirectory(rpf.Root, directoryPath); RpfFile.CreateFile(directory, fileName, data, true);
    }
    private static void Verify(PlannedChange change, string target) { if (!File.Exists(target) && change.Kind != ChangeKind.Delete) throw new InvalidOperationException("VERIFY_FAILED: target was not written."); if (!string.IsNullOrWhiteSpace(change.InternalPath) && File.Exists(target)) { var rpf = RpfReadService.Open(target); var exists = RpfReadService.Find(rpf.Root, change.InternalPath!) != null; if (exists == (change.Kind == ChangeKind.Delete)) throw new InvalidOperationException("VERIFY_FAILED: RPF change was not persisted."); } }
    private string Resolve(string value) => Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(_configDirectory, value));
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}
