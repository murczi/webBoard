namespace Webboard.Infrastructure.Repositories;
using System.Text.Json;
using Configuration;
using Domain.Interfaces.Services;
using Domain.Model.Monitoring;
using Domain.Model.Modules;
using Domain.Services;
using Microsoft.EntityFrameworkCore;

public sealed class MonitoringReader(WebboardDbContext db, MonitoringOptions options) : IMonitoringReader {
    private readonly TimeProvider timeProvider = TimeProvider.System;
    public MonitoringReader(WebboardDbContext db, MonitoringOptions options, TimeProvider timeProvider) : this(db, options) => this.timeProvider = timeProvider;

    public async Task<IReadOnlyList<HistoryModule>> ModulesAsync(CancellationToken token) =>
        await db.Modules.AsNoTracking().Where(x => !x.DeletionFlag).OrderBy(x => x.FriendlyName)
            .Select(x => new HistoryModule(x.Id, x.FriendlyName, x.IsEnabled)).ToListAsync(token);
    public async Task<IReadOnlyList<MonitoringStatus>> LatestAsync(CancellationToken token) {
        var rows = await (from module in db.Modules.AsNoTracking()
                          where module.IsEnabled && !module.DeletionFlag
                          join latest in db.LatestMonitoring.AsNoTracking() on module.Id equals latest.ModuleId into results
                          from latest in results.DefaultIfEmpty()
                          select new { module.Id, module.HostId, HostExists = module.Host != null, HostEnabled = module.Host != null && module.Host.IsEnabled, HostDeleted = module.Host != null && module.Host.DeletionFlag, Latest = latest }).ToListAsync(token);
        var now = timeProvider.GetUtcNow();
        return rows.Select(row => {
            var latest = row.Latest;
            var unavailable = ModuleEligibility.UnavailableReason(true, row.HostId, row.HostExists, row.HostEnabled, row.HostDeleted);
            if (unavailable is not null) return new MonitoringStatus(row.Id, new(ModuleHealthState.Unknown, null, unavailable), latest?.Timestamp, false);
            if (latest is null) return new MonitoringStatus(row.Id, new(ModuleHealthState.Unknown, null, "Awaiting monitoring data"), null, false);
            var stale = latest.FreshUntil <= now;
            var health = new ModuleHealthResult(stale ? ModuleHealthState.Unknown : latest.State,
                latest.PingMilliseconds, stale ? $"Monitoring data is stale. Last observation: {latest.Message}" : latest.Message);
            if (!stale && latest.Details is not null) {
                try { health = health with { Steam = JsonSerializer.Deserialize<SteamServerInfo>(latest.Details) }; } catch (JsonException) { }
            }
            return new MonitoringStatus(row.Id, health, latest.Timestamp, stale);
        }).ToList();
    }
    public async Task<MonitoringBlock> BlockAsync(int moduleId, DateTimeOffset at, CancellationToken token) {
        if (!await db.Modules.AnyAsync(x => x.Id == moduleId && !x.DeletionFlag, token)) throw new KeyNotFoundException();
        at = at.ToUniversalTime();
        var cutoff = timeProvider.GetUtcNow().AddDays(-options.RetentionDays);
        if (at < cutoff) return new(at, cutoff, ModuleHealthState.Unknown, "Outside retained history");
        var end = timeProvider.GetUtcNow();
        if (at >= end) return new(end, at.AddSeconds(1), ModuleHealthState.Unknown, "No monitoring data");
        var previous = await db.MonitoringBlocks.AsNoTracking().Where(x => x.ModuleId == moduleId && x.Start <= at)
            .OrderByDescending(x => x.Start).ThenByDescending(x => x.Id).FirstOrDefaultAsync(token);
        if (previous is not null && previous.End > at) return new(previous.Start > cutoff ? previous.Start : cutoff,
            previous.End < end ? previous.End : end, previous.State, previous.Message);
        var next = await db.MonitoringBlocks.AsNoTracking().Where(x => x.ModuleId == moduleId && x.Start > at)
            .OrderBy(x => x.Start).Select(x => (DateTimeOffset?)x.Start).FirstOrDefaultAsync(token);
        var start = previous?.End ?? cutoff;
        return new(start > cutoff ? start : cutoff, next.HasValue && next < end ? next.Value : end,
            ModuleHealthState.Unknown, "No monitoring data");
    }
    public async Task<HistorySlice> HistoryAsync(int moduleId, DateTimeOffset from, DateTimeOffset until, CancellationToken token) {
        from = from.ToUniversalTime(); until = until.ToUniversalTime();
        if (until <= from || until - from > TimeSpan.FromDays(3660)) throw new ArgumentException("Select a valid time range of at most ten years.");
        if (!await db.Modules.AnyAsync(x => x.Id == moduleId && !x.DeletionFlag, token)) throw new KeyNotFoundException();
        var cutoff = timeProvider.GetUtcNow().AddDays(-options.RetentionDays);
        if (until <= cutoff) return new(moduleId, from, until, [new(from, until, ModuleHealthState.Unknown, "Outside retained history")], null);
        var sliceEnd = until < from.AddDays(1) ? until : from.AddDays(1);
        if (sliceEnd < cutoff && cutoff < until) sliceEnd = cutoff;
        if (from > timeProvider.GetUtcNow()) sliceEnd = until;
        var queryStart = from > cutoff ? from : cutoff;
        var spans = new List<HistorySpan>();
        if (sliceEnd > queryStart) {
            var predecessor = await db.MonitoringBlocks.AsNoTracking().Where(x => x.ModuleId == moduleId && x.Start < queryStart)
                .OrderByDescending(x => x.Start).ThenByDescending(x => x.Id).FirstOrDefaultAsync(token);
            var rows = await db.MonitoringBlocks.AsNoTracking().Where(x => x.ModuleId == moduleId && x.Start >= queryStart && x.Start < sliceEnd)
                .OrderBy(x => x.Start).ThenBy(x => x.Id).Take(2001).ToListAsync(token);
            if (predecessor is not null && predecessor.End > queryStart) rows.Insert(0, predecessor);
            spans = rows.Select(x => new HistorySpan { Start = x.Start > queryStart ? x.Start : queryStart,
                End = x.End < sliceEnd ? x.End : sliceEnd, State = (int)x.State, Message = x.Message }).ToList();
        }
        if (spans.Count > 2000) { sliceEnd = spans[2000].Start; spans.RemoveAt(2000); }
        var points = spans.Select(x => new MonitoringPoint(x.Start, x.End, (ModuleHealthState)x.State, x.Message)).ToList();
        var blocks = new List<MonitoringBlock>();
        if (from < cutoff) blocks.Add(new(from, cutoff < sliceEnd ? cutoff : sliceEnd, ModuleHealthState.Unknown, "Outside retained history"));
        var retainedStart = from > cutoff ? from : cutoff;
        if (retainedStart < sliceEnd) blocks.AddRange(MonitoringTimeline.Build(points, retainedStart, sliceEnd));
        return new(moduleId, from, sliceEnd, blocks, sliceEnd < until ? sliceEnd : null);
    }
    public sealed class HistorySpan {
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public int State { get; set; }
        public string Message { get; set; } = "";
    }
}
