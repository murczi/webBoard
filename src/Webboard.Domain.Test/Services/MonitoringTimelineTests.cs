namespace Webboard.Domain.Test.Services;
using Webboard.Domain.Services;
using Webboard.Domain.Model.Monitoring;
using Webboard.Domain.Model.Modules;
public sealed class MonitoringTimelineTests {
    private readonly DateTimeOffset start = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
    [Fact]
    public void MissingDataIsUnknownAndConsecutiveHealthySamplesMerge() {
        MonitoringPoint[] points = [new(start.AddSeconds(-10), start.AddSeconds(20), ModuleHealthState.Healthy, "OK"),
            new(start.AddSeconds(10), start.AddSeconds(40), ModuleHealthState.Healthy, "OK"),
            new(start.AddSeconds(60), start.AddSeconds(90), ModuleHealthState.Unhealthy, "timeout")];
        var blocks = MonitoringTimeline.Build(points, start, start.AddSeconds(100));
        Assert.Equal(new[] { ModuleHealthState.Healthy, ModuleHealthState.Unknown, ModuleHealthState.Unhealthy, ModuleHealthState.Unknown }, blocks.Select(x => x.State));
        Assert.Equal(40, (blocks[0].End - blocks[0].Start).TotalSeconds);
        Assert.Equal(100, blocks.Sum(x => (x.End - x.Start).TotalSeconds));
    }
    [Fact]
    public void ExpiredPredecessorDoesNotExtendHealth() {
        var blocks = MonitoringTimeline.Build([new(start.AddHours(-1), start.AddMinutes(-59), ModuleHealthState.Healthy, "OK")], start, start.AddHours(1));
        Assert.Equal(ModuleHealthState.Unknown, Assert.Single(blocks).State);
    }
    [Fact]
    public void NotConfiguredRemainsDistinct() {
        var blocks = MonitoringTimeline.Build([new(start, start.AddMinutes(1), ModuleHealthState.NotConfigured, "Missing URL")], start, start.AddMinutes(2));
        Assert.Equal(ModuleHealthState.NotConfigured, blocks[0].State); Assert.Equal(ModuleHealthState.Unknown, blocks[1].State);
    }
}
