using CodeWalker.Agent.Abstractions;
using CodeWalker.Agent.Core;
using CodeWalker.Agent.Security;
using CodeWalker.Agent.Storage;
using System.Text.Json;

var (settings, configDirectory) = AgentSettingsLoader.Load();
var games = new GameInstallationService(settings); var packages = new ModPackageService(); var peds = new PedInstallationService(packages); var operations = new OperationService(new OperationStore(settings, configDirectory), settings, configDirectory, new PathPolicy(), new ProcessGuard(), new ArchiveLockManager(), games);
if (args.Length == 0) { Console.Error.WriteLine("Usage: gta-agent scan [path] | mod inspect <path> | ped analyze <path> | operation list"); return 2; }
object result = args[0].ToLowerInvariant() switch { "scan" => games.Scan(args.ElementAtOrDefault(1)), "mod" when args.ElementAtOrDefault(1) == "inspect" => await packages.InspectAsync(args.ElementAtOrDefault(2) ?? throw new ArgumentException("path required")), "ped" when args.ElementAtOrDefault(1) == "analyze" => await peds.AnalyzeAsync(args.ElementAtOrDefault(2) ?? throw new ArgumentException("path required")), "operation" when args.ElementAtOrDefault(1) == "list" => await operations.ListAsync(0, 50), _ => throw new ArgumentException("Unknown command.") };
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return 0;
