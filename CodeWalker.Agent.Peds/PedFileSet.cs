namespace CodeWalker.Agent.Peds;

public sealed class PedFileSet
{
    public string ModelName { get; }
    public IReadOnlyDictionary<string, string> Files { get; }
    private PedFileSet(string modelName, Dictionary<string, string> files) { ModelName = modelName; Files = files; }
    public static PedFileSet FromDirectory(string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(path => new[] { ".ydd", ".yft", ".ymt", ".ytd" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToDictionary(path => Path.GetExtension(path)!, StringComparer.OrdinalIgnoreCase);
        if (files.Count != 4) throw new InvalidOperationException("PED_INVALID: expected exactly .ydd, .yft, .ymt, and .ytd.");
        var names = files.Values.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); if (names.Count != 1) throw new InvalidOperationException("PED_INVALID: required Ped files must have the same base name."); return new PedFileSet(names[0]!, files);
    }
}
