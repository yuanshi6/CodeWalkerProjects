using CodeWalker.Agent.Abstractions;
using CodeWalker.GameFiles;

namespace CodeWalker.Agent.Core;

public sealed class PedInstallationService : IPedInstallationService
{
    private readonly IModPackageService _packages;
    public PedInstallationService(IModPackageService packages) => _packages = packages;
    public async Task<PedAnalysis> AnalyzeAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var mod = await _packages.InspectAsync(sourcePath, cancellationToken); var result = new PedAnalysis { ModType = mod.ModType, Confidence = mod.Confidence, ModelName = mod.ModelName, Files = mod.Files, MissingFiles = mod.MissingFiles, Conflicts = mod.Conflicts, SuggestedTarget = mod.SuggestedTarget, RequiresDlclistRegistration = mod.RequiresDlclistRegistration };
        result.HasYdd = Has(".ydd"); result.HasYft = Has(".yft"); result.HasYmt = Has(".ymt"); result.HasYtd = Has(".ytd");
        var names = result.Files.Where(f => new[] { ".ydd", ".yft", ".ymt", ".ytd" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.NamesConsistent = names.Count <= 1; if (!result.NamesConsistent) result.Conflicts.Add("Ped core files do not share a common model name."); return result;
        bool Has(string extension) => result.Files.Any(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }
    public async Task<PedBuildResult> BuildAddonAsync(string sourcePath, string addonName, CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeAsync(sourcePath, cancellationToken);
        if (analysis.ModType != "addon-ped" || !analysis.NamesConsistent || !(analysis.HasYdd && analysis.HasYft && analysis.HasYmt && analysis.HasYtd)) throw new InvalidOperationException("PED_INVALID: the package must contain consistent .ydd, .yft, .ymt, and .ytd files.");
        if (string.IsNullOrWhiteSpace(addonName) || addonName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || addonName.Contains('/') || addonName.Contains('\\')) throw new InvalidOperationException("PED_INVALID: invalid add-on name.");
        if (!Directory.Exists(sourcePath)) throw new InvalidOperationException("PED_INVALID: building is currently supported for unpacked Ped directories only.");
        var workspace = Path.Combine(Path.GetTempPath(), "CodeWalker.Agent", "peds", addonName + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
        var dlcPath = Path.Combine(workspace, "dlc.rpf"); var dlc = RpfFile.CreateNew(workspace, "dlc.rpf", RpfEncryption.OPEN); var x64 = RpfReadService.EnsureDirectory(dlc.Root, "x64\\models\\cdimages"); var modelRpf = RpfFile.CreateNew(x64, $"{addonName}.rpf", RpfEncryption.OPEN);
        var result = new PedBuildResult { AddonName = addonName, WorkspacePath = workspace, DlcRpfPath = dlcPath };
        foreach (var extension in new[] { ".ydd", ".yft", ".ymt", ".ytd" }) { var source = Directory.EnumerateFiles(sourcePath, "*" + extension, SearchOption.AllDirectories).First(); var data = File.ReadAllBytes(source); RpfFile.CreateFile(modelRpf.Root, Path.GetFileName(source), data, true); result.IncludedFiles.Add(Path.GetFileName(source)); }
        RpfFile.CreateFile(dlc.Root, "content.xml", System.Text.Encoding.UTF8.GetBytes($"<CDataFileMgr__ContentsOfDataFileXml><dataFiles/></CDataFileMgr__ContentsOfDataFileXml>"), true);
        RpfFile.CreateFile(dlc.Root, "setup2.xml", System.Text.Encoding.UTF8.GetBytes($"<SSetupData><name>{addonName}</name></SSetupData>"), true);
        return result;
    }
}
