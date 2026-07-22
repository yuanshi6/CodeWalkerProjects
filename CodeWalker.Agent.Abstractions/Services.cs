using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeWalker.Agent.Abstractions;

public interface IGameInstallationService { GameInstallationInfo Scan(string? gamePath = null); GameInstallationInfo GetStatus(); }
public interface IRpfReadService { Task<Page<RpfEntryInfo>> ListAsync(string archivePath, string internalPath, bool recursive, int offset, int limit, CancellationToken cancellationToken = default); Task<RpfEntryInfo> StatAsync(string archivePath, string internalPath, bool includeHash, CancellationToken cancellationToken = default); Task<byte[]> ExtractAsync(string archivePath, string internalPath, CancellationToken cancellationToken = default); }
public interface IModPackageService { Task<ModAnalysis> InspectAsync(string sourcePath, CancellationToken cancellationToken = default); }
public interface IPedInstallationService { Task<PedAnalysis> AnalyzeAsync(string sourcePath, CancellationToken cancellationToken = default); Task<PedBuildResult> BuildAddonAsync(string sourcePath, string addonName, CancellationToken cancellationToken = default); }
public interface IOperationService { Task<OperationRecord> CreateAsync(string gamePath, CancellationToken cancellationToken = default); Task<OperationRecord?> GetAsync(string operationId, CancellationToken cancellationToken = default); Task<Page<OperationRecord>> ListAsync(int offset, int limit, CancellationToken cancellationToken = default); Task<OperationRecord> StageAsync(string operationId, PlannedChange change, CancellationToken cancellationToken = default); Task<ValidationResult> ValidateAsync(string operationId, CancellationToken cancellationToken = default); Task<OperationRecord> CommitAsync(string operationId, string approvalToken, CancellationToken cancellationToken = default); Task<OperationRecord> RollbackAsync(string operationId, CancellationToken cancellationToken = default); Task<OperationRecord> CancelAsync(string operationId, CancellationToken cancellationToken = default); }
