using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FolderDeck.App.Views;


internal readonly record struct GridCell(int Row, int Column);


internal readonly record struct GridIndexRange(int FirstIndex, int LastIndex)
{
    public static GridIndexRange Empty => new(0, -1);
    public bool IsEmpty => LastIndex < FirstIndex;
    public int Count => IsEmpty ? 0 : LastIndex - FirstIndex + 1;
}




internal static class VirtualizingWrapLayout
{
    public static int ColumnCount(double viewportWidth, double itemWidth)
    {
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0
            || !double.IsFinite(itemWidth) || itemWidth <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Min(int.MaxValue, Math.Floor(viewportWidth / itemWidth)));
    }

    public static int RowCount(int itemCount, int columns)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        columns = Math.Max(1, columns);
        return 1 + ((itemCount - 1) / columns);
    }

    public static GridIndexRange VisibleRange(
        int itemCount, int columns, double verticalOffset, double viewportHeight, double itemHeight)
    {
        if (itemCount <= 0)
        {
            return GridIndexRange.Empty;
        }

        columns = Math.Max(1, columns);
        var totalRows = RowCount(itemCount, columns);
        var offset = double.IsFinite(verticalOffset) ? Math.Max(0, verticalOffset) : 0;
        var viewportRows = double.IsFinite(viewportHeight) && viewportHeight > 0
            && double.IsFinite(itemHeight) && itemHeight > 0
                ? Math.Max(1, viewportHeight / itemHeight)
                : 1;

        var firstRow = Math.Min(totalRows - 1, (int)Math.Min(int.MaxValue, Math.Floor(offset)));
        var lastRowValue = Math.Ceiling(offset + viewportRows) - 1;
        var lastRow = Math.Min(totalRows - 1, (int)Math.Min(int.MaxValue, lastRowValue));
        var firstIndex = firstRow * columns;
        var lastIndex = Math.Min(itemCount - 1, ((lastRow + 1) * columns) - 1);

        return new GridIndexRange(firstIndex, lastIndex);
    }

    public static GridCell CellFromIndex(int index, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        return new GridCell(index / columns, index % columns);
    }

    public static int IndexFromCell(int row, int column, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, columns);
        return checked((row * columns) + column);
    }

    public static double OffsetToRevealRow(int row, double currentOffset, double viewportRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        var offset = double.IsFinite(currentOffset) ? Math.Max(0, currentOffset) : 0;
        var viewport = double.IsFinite(viewportRows) ? Math.Max(1, viewportRows) : 1;

        if (row < offset)
        {
            return row;
        }

        return row + 1 > offset + viewport ? row + 1 - viewport : offset;
    }
}




public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public const double DefaultItemWidth = 120;
    public const double DefaultItemHeight = 120;

    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(
            DefaultItemWidth,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsValidItemSize);

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(
            DefaultItemHeight,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsValidItemSize);

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private static bool IsValidItemSize(object value) =>
        value is double size && double.IsFinite(size) && size > 0;

    private Size _extent;
    private Size _viewport;
    private Size _pixelViewport = new(DefaultItemWidth, DefaultItemHeight);
    private Point _offset;
    private int _columns = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        var viewportWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ItemWidth;
        var viewportHeight = double.IsFinite(availableSize.Height) ? availableSize.Height : ItemHeight;
        _pixelViewport = new Size(Math.Max(0, viewportWidth), Math.Max(0, viewportHeight));
        _columns = VirtualizingWrapLayout.ColumnCount(viewportWidth, ItemWidth);
        var totalRows = VirtualizingWrapLayout.RowCount(itemCount, _columns);
        var viewportRows = Math.Max(1, viewportHeight / ItemHeight);

        UpdateScrollInfo(new Size(_columns, totalRows), new Size(_columns, viewportRows));

        var range = VirtualizingWrapLayout.VisibleRange(
            itemCount, _columns, _offset.Y, viewportHeight, ItemHeight);
        if (range.IsEmpty)
        {
            RemoveAllChildren();
            return _pixelViewport;
        }

        RemoveChildrenOutside(range.FirstIndex, range.LastIndex);
        RealizeChildren(range.FirstIndex, range.LastIndex);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(ItemWidth, ItemHeight));
        }

        return _pixelViewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        _columns = VirtualizingWrapLayout.ColumnCount(finalSize.Width, ItemWidth);
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(
                new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0 || itemIndex >= itemCount)
            {
                continue;
            }

            var cell = VirtualizingWrapLayout.CellFromIndex(itemIndex, _columns);
            InternalChildren[childIndex].Arrange(new Rect(
                cell.Column * ItemWidth,
                (cell.Row - _offset.Y) * ItemHeight,
                ItemWidth,
                ItemHeight));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);

        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                RemoveChangedChildren(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Move:
                RemoveChangedChildren(args.OldPosition.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                if (InternalChildren.Count > 0)
                {
                    RemoveInternalChildRange(0, InternalChildren.Count);
                }
                break;
        }

        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    private void RemoveChangedChildren(int index, int count)
    {
        if (index < 0 || count <= 0 || index >= InternalChildren.Count)
        {
            return;
        }

        RemoveInternalChildRange(index, Math.Min(count, InternalChildren.Count - index));
    }

    private void RealizeChildren(int firstIndex, int lastIndex)
    {
        var startPosition = ItemContainerGenerator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using var generator = ItemContainerGenerator.StartAt(
            startPosition, GeneratorDirection.Forward, allowStartAtRealizedItem: true);

        for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
        {
            var child = (UIElement)ItemContainerGenerator.GenerateNext(out var isNewlyRealized);
            var isAttached = ReferenceEquals(VisualTreeHelper.GetParent(child), this);
            if (!isNewlyRealized && isAttached)
            {
                continue;
            }

            if (!isAttached && childIndex >= InternalChildren.Count)
            {
                AddInternalChild(child);
            }
            else if (!isAttached)
            {
                InsertInternalChild(childIndex, child);
            }



            ItemContainerGenerator.PrepareItemContainer(child);
        }
    }

    private void RemoveChildrenOutside(int firstIndex, int lastIndex)
    {
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex)
            {
                continue;
            }

            RemoveGeneratedChild(position, childIndex);
        }
    }

    private void RemoveAllChildren()
    {
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            RemoveGeneratedChild(new GeneratorPosition(childIndex, 0), childIndex);
        }
    }

    private void RemoveGeneratedChild(GeneratorPosition position, int childIndex)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var recycling = owner is not null
            && GetVirtualizationMode(owner) == VirtualizationMode.Recycling
            && ItemContainerGenerator is IRecyclingItemContainerGenerator;

        if (recycling)
        {
            ((IRecyclingItemContainerGenerator)ItemContainerGenerator).Recycle(position, 1);
        }
        else
        {
            ItemContainerGenerator.Remove(position, 1);
        }

        RemoveInternalChildRange(childIndex, 1);
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        var changed = extent != _extent || viewport != _viewport;
        _extent = extent;
        _viewport = viewport;
        SetVerticalOffset(_offset.Y);

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(VerticalOffset - 1);
    public void LineDown() => SetVerticalOffset(VerticalOffset + 1);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 3);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 3);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() { }
    public void PageRight() { }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is null || rectangle.IsEmpty || visual == this || !IsAncestorOf(visual))
        {
            return Rect.Empty;
        }

        var transformed = visual.TransformToAncestor(this).TransformBounds(rectangle);
        var childIndex = DirectChildIndex(visual);
        if (childIndex < 0)
        {
            return transformed;
        }

        var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(
            new GeneratorPosition(childIndex, 0));
        if (itemIndex < 0)
        {
            return transformed;
        }

        var oldOffset = VerticalOffset;
        var row = VirtualizingWrapLayout.CellFromIndex(itemIndex, _columns).Row;
        SetVerticalOffset(VirtualizingWrapLayout.OffsetToRevealRow(row, oldOffset, ViewportHeight));

        var visible = new Rect(0, 0, _pixelViewport.Width, _pixelViewport.Height);
        var afterScroll = new Rect(
            transformed.X,
            transformed.Y + ((oldOffset - VerticalOffset) * ItemHeight),
            transformed.Width,
            transformed.Height);
        afterScroll.Intersect(visible);
        return afterScroll;
    }

    private int DirectChildIndex(Visual visual)
    {
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            if (ReferenceEquals(child, visual) || child.IsAncestorOf(visual))
            {
                return index;
            }
        }

        return -1;
    }

    public void SetHorizontalOffset(double offset)
    {
        if (_offset.X.Equals(0))
        {
            return;
        }

        _offset.X = 0;
        InvalidateArrange();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetVerticalOffset(double offset)
    {
        var maximum = Math.Max(0, ExtentHeight - ViewportHeight);
        var finite = double.IsNaN(offset) ? 0 : offset;
        var coerced = Math.Max(0, Math.Min(finite, maximum));
        if (coerced.Equals(_offset.Y))
        {
            return;
        }

        _offset.Y = coerced;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }
}
