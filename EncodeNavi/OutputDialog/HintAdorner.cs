using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace EncodeNavi.OutputDialog
{
    internal sealed class HintAdorner : Adorner
    {
        private readonly VisualCollection visuals;
        private readonly Border outline;
        private readonly Border callout;

        private record Palette(Brush Outline, Brush Background, Brush Border, Brush Foreground);

        public HintAdorner(FrameworkElement adornedElement, OutputHint hint, Action showDetails, bool isMonochrome) : base(adornedElement)
        {
            var palette = isMonochrome ? GetControlPalette(adornedElement) : GetColorPalette(hint.Kind);

            outline = new Border
            {
                BorderBrush = palette.Outline,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                IsHitTestVisible = false,
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = hint.Text,
                Foreground = palette.Foreground,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = Math.Max(240, adornedElement.ActualWidth),
            });
            if (hint.CopyText is { } copyText)
            {
                content.Children.Add(CreateButton("コピー", copyText, button =>
                {
                    Clipboard.SetText(copyText);
                    button.Content = "✓ コピーしました";
                }));
            }
            if (hint.ShowsDetailsButton)
                content.Children.Add(CreateButton("詳細", "推奨値の理由や注意点を別ウィンドウで表示します", _ => showDetails()));

            callout = new Border
            {
                Background = palette.Background,
                BorderBrush = palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1, 7, 1),
                Child = content,
                ToolTip = hint.ToolTip,
            };

            visuals = new VisualCollection(this) { outline, callout };
        }

        protected override int VisualChildrenCount => visuals.Count;

        protected override Visual GetVisualChild(int index) => visuals[index];

        protected override Size MeasureOverride(Size constraint)
        {
            outline.Measure(AdornedElement.RenderSize);
            callout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = AdornedElement.RenderSize;
            outline.Arrange(new Rect(-2, -2, size.Width + 4, size.Height + 4));

            var calloutSize = callout.DesiredSize;
            var top = -calloutSize.Height + 5;
            if (VisualTreeHelper.GetParent(this) is UIElement layer && AdornedElement.TranslatePoint(new Point(0, top), layer).Y < 0)
                top = size.Height - 5;
            callout.Arrange(new Rect(new Point(size.Width - calloutSize.Width + 2, top), calloutSize));
            return finalSize;
        }

        private static Button CreateButton(string content, string toolTip, Action<Button> onClick)
        {
            var button = new Button
            {
                Content = content,
                FontSize = 11,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(6, 1, 0, 1),
                ToolTip = toolTip,
            };
            button.Click += (_, _) => onClick(button);
            return button;
        }

        private static Palette GetControlPalette(FrameworkElement element)
        {
            var text = (element.TryFindResource(SystemColors.WindowTextBrushKey) as SolidColorBrush)?.Color ?? Colors.White;
            var control = (element.TryFindResource(SystemColors.ControlBrushKey) as SolidColorBrush)?.Color ?? Colors.Black;
            return new(
                Frozen(Color.FromArgb(0xA0, text.R, text.G, text.B)),
                Frozen(control),
                Frozen(Color.FromArgb(0x80, text.R, text.G, text.B)),
                Frozen(text));
        }

        private static Palette GetColorPalette(OutputHintKind kind)
        {
            var (accent, foreground) = kind switch
            {
                OutputHintKind.Warning => (Frozen(Color.FromRgb(0xFF, 0x98, 0x00)), Frozen(Color.FromRgb(0x20, 0x20, 0x20))),
                OutputHintKind.Ok => (Frozen(Color.FromRgb(0x4C, 0xAF, 0x50)), Frozen(Colors.White)),
                _ => (Frozen(Color.FromRgb(0x21, 0x96, 0xF3)), Frozen(Colors.White)),
            };
            return new(accent, accent, accent, foreground);
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
