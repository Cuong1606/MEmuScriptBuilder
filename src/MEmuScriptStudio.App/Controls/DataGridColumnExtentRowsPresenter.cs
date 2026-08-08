using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MEmuScriptStudio.App.Controls;

/// <summary>
/// Keeps a DataGrid's horizontal extent aligned with its visible columns when
/// there are no realized rows. The native rows presenter otherwise reports a
/// zero-width extent for an empty items collection.
/// </summary>
public sealed class DataGridColumnExtentRowsPresenter : DataGridRowsPresenter, IScrollInfo
{
    private const double HorizontalLineDelta = 16;
    private double columnExtentWidth;
    private double emptyHorizontalOffset;

    protected override Size MeasureOverride(Size constraint)
    {
        var desiredSize = base.MeasureOverride(constraint);
        if (ItemsControl.GetItemsOwner(this) is not DataGrid dataGrid)
            return desiredSize;

        var measuredColumnsWidth = dataGrid.Columns
            .Where(column => column.Visibility == Visibility.Visible)
            .Sum(column => column.ActualWidth);
        if (double.IsFinite(measuredColumnsWidth) &&
            !AreClose(columnExtentWidth, measuredColumnsWidth))
        {
            columnExtentWidth = measuredColumnsWidth;
            CoerceEmptyHorizontalOffset();
            ScrollOwner?.InvalidateScrollInfo();
        }

        return desiredSize;
    }

    bool IScrollInfo.CanHorizontallyScroll
    {
        get => CanHorizontallyScroll;
        set => CanHorizontallyScroll = value;
    }

    bool IScrollInfo.CanVerticallyScroll
    {
        get => CanVerticallyScroll;
        set => CanVerticallyScroll = value;
    }

    double IScrollInfo.ExtentWidth => Math.Max(ExtentWidth, columnExtentWidth);
    double IScrollInfo.ExtentHeight => ExtentHeight;
    double IScrollInfo.ViewportWidth => ViewportWidth;
    double IScrollInfo.ViewportHeight => ViewportHeight;
    double IScrollInfo.HorizontalOffset => UsesColumnOnlyExtent ? emptyHorizontalOffset : HorizontalOffset;
    double IScrollInfo.VerticalOffset => VerticalOffset;

    ScrollViewer IScrollInfo.ScrollOwner
    {
        get => ScrollOwner;
        set => ScrollOwner = value;
    }

    void IScrollInfo.LineLeft() => SetHorizontalOffsetCore(CurrentHorizontalOffset - HorizontalLineDelta);
    void IScrollInfo.LineRight() => SetHorizontalOffsetCore(CurrentHorizontalOffset + HorizontalLineDelta);
    void IScrollInfo.PageLeft() => SetHorizontalOffsetCore(CurrentHorizontalOffset - ViewportWidth);
    void IScrollInfo.PageRight() => SetHorizontalOffsetCore(CurrentHorizontalOffset + ViewportWidth);
    void IScrollInfo.MouseWheelLeft() => SetHorizontalOffsetCore(CurrentHorizontalOffset - (3 * HorizontalLineDelta));
    void IScrollInfo.MouseWheelRight() => SetHorizontalOffsetCore(CurrentHorizontalOffset + (3 * HorizontalLineDelta));
    void IScrollInfo.SetHorizontalOffset(double offset) => SetHorizontalOffsetCore(offset);

    void IScrollInfo.LineUp() => LineUp();
    void IScrollInfo.LineDown() => LineDown();
    void IScrollInfo.PageUp() => PageUp();
    void IScrollInfo.PageDown() => PageDown();
    void IScrollInfo.MouseWheelUp() => MouseWheelUp();
    void IScrollInfo.MouseWheelDown() => MouseWheelDown();
    void IScrollInfo.SetVerticalOffset(double offset) => SetVerticalOffset(offset);
    Rect IScrollInfo.MakeVisible(Visual visual, Rect rectangle) => MakeVisible(visual, rectangle);

    private bool UsesColumnOnlyExtent => ExtentWidth < columnExtentWidth && ExtentWidth <= ViewportWidth;
    private double CurrentHorizontalOffset => UsesColumnOnlyExtent ? emptyHorizontalOffset : HorizontalOffset;

    private void SetHorizontalOffsetCore(double offset)
    {
        if (!UsesColumnOnlyExtent)
        {
            SetHorizontalOffset(offset);
            return;
        }

        var coercedOffset = Math.Clamp(offset, 0, Math.Max(0, columnExtentWidth - ViewportWidth));
        if (AreClose(emptyHorizontalOffset, coercedOffset))
            return;

        emptyHorizontalOffset = coercedOffset;
        InvalidateArrange();
        ScrollOwner?.InvalidateScrollInfo();
    }

    private void CoerceEmptyHorizontalOffset()
    {
        emptyHorizontalOffset = Math.Clamp(
            emptyHorizontalOffset,
            0,
            Math.Max(0, columnExtentWidth - ViewportWidth));
    }

    private static bool AreClose(double first, double second) => Math.Abs(first - second) < 0.01;
}
