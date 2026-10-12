namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Model.Modules;
using Microsoft.EntityFrameworkCore;

public static class MonitoringBlockProjection {
    // Caller holds the module's transaction advisory lock, including configuration invalidation.
    public static async Task ObserveAsync(WebboardDbContext db, int moduleId, DateTimeOffset at,
        DateTimeOffset freshUntil, ModuleHealthState state, string message, CancellationToken token) {
        var last = await db.MonitoringBlocks.Where(x => x.ModuleId == moduleId).OrderByDescending(x => x.Start).ThenByDescending(x => x.Id).FirstOrDefaultAsync(token);
        if (last is not null && last.Start <= at && last.End >= at && last.State == state && last.Message == message && freshUntil > at) {
            last.End = freshUntil;
            return;
        }
        if (last is not null && last.End > at) {
            if (last.Start >= at) db.MonitoringBlocks.Remove(last);
            else last.End = at;
        }
        if (freshUntil > at) db.MonitoringBlocks.Add(new MonitoringBlockEntity {
            ModuleId = moduleId, Start = at, End = freshUntil, State = state, Message = message
        });
    }

    public static async Task<int> CutAsync(WebboardDbContext db, int[] ids, DateTimeOffset at, CancellationToken token) {
        await db.MonitoringBlocks.Where(x => ids.Contains(x.ModuleId) && x.Start >= at).ExecuteDeleteAsync(token);
        return await db.MonitoringBlocks.Where(x => ids.Contains(x.ModuleId) && x.End > at)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.End, at), token);
    }
}
