using CodeWalker.Agent.Abstractions;
using CodeWalker.GameFiles;
using System.Security.Cryptography;

namespace CodeWalker.Agent.Core;

public sealed class RpfReadService : IRpfReadService
{
    public Task<Page<RpfEntryInfo>> ListAsync(string archivePath, string internalPath, bool recursive, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var rpf = Open(archivePath); var all = Entries(rpf.Root, recursive).Where(e => string.IsNullOrWhiteSpace(internalPath) || PathMatches(e.Path, Normalize(internalPath))).Select(e => ToInfo(archivePath, e, null)).ToList();
        offset = Math.Max(0, offset); limit = Math.Clamp(limit, 1, 100); var items = all.Skip(offset).Take(limit).ToList(); return Task.FromResult(new Page<RpfEntryInfo> { Total = all.Count, Offset = offset, Count = items.Count, HasMore = offset + items.Count < all.Count, NextOffset = offset + items.Count < all.Count ? offset + items.Count : null, Items = items });
    }
    public Task<RpfEntryInfo> StatAsync(string archivePath, string internalPath, bool includeHash, CancellationToken cancellationToken = default)
    {
        var rpf = Open(archivePath); var entry = Find(rpf.Root, internalPath) ?? throw new FileNotFoundException("RPF entry was not found.", internalPath); byte[]? data = null; if (includeHash && entry is RpfFileEntry file) data = file.File.ExtractFile(file); return Task.FromResult(ToInfo(archivePath, entry, data));
    }
    public Task<byte[]> ExtractAsync(string archivePath, string internalPath, CancellationToken cancellationToken = default)
    {
        var rpf = Open(archivePath); var entry = Find(rpf.Root, internalPath) as RpfFileEntry ?? throw new FileNotFoundException("RPF file was not found.", internalPath); return Task.FromResult(entry.File.ExtractFile(entry));
    }
    public static RpfFile Open(string archivePath) { if (!File.Exists(archivePath)) throw new FileNotFoundException("RPF archive was not found.", archivePath); var rpf = new RpfFile(archivePath, Path.GetFileName(archivePath)); rpf.ScanStructure(null, _ => { }); return rpf; }
    public static RpfEntry? Find(RpfDirectoryEntry directory, string path) { var normalized = Normalize(path); return Entries(directory, true).FirstOrDefault(e => e.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase) || e.Path.EndsWith("\\" + normalized, StringComparison.OrdinalIgnoreCase)); }
    public static IEnumerable<RpfEntry> Entries(RpfDirectoryEntry directory, bool recursive) { foreach (var child in directory.Directories) { yield return child; if (recursive) foreach (var nested in Entries(child, true)) yield return nested; } foreach (var file in directory.Files) yield return file; }
    public static RpfDirectoryEntry EnsureDirectory(RpfDirectoryEntry root, string relative) { var current = root; foreach (var part in Normalize(relative).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) { current = current.Directories.FirstOrDefault(d => d.Name.Equals(part, StringComparison.OrdinalIgnoreCase)) ?? RpfFile.CreateDirectory(current, part); } return current; }
    private static RpfEntryInfo ToInfo(string archive, RpfEntry entry, byte[]? data) { var file = entry as RpfFileEntry; return new RpfEntryInfo { ArchivePath = archive, InternalPath = entry.Path, Name = entry.Name, IsDirectory = entry is RpfDirectoryEntry, Size = file?.GetFileSize() ?? 0, Encrypted = file?.IsEncrypted ?? false, ResourceType = TypeOf(entry.Name), Sha256 = data == null ? null : Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() }; }
    public static string TypeOf(string name) => Path.GetExtension(name).TrimStart('.').ToLowerInvariant() switch { "xml" => "xml", "meta" => "meta", "ytd" => "texture-dictionary", "yft" => "fragment-model", "ydd" => "drawable-dictionary", "ydr" => "drawable", "ymap" => "map", "ytyp" => "archetype", "ybn" => "collision", _ => "binary" };
    private static string Normalize(string value) => value.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
    private static bool PathMatches(string candidate, string query) => candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase) || candidate.IndexOf("\\" + query, StringComparison.OrdinalIgnoreCase) >= 0;
}
