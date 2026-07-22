using CodeWalker.Agent.Abstractions;
using System.Text.Json;

namespace CodeWalker.Agent.Core;

public static class AgentSettingsLoader
{
    public static (AgentSettings Settings, string ConfigDirectory) Load(string? configPath = null)
    {
        configPath ??= Path.Combine(AppContext.BaseDirectory, "config", "agentsettings.json");
        var full = Path.GetFullPath(configPath); var directory = Path.GetDirectoryName(full)!;
        AgentSettings settings = File.Exists(full)
            ? JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(full), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AgentSettings()
            : new AgentSettings();
        return (settings, directory);
    }
}
