using CodeWalker.Agent.Abstractions;
using CodeWalker.Agent.Core;
using CodeWalker.Agent.Security;
using CodeWalker.Agent.Storage;
using CodeWalker.GameFiles;
using Xunit;

namespace CodeWalker.Agent.Tests;

public sealed class SecurityAndTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CodeWalker.Agent.Tests", Guid.NewGuid().ToString("N"));
    public SecurityAndTransactionTests() { Directory.CreateDirectory(_root); File.WriteAllText(Path.Combine(_root, "GTA5.exe"), "test"); }
    [Fact]
    public void PathPolicy_denies_paths_outside_mods() => Assert.Throws<InvalidOperationException>(() => new PathPolicy().RequireModsPath(_root, Path.Combine(_root, "update", "update.rpf")));
    [Fact]
    public async Task Transaction_requires_token_and_writes_only_after_commit()
    {
        var settings = new AgentSettings { Paths = new PathSettings { Database = "data/test.db", Backups = "data/backups" }, Security = new SecuritySettings { BlockWhenGameRunning = false } };
        var games = new GameInstallationService(settings); var service = new OperationService(new OperationStore(settings, _root), settings, _root, new PathPolicy(), new ProcessGuard(), new ArchiveLockManager(), games);
        var op = await service.CreateAsync(_root); var target = Path.Combine(_root, "mods", "example.txt"); await service.StageAsync(op.OperationId, new PlannedChange { Kind = ChangeKind.TextEdit, TargetPath = target, Content = "safe" });
        Assert.False(File.Exists(target)); var validation = await service.ValidateAsync(op.OperationId); await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(op.OperationId, "wrong")); var completed = await service.CommitAsync(op.OperationId, validation.ApprovalToken);
        Assert.Equal(OperationStatus.Completed, completed.Status); Assert.Equal("safe", await File.ReadAllTextAsync(target));
    }
    [Fact]
    public async Task Mod_analysis_reports_missing_ped_files()
    {
        var dir = Path.Combine(_root, "ped"); Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, "sample.ydd"), new byte[] { 1 }); File.WriteAllBytes(Path.Combine(dir, "sample.yft"), new byte[] { 1 }); var result = await new ModPackageService().InspectAsync(dir);
        Assert.Equal("addon-ped", result.ModType); Assert.Contains("ymt", result.MissingFiles); Assert.Contains("ytd", result.MissingFiles);
    }
    [Fact]
    public async Task Rpf_read_service_lists_stats_and_extracts_a_test_archive()
    {
        var archive = Path.Combine(_root, "sample.rpf"); var rpf = RpfFile.CreateNew(_root, "sample.rpf", RpfEncryption.OPEN); RpfFile.CreateFile(rpf.Root, "hello.txt", System.Text.Encoding.UTF8.GetBytes("hello"), true);
        var service = new RpfReadService(); var entries = await service.ListAsync(archive, "", true, 0, 10); var stat = await service.StatAsync(archive, "hello.txt", true); var data = await service.ExtractAsync(archive, "hello.txt");
        Assert.Single(entries.Items, x => x.Name == "hello.txt"); Assert.Equal("binary", stat.ResourceType); Assert.NotNull(stat.Sha256); Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(data));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
