using VeyonSync.Core;

namespace VeyonSync.Student;

public sealed class Worker(AgentRuntime runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do { await runtime.Tick(ct); } while (await timer.WaitForNextTickAsync(ct));
    }
}
public sealed class Administration(AgentRuntime runtime) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => LocalRpc.Serve("Student",
        async (request, token) => await runtime.Action(request.Action, request.Data, token), ct);
}
