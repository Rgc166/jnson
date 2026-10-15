using VeyonSync.Core;

namespace VeyonSync.Teacher;

public sealed class Maintenance(Store store, TeacherConfig config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var lastPrune = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            store.UpdateCommunication();
            if (DateTimeOffset.UtcNow - lastPrune >= TimeSpan.FromDays(1))
            {
                store.Prune(config.RetentionDays);
                lastPrune = DateTimeOffset.UtcNow;
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
