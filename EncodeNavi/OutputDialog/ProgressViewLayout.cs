using System.Windows;
using System.Windows.Controls;
using YukkuriMovieMaker.Views;

namespace EncodeNavi.OutputDialog
{
    internal static class ProgressViewLayout
    {
        public static void Grow(ProgressView view, double added)
        {
            if (view.Content is Grid grid && !double.IsNaN(grid.Height))
                grid.Height += added;
            if (!double.IsNaN(view.Height))
                view.Height += added;
            if (Window.GetWindow(view) is { } window && (window.SizeToContent & SizeToContent.Height) == 0)
                window.Height += added;
        }
    }
}
