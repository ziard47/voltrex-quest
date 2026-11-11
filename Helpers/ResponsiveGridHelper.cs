using Microsoft.UI.Xaml.Controls;
using System;

namespace voltrex_quest.Helpers;

/// <summary>
/// Keeps GridView cards laid out cleanly at 4 columns per row, stretched across the full available width.
/// </summary>
public static class ResponsiveGridHelper
{
    public static void UpdateFourColumnGrid(GridView gridView, double rightScrollbarReserve = 16.0)
    {
        if (gridView.ItemsPanelRoot is not ItemsWrapGrid wrapGrid)
        {
            return;
        }

        var available = gridView.ActualWidth - gridView.Padding.Left - gridView.Padding.Right - rightScrollbarReserve;
        if (available <= 0)
        {
            return;
        }

        var columns = available >= 640 ? 4 : Math.Max(1, (int)Math.Floor(available / 200.0));
        var slotWidth = Math.Floor(available / columns);

        var changed = false;
        if (wrapGrid.MaximumRowsOrColumns != columns)
        {
            wrapGrid.MaximumRowsOrColumns = columns;
            changed = true;
        }

        if (slotWidth > 0 && (double.IsNaN(wrapGrid.ItemWidth) || Math.Abs(wrapGrid.ItemWidth - slotWidth) > 0.5))
        {
            wrapGrid.ItemWidth = slotWidth;
            changed = true;
        }

        if (changed)
        {
            wrapGrid.InvalidateMeasure();
        }
    }

    public static void ScheduleFourColumnGridUpdate(GridView gridView)
    {
        UpdateFourColumnGrid(gridView);
        gridView.DispatcherQueue?.TryEnqueue(() => UpdateFourColumnGrid(gridView));
    }
}
