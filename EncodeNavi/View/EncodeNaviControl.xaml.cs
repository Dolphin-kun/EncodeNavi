using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EncodeNavi.Settings;
using YukkuriMovieMaker.Plugin;

namespace EncodeNavi.View
{
    public partial class EncodeNaviControl : UserControl
    {
        public EncodeNaviControl()
        {
            InitializeComponent();
            ApplyHintTheme();

            Loaded += (_, _) =>
            {
                SettingsWatcher.Changed += OnThemeChanged;
                ThemeManager.Instance.ThemeChanged += OnThemeChanged;
                ApplyHintTheme();
            };
            Unloaded += (_, _) =>
            {
                SettingsWatcher.Changed -= OnThemeChanged;
                ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
            };
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyHintTheme();

        private void ApplyHintTheme()
        {
            var (warning, ok) = EncodeNaviSettings.Default.HintTheme switch
            {
                OutputHintTheme.Color => (Color.FromRgb(0xFF, 0x98, 0x00), Color.FromRgb(0x4C, 0xAF, 0x50)),
                OutputHintTheme.Cyber => (Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0x4A, 0x9C, 0xFF)),
                OutputHintTheme.Pop => (Color.FromRgb(0xFF, 0x5F, 0xA2), Color.FromRgb(0x43, 0xC9, 0x6B)),
                _ => (GetTextColor(0xC0), GetTextColor(0x70)),
            };
            Resources["WarningBrush"] = Frozen(warning);
            Resources["WarningBackgroundBrush"] = Frozen(Color.FromArgb(0x2E, warning.R, warning.G, warning.B));
            Resources["OkBrush"] = Frozen(ok);
            Resources["OkBackgroundBrush"] = Frozen(Color.FromArgb(0x1F, ok.R, ok.G, ok.B));
        }

        private Color GetTextColor(byte alpha)
        {
            var text = (TryFindResource(SystemColors.WindowTextBrushKey) as SolidColorBrush)?.Color ?? Colors.White;
            return Color.FromArgb(alpha, text.R, text.G, text.B);
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
