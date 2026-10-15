using VeyonSync.Core;
using VeyonSync.Student;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("O serviço Aluno requer Windows.");
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "VeyonSyncStudent");
builder.Services.AddSingleton<AgentRuntime>();
builder.Services.AddHostedService<Worker>(); builder.Services.AddHostedService<Administration>();
await builder.Build().RunAsync();
