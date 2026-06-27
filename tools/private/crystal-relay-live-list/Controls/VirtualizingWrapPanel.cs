using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CrystalRelayLiveList.Controls;

public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    private Size extent = new(double.PositiveInfinity, 0);
    private Size viewport;
    private double verticalOffset;
    private Size[] itemSizes = Array.Empty<Size>();
    private List<List<int>> rows = new();

    public double ItemWidth { get; set; } = 284;
    public double ItemHeight { get; set; } = 178;
    public double Spacing { get; set; } = 14;

    public static List<List<int>> ComputeRows(IReadOnlyList<Size> sizes, double itemWidth, double availableWidth, double spacing)
    {
        var rows = new List<List<int>>();
        var current = new List<int>();
        var used = 0d;
        for (var i = 0; i < sizes.Count; i++)
        {
            var w = itemWidth;
            if (used + w > availableWidth && current.Count > 0)
            {
                rows.Add(current);
                current = new List<int>();
                used = 0;
            }
            current.Add(i);
            used += w + spacing;
        }
        if (current.Count > 0)
        {
            rows.Add(current);
        }
        return rows;
    }

    public static (double Y, double Height) ComputeRowOffset(IReadOnlyList<IReadOnlyList<Size>> rows, int rowIndex, double spacing)
    {
        var y = 0d;
        for (var i = 0; i < rowIndex; i++)
        {
            var h = 0d;
            foreach (var s in rows[i])
            {
                if (s.Height > h) h = s.Height;
            }
            y += h + spacing;
        }
        var rowHeight = 0d;
        foreach (var s in rows[rowIndex])
        {
            if (s.Height > rowHeight) rowHeight = s.Height;
        }
        return (y, rowHeight);
    }

    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => extent.Width;
    public double ExtentHeight => extent.Height;
    public double ViewportWidth => viewport.Width;
    public double ViewportHeight => viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => verticalOffset;
    public ScrollViewer? ScrollOwner { get; set; }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    public void SetVerticalOffset(double value)
    {
        var clamped = Math.Max(0, Math.Min(value, Math.Max(0, extent.Height - viewport.Height)));
        if (clamped == verticalOffset) return;
        verticalOffset = clamped;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetHorizontalOffset(double value) { }
    public void LineUp() => SetVerticalOffset(verticalOffset - 16);
    public void LineDown() => SetVerticalOffset(verticalOffset + 16);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelUp() => LineUp();
    public void MouseWheelDown() => LineDown();
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageUp() => SetVerticalOffset(verticalOffset - viewport.Height);
    public void PageDown() => SetVerticalOffset(verticalOffset + viewport.Height);
    public void PageLeft() { }
    public void PageRight() { }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        viewport = availableSize;
        var generator = ItemContainerGenerator;
        var owner = ItemsControl.GetItemsOwner(this);
        var count = owner?.Items.Count ?? 0;
        if (generator is null || count == 0)
        {
            extent = new Size(0, 0);
            ScrollOwner?.InvalidateScrollInfo();
            return new Size(0, 0);
        }

        itemSizes = new Size[count];
        for (var i = 0; i < count; i++)
        {
            itemSizes[i] = new Size(ItemWidth, ItemHeight);
        }

        // Use the constrained available width (or a sensible fallback) for row wrapping.
        var availableWidth = double.IsInfinity(availableSize.Width) ? ItemWidth * 4 + Spacing * 3 : availableSize.Width;
        rows = ComputeRows(itemSizes, ItemWidth, availableWidth, Spacing);

        double totalHeight = 0;
        foreach (var row in rows)
        {
            var h = 0d;
            foreach (var idx in row)
            {
                if (itemSizes[idx].Height > h) h = itemSizes[idx].Height;
            }
            totalHeight += h + Spacing;
        }
        var contentHeight = Math.Max(0, totalHeight - Spacing);
        extent = new Size(availableWidth, contentHeight);

        // The desired size of the panel itself must never be infinite.
        var desiredWidth = Math.Min(availableWidth, ItemWidth + Spacing);
        var desiredHeight = double.IsInfinity(availableSize.Height)
            ? contentHeight
            : Math.Min(contentHeight, availableSize.Height);

        var topY = 0d;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var rowHeight = 0d;
            foreach (var idx in row)
            {
                if (itemSizes[idx].Height > rowHeight) rowHeight = itemSizes[idx].Height;
            }
            var rowBottom = topY + rowHeight;
            if (rowBottom >= verticalOffset && topY <= verticalOffset + viewport.Height)
            {
                foreach (var idx in row)
                {
                    var container = ((System.Windows.Controls.ItemContainerGenerator)generator).ContainerFromIndex(idx) as UIElement;
                    if (container is null)
                    {
                        var pos = generator.GeneratorPositionFromIndex(idx);
                        var newlyRealized = false;
                        var ui = generator.GenerateNext(out newlyRealized) as UIElement;
                        if (ui is not null)
                        {
                            if (newlyRealized)
                            {
                                var insertIndex = (pos.Index >= 0 ? pos.Index : 0) + (pos.Offset > 0 ? 1 : 0);
                                InsertInternalChild(insertIndex, ui);
                                generator.PrepareItemContainer(ui);
                            }
                            container = ui;
                        }
                    }
                    container?.Measure(new Size(ItemWidth, ItemHeight));
                }
            }
            topY += rowHeight + Spacing;
        }

        // Recycle off-screen containers.
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            if (InternalChildren[i] is not UIElement child) continue;
            var genPos = generator.GeneratorPositionFromIndex(i);
            var index = (genPos.Index >= 0 ? genPos.Index : 0) + (genPos.Offset > 0 ? 1 : 0);
            var rowOf = RowContainingIndex(index);
            if (rowOf < 0) continue;
            var rowSizes = rows.Select(r => (IReadOnlyList<Size>)r.Select(idx => itemSizes[idx]).ToList()).ToList();
            var (y, h) = ComputeRowOffset(rowSizes, rowOf, Spacing);
            if (y + h < verticalOffset || y > verticalOffset + viewport.Height)
            {
                RemoveInternalChildRange(i, 1);
                ((IRecyclingItemContainerGenerator)generator).Recycle(genPos, 1);
            }
        }

        ScrollOwner?.InvalidateScrollInfo();
        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        if (generator is null) return finalSize;

        double topY = 0;
        foreach (var row in rows)
        {
            var rowHeight = 0d;
            foreach (var idx in row)
            {
                if (itemSizes[idx].Height > rowHeight) rowHeight = itemSizes[idx].Height;
            }
            if (topY + rowHeight >= verticalOffset && topY <= verticalOffset + viewport.Height)
            {
                double x = 0;
                foreach (var idx in row)
                {
                    var container = ((System.Windows.Controls.ItemContainerGenerator)generator).ContainerFromIndex(idx) as UIElement;
                    if (container is not null)
                    {
                        container.Arrange(new Rect(x, topY - verticalOffset, ItemWidth, itemSizes[idx].Height));
                        x += ItemWidth + Spacing;
                    }
                }
            }
            topY += rowHeight + Spacing;
        }
        return finalSize;
    }

    private int RowContainingIndex(int index)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Contains(index)) return r;
        }
        return -1;
    }
}
