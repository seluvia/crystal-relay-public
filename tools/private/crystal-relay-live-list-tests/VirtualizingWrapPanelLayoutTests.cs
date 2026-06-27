using System.Windows;
using CrystalRelayLiveList.Controls;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class VirtualizingWrapPanelLayoutTests
{
    [Fact]
    public void ComputeRows_WrapsWhenItemExceedsAvailableWidth()
    {
        var sizes = new[] { new Size(100, 50), new Size(100, 50), new Size(100, 50) };
        var rows = VirtualizingWrapPanel.ComputeRows(sizes, itemWidth: 100, availableWidth: 250, spacing: 10);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);
        Assert.Single(rows[1]);
    }

    [Fact]
    public void ComputeRows_SingleItemFits()
    {
        var rows = VirtualizingWrapPanel.ComputeRows(new[] { new Size(100, 50) }, 100, 250, 10);
        Assert.Single(rows);
        Assert.Single(rows[0]);
    }

    [Fact]
    public void ComputeRowOffset_SumsRowHeightsWithSpacing()
    {
        var rows = new IReadOnlyList<Size>[]
        {
            new[] { new Size(100, 50) },
            new[] { new Size(100, 70) }
        };
        var (y0, h0) = VirtualizingWrapPanel.ComputeRowOffset(rows, 0, spacing: 10);
        var (y1, h1) = VirtualizingWrapPanel.ComputeRowOffset(rows, 1, spacing: 10);
        Assert.Equal(0, y0);
        Assert.Equal(50, h0);
        Assert.Equal(60, y1);
        Assert.Equal(70, h1);
    }
}
