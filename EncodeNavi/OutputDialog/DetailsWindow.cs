using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EncodeNavi.View;
using EncodeNavi.ViewModel;
using YukkuriMovieMaker.Plugin;

namespace EncodeNavi.OutputDialog
{
    internal sealed partial class DetailsWindow : Window
    {
        private const double DefaultWidth = 440;

        private const int DwmaBorderColor = 34;
        private const int DwmaCaptionColor = 35;
        private const int DwmaTextColor = 36;
        private const uint DwmColorDefault = 0xFFFFFFFF;

        public DetailsWindow(Window? owner, bool preferLeft)
        {
            var viewModel = new EncodeNaviViewModel();

            Title = "EncodeNavi - 推奨設定の詳細";
            Content = new EncodeNaviControl { DataContext = viewModel };
            ShowInTaskbar = false;
            Width = DefaultWidth;

            SetResourceReference(BackgroundProperty, SystemColors.ControlBrushKey);
            SetResourceReference(ForegroundProperty, SystemColors.WindowTextBrushKey);

            SourceInitialized += (_, _) => ApplyTitleBarTheme();
            ThemeManager.Instance.ThemeChanged += ThemeManager_ThemeChanged;
            Closed += (_, _) =>
            {
                ThemeManager.Instance.ThemeChanged -= ThemeManager_ThemeChanged;
                viewModel.Dispose();
            };

            if (owner is null)
            {
                Height = 720;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            Owner = owner;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Height = owner.ActualHeight;
            Top = owner.Top;
            var screen = SystemParameters.WorkArea;
            var right = owner.Left + owner.ActualWidth;
            var left = owner.Left - DefaultWidth;
            var fitsRight = right + DefaultWidth <= screen.Right;
            var fitsLeft = left >= screen.Left;
            Left = (preferLeft ? fitsLeft || !fitsRight : !fitsRight && fitsLeft) ? Math.Max(screen.Left, left) : right;
        }

        private void ThemeManager_ThemeChanged(object? sender, ThemeManager.ThemeChangedEventArgs e) => ApplyTitleBarTheme();

        private void ApplyTitleBarTheme()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                return;

            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            if (ThemeManager.Instance.CurrentThemeName is "Windows" or "NormalColor")
            {
                SetColor(handle, DwmaCaptionColor, DwmColorDefault);
                SetColor(handle, DwmaBorderColor, DwmColorDefault);
                SetColor(handle, DwmaTextColor, DwmColorDefault);
                return;
            }

            var caption = ToColorRef((TryFindResource(SystemColors.ControlBrushKey) as SolidColorBrush)?.Color ?? Colors.White);
            var text = ToColorRef((TryFindResource(SystemColors.WindowTextBrushKey) as SolidColorBrush)?.Color ?? Colors.White);
            SetColor(handle, DwmaCaptionColor, caption);
            SetColor(handle, DwmaBorderColor, caption);
            SetColor(handle, DwmaTextColor, text);
        }

        private static void SetColor(IntPtr handle, int attribute, uint color) => _ = DwmSetWindowAttribute(handle, attribute, ref color, sizeof(uint));

        private static uint ToColorRef(Color color) => (uint)(color.R | (color.G << 8) | (color.B << 16));

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
    }
}
