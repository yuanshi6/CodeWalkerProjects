using CodeWalker.Agent.Abstractions;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace CodeWalker.Agent.Core;

public sealed class ModPackageService : IModPackageService
{
    public Task<ModAnalysis> InspectAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath)) throw new FileNotFoundException("Mod package was not found.", sourcePath);
        var files = Directory.Exists(sourcePath) ? Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(sourcePath, f)).ToList() : ReadArchive(sourcePath);
        var lower = files.Select(x => x.Replace('\\', '/').ToLowerInvariant()).ToList(); var model = FindModelName(lower);
        var analysis = new ModAnalysis { Files = files, ModelName = model };
        if (Path.GetExtension(sourcePath).Equals(".oiv", StringComparison.OrdinalIgnoreCase) || lower.Any(x => x.EndsWith("assembly.xml"))) { analysis.ModType = "oiv"; analysis.Confidence = .99; }
        else if (HasPed(lower)) { analysis.ModType = "addon-ped"; analysis.Confidence = .96; analysis.RequiresDlclistRegistration = true; analysis.SuggestedTarget = model == null ? null : $"mods/update/x64/dlcpacks/agent_{model}"; foreach (var ext in new[] { ".ydd", ".yft", ".ymt", ".ytd" }) if (!lower.Any(x => x.EndsWith(ext))) analysis.MissingFiles.Add(ext.TrimStart('.')); }
        else if (lower.Any(x => x.EndsWith(".asi"))) { analysis.ModType = "asi"; analysis.Confidence = .9; }
        else if (lower.Any(x => x.EndsWith(".ymap") || x.EndsWith(".ytyp"))) { analysis.ModType = "map"; analysis.Confidence = .85; }
        else { analysis.ModType = "unknown"; analysis.Confidence = .1; }
        return Task.FromResult(analysis);
    }
    private static List<string> ReadArchive(string path)
    {
        if (Path.GetExtension(path).Equals(".rpf", StringComparison.OrdinalIgnoreCase)) return new List<string> { Path.GetFileName(path) };
        using var archive = ArchiveFactory.OpenArchive(path, new ReaderOptions()); return archive.Entries.Where(e => !e.IsDirectory).Select(e => e.Key ?? "").ToList();
    }
    private static bool HasPed(List<string> files) => files.Any(x => x.EndsWith(".ydd")) && files.Any(x => x.EndsWith(".yft"));
    private static string? FindModelName(List<string> files) => files.Select(x => Path.GetFileNameWithoutExtension(x)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("content", StringComparison.OrdinalIgnoreCase));
}
