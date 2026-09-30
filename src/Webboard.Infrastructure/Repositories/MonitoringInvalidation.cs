namespace Webboard.Infrastructure.Repositories;
using Configuration;
using Domain.Model.Modules;
using Microsoft.EntityFrameworkCore;
public static class MonitoringInvalidation {
    public static async Task LockAsync(WebboardDbContext db, IEnumerable<int> moduleIds, CancellationToken token) {
        foreach (var id in moduleIds.Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(70111, {id})", token);
    }
    public static async Task InvalidateAsync(WebboardDbContext db, IEnumerable<int> moduleIds, CancellationToken token) {
        var ids = moduleIds.ToArray();
        await db.LatestMonitoring.Where(x => ids.Contains(x.ModuleId)).ExecuteDeleteAsync(token);
        var now = DateTimeOffset.UtcNow;
        foreach (var id in ids) db.MonitoringResults.Add(new() {
            ModuleId = id, Timestamp = now, FreshUntil = now, ConfigurationRevision = now,
            State = ModuleHealthState.Unknown, Message = "Monitoring configuration changed"
        });
    }
}
