namespace Webboard.Infrastructure.Repositories;
using System.Text.Json;
using Configuration;
using Domain.Interfaces.Services;
using Domain.Model.Monitoring;
using Domain.Model.Modules;
using Domain.Services;
using Microsoft.EntityFrameworkCore;

public sealed class MonitoringReader(WebboardDbContext db, MonitoringOptions options) : IMonitoringReader {
    public async Task<IReadOnlyList<HistoryModule>> ModulesAsync(CancellationToken token) =>
        await db.Modules.AsNoTracking().Where(x => !x.DeletionFlag).OrderBy(x => x.FriendlyName)
            .Select(x => new HistoryModule(x.Id, x.FriendlyName, x.IsEnabled)).ToListAsync(token);
    public async Task<IReadOnlyList<MonitoringStatus>> LatestAsync(CancellationToken token) {
        var rows = await (from module in db.Modules.AsNoTracking()
                          where module.IsEnabled && !module.DeletionFlag
                          join latest in db.LatestMonitoring.AsNoTracking() on module.Id equals latest.ModuleId into results
                          from latest in results.DefaultIfEmpty()
                          select new { module.Id, Latest = latest }).ToListAsync(token);
        var now = DateTimeOffset.UtcNow;
        return rows.Select(row => {
            var latest = row.Latest;
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
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays);
        if (at < cutoff) return new(at, cutoff, ModuleHealthState.Unknown, "Outside retained history");
        var end = DateTimeOffset.UtcNow;
        if (at >= end) return new(end, at.AddSeconds(1), ModuleHealthState.Unknown, "No monitoring data");
        var spans = await db.Database.SqlQuery<HistorySpan>($"""
            WITH source AS (
                SELECT "Timestamp", "FreshUntil", "State", "Message", "Id",
                    LEAD("Timestamp", 1, {end}) OVER (ORDER BY "Timestamp", "Id") AS next
                FROM "MonitoringResults" WHERE "ModuleId" = {moduleId} AND "Timestamp" >= {cutoff} AND "Timestamp" < {end}
            ), observed AS (
                SELECT "Timestamp" AS "Start", LEAST("FreshUntil", next, {end}) AS "End", "State", "Message" FROM source
            ), spans AS (
                SELECT * FROM observed WHERE "End" > "Start"
                UNION ALL SELECT LEAST("FreshUntil", next), next, 3, 'No monitoring data' FROM source WHERE "FreshUntil" < next
                UNION ALL SELECT {cutoff}, COALESCE(MIN("Timestamp"), {end}), 3, 'No monitoring data' FROM source
            ), marked AS (
                SELECT *, CASE WHEN "Start" = LAG("End") OVER w AND "State" = LAG("State") OVER w
                    AND "Message" = LAG("Message") OVER w THEN 0 ELSE 1 END AS boundary
                FROM spans WHERE "End" > "Start" WINDOW w AS (ORDER BY "Start")
            ), grouped AS (
                SELECT *, SUM(boundary) OVER (ORDER BY "Start") AS island FROM marked
            ), blocks AS (
                SELECT MIN("Start") AS "Start", MAX("End") AS "End", "State", "Message"
                FROM grouped GROUP BY island, "State", "Message"
            ) SELECT * FROM blocks WHERE "Start" <= {at} AND "End" > {at} LIMIT 1
            """).ToListAsync(token);
        var span = spans.SingleOrDefault();
        return span is null ? new(at, end, ModuleHealthState.Unknown, "No monitoring data") : new(span.Start, span.End, (ModuleHealthState)span.State, span.Message);
    }
    public async Task<HistorySlice> HistoryAsync(int moduleId, DateTimeOffset from, DateTimeOffset until, CancellationToken token) {
        from = from.ToUniversalTime(); until = until.ToUniversalTime();
        if (until <= from || until - from > TimeSpan.FromDays(3660)) throw new ArgumentException("Select a valid time range of at most ten years.");
        if (!await db.Modules.AnyAsync(x => x.Id == moduleId && !x.DeletionFlag, token)) throw new KeyNotFoundException();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays);
        if (until <= cutoff) return new(moduleId, from, until, [new(from, until, ModuleHealthState.Unknown, "Outside retained history")], null);
        var sliceEnd = until < from.AddDays(1) ? until : from.AddDays(1);
        if (sliceEnd < cutoff && cutoff < until) sliceEnd = cutoff;
        if (from > DateTimeOffset.UtcNow) sliceEnd = until;
        var queryStart = from > cutoff ? from : cutoff;
        var spans = sliceEnd > queryStart ? await db.Database.SqlQuery<HistorySpan>($"""
            WITH source AS (
                (SELECT "Id", "Timestamp", "FreshUntil", "State", "Message" FROM "MonitoringResults"
                 WHERE "ModuleId" = {moduleId} AND "Timestamp" < {queryStart} AND "Timestamp" >= {cutoff}
                 ORDER BY "Timestamp" DESC, "Id" DESC LIMIT 1)
                UNION ALL
                SELECT "Id", "Timestamp", "FreshUntil", "State", "Message" FROM "MonitoringResults"
                WHERE "ModuleId" = {moduleId} AND "Timestamp" >= {queryStart} AND "Timestamp" < {sliceEnd}
            ), edges AS (
                SELECT GREATEST("Timestamp", {queryStart}) AS "Start",
                    LEAST("FreshUntil", LEAD("Timestamp", 1, {sliceEnd}) OVER (ORDER BY "Timestamp", "Id"), {sliceEnd}) AS "End",
                    "State", "Message" FROM source
            ), marked AS (
                SELECT *, CASE WHEN "Start" = LAG("End") OVER w AND "State" = LAG("State") OVER w
                     AND "Message" = LAG("Message") OVER w THEN 0 ELSE 1 END AS boundary
                FROM edges WHERE "End" > "Start" WINDOW w AS (ORDER BY "Start")
            ), grouped AS (
                SELECT *, SUM(boundary) OVER (ORDER BY "Start") AS island FROM marked
            ) SELECT MIN("Start") AS "Start", MAX("End") AS "End", "State", "Message"
                FROM grouped GROUP BY island, "State", "Message" ORDER BY "Start" LIMIT 2001
            """).ToListAsync(token) : [];
        if (spans.Count > 2000) { sliceEnd = spans[2000].Start; spans.RemoveAt(2000); }
        var points = spans.Select(x => new MonitoringPoint(x.Start, x.End, (ModuleHealthState)x.State, x.Message)).ToList();
        return new(moduleId, from, sliceEnd, MonitoringTimeline.Build(points, from, sliceEnd), sliceEnd < until ? sliceEnd : null);
    }
    public sealed class HistorySpan {
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public int State { get; set; }
        public string Message { get; set; } = "";
    }
}
