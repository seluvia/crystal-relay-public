using System.IO;
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class DevCommandServiceTests
{
    [Fact]
    public void BuildGrow_FormatsInvariant()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm grow 0.25 30 1", svc.BuildGrow(0.25, 30, 1));
    }

    [Fact]
    public void BuildShrink_FormatsInvariant()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm shrink 0.5 10 0", svc.BuildShrink(0.5, 10, 0));
    }

    [Fact]
    public void BuildScalerandom_FormatsRange()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm scalerandom 0.8-2 20", svc.BuildScaleRandom(0.8, 2.0, 20));
    }

    [Fact]
    public void BuildMove_FormatsDirectionSeconds()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm move forward 5", svc.BuildMove("forward", 5));
        Assert.Equal("!screm move spinleft 12", svc.BuildMove("spinleft", 12));
    }

    [Fact]
    public void BuildMoverandom_FormatsSeconds()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm moverandom 12", svc.BuildMoveRandom(12));
    }

    [Fact]
    public void BuildSnapLeft_FormatsSeconds()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm move snapleft 10", svc.BuildSnapLeft(10));
    }

    [Fact]
    public void BuildSnapRight_FormatsSeconds()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm move snapright 15", svc.BuildSnapRight(15));
    }

    [Fact]
    public void BuildFiresale_FormatsPercentSeconds()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm firesale 25 120", svc.BuildFireSale(25, 120));
    }

    [Fact]
    public void CopyHistory_CappedAndOrderedNewestFirst()
    {
        var svc = new DevCommandService(historyCapacity: 3);
        svc.RecordCopy("!screm grow 0.25 30 1");
        svc.RecordCopy("!screm move forward 5");
        svc.RecordCopy("!screm firesale 25 120");
        svc.RecordCopy("!screm moverandom 12");

        var history = svc.CopyHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal("!screm moverandom 12", history[0]);
    }

    [Fact]
    public void Presets_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "presets.json");
            var svc = new DevCommandService(presetsPath: path);
            svc.SavePreset("warmup", "!screm grow 0.25 30 1");
            svc.SavePreset("warmup", "!screm grow 0.25 30 2");

            var loaded = new DevCommandService(presetsPath: path).LoadPresets();
            Assert.Single(loaded);
            Assert.Equal("!screm grow 0.25 30 2", loaded["warmup"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
