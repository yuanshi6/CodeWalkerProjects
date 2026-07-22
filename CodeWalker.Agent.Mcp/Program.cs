using CodeWalker.Agent.Abstractions;
using CodeWalker.Agent.Core;
using CodeWalker.Agent.Security;
using CodeWalker.Agent.Storage;
using CodeWalker.Agent.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var (settings, configDirectory) = AgentSettingsLoader.Load(ArgumentValue(args, "--config"));
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(settings).AddSingleton(new OperationStore(settings, configDirectory)).AddSingleton<PathPolicy>().AddSingleton<ProcessGuard>().AddSingleton<ArchiveLockManager>();
builder.Services.AddSingleton<IGameInstallationService, GameInstallationService>().AddSingleton<IRpfReadService, RpfReadService>().AddSingleton<IModPackageService, ModPackageService>().AddSingleton<IPedInstallationService, PedInstallationService>().AddSingleton<IOperationService>(sp => new OperationService(sp.GetRequiredService<OperationStore>(), settings, configDirectory, sp.GetRequiredService<PathPolicy>(), sp.GetRequiredService<ProcessGuard>(), sp.GetRequiredService<ArchiveLockManager>(), sp.GetRequiredService<IGameInstallationService>()));
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
var host = builder.Build();
AgentToolRuntime.Services = host.Services;
await host.RunAsync();

static string? ArgumentValue(string[] values, string name) { var index = Array.IndexOf(values, name); return index >= 0 && index + 1 < values.Length ? values[index + 1] : null; }
