using CodeWalker.Agent.Abstractions;

namespace CodeWalker.Agent.Core;

public sealed class GameInstallationService : IGameInstallationService
{
    private readonly AgentSettings _settings;
    public GameInstallationService(AgentSettings settings) => _settings = settings;
    public GameInstallationInfo Scan(string? gamePath = null)
    {
        gamePath = string.IsNullOrWhiteSpace(gamePath) ? PreferredPath() : gamePath;
        if (string.IsNullOrWhiteSpace(gamePath)) return new GameInstallationInfo();
        gamePath = Path.GetFullPath(gamePath);
        var enhanced = Path.Combine(gamePath, "GTA5_Enhanced.exe"); var legacy = Path.Combine(gamePath, "GTA5.exe"); var executable = File.Exists(enhanced) ? enhanced : legacy;
        return new GameInstallationInfo { GamePath = gamePath, Exists = Directory.Exists(gamePath) && File.Exists(executable), Edition = File.Exists(enhanced) || File.Exists(Path.Combine(gamePath, "eboot.bin")) ? GameEdition.Enhanced : File.Exists(legacy) ? GameEdition.Legacy : GameEdition.Unknown, ExecutablePath = executable, ModsExists = Directory.Exists(Path.Combine(gamePath, "mods")) };
    }
    public GameInstallationInfo GetStatus() => Scan();
    private string PreferredPath() => _settings.Game.PreferredEdition.Equals("enhanced", StringComparison.OrdinalIgnoreCase) ? _settings.Game.EnhancedPath : !string.IsNullOrWhiteSpace(_settings.Game.LegacyPath) ? _settings.Game.LegacyPath : _settings.Game.EnhancedPath;
}
