namespace Webboard.Domain.Services;
using Model.Monitoring;
using Model.Modules;
public static class MonitoringTimeline {
    public static IReadOnlyList<MonitoringBlock> Build(IReadOnlyList<MonitoringPoint> points, DateTimeOffset from, DateTimeOffset until) {
        var blocks = new List<MonitoringBlock>();
        var cursor = from;
        for (var i = 0; i < points.Count; i++) {
            var point = points[i];
            var start = point.Timestamp < from ? from : point.Timestamp;
            var next = i + 1 < points.Count ? points[i + 1].Timestamp : until;
            var end = new[] { point.FreshUntil, next, until }.Min();
            if (start > cursor) Add(cursor, start < until ? start : until, ModuleHealthState.Unknown, "No monitoring data");
            if (end > start && start < until) { Add(start, end, point.State, point.Message); cursor = end; }
            else if (start > cursor) cursor = start;
        }
        if (cursor < until) Add(cursor, until, ModuleHealthState.Unknown, "No monitoring data");
        return blocks;
        void Add(DateTimeOffset start, DateTimeOffset end, ModuleHealthState state, string message) {
            if (end <= start) return;
            if (blocks.Count > 0 && blocks[^1].End == start && blocks[^1].State == state && blocks[^1].Message == message)
                blocks[^1] = blocks[^1] with { End = end };
            else blocks.Add(new(start, end, state, message));
        }
    }
}
