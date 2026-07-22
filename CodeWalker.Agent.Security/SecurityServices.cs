using CodeWalker.Agent.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeWalker.Agent.Security;

public sealed class PathPolicy
{
    public string RequireModsPath(string gamePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(targetPath)) throw new InvalidOperationException("SECURITY_PATH_DENIED: game and target paths are required.");
        var root = Path.GetFullPath(Path.Combine(gamePath, "mods")) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(targetPath);
        if (targetPath.Contains("..") || !candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SECURITY_PATH_DENIED: writes are restricted to the game mods directory.");
        EnsureNoReparsePointEscape(root, candidate);
        return candidate;
    }
    public string RequireSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..") || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new InvalidOperationException("SECURITY_PATH_DENIED: invalid relative path.");
        return path.Replace('/', '\\').TrimStart('\\');
    }
    private static void EnsureNoReparsePointEscape(string root, string candidate)
    {
        var cursor = new DirectoryInfo(Path.GetDirectoryName(candidate) ?? candidate);
        while (cursor.Exists && cursor.FullName.Length >= root.Length)
        {
            if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("SECURITY_PATH_DENIED: symbolic links are not allowed in write targets.");
            var parent = cursor.Parent;
            if (parent == null) break;
            cursor = parent;
        }
    }
}

public sealed class ProcessGuard
{
    private static readonly string[] Names = { "GTA5", "GTA5_Enhanced", "PlayGTAV" };
    public void EnsureGameStopped() { if (Process.GetProcesses().Any(p => Names.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))) throw new InvalidOperationException("GAME_RUNNING: close GTA V before modifying mods."); }
}

public sealed class ArchiveLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
    public async Task<IDisposable> AcquireAsync(string archivePath, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(Path.GetFullPath(archivePath), _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false)) throw new InvalidOperationException("ARCHIVE_LOCKED: another transaction is using this target.");
        return new Releaser(semaphore);
    }
    private sealed class Releaser : IDisposable { private readonly SemaphoreSlim _semaphore; public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore; public void Dispose() => _semaphore.Release(); }
}
