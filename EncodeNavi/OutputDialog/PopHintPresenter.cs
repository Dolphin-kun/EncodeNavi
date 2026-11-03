using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace EncodeNavi.OutputDialog
{
    internal sealed class PopHintPresenter(Window owner) : OverlayHintPresenter(owner)
    {
        private const double LineThickness = 2.6;
        private const double OutlineThickness = 2.5;
        private const double OutlineGap = 3;
        private const double StartDot = 7;

        private static readonly Color[] Accents =
        [
            Color.FromRgb(0xFF, 0x5F, 0xA2),
            Color.FromRgb(0xB3, 0x6B, 0xFF),
            Color.FromRgb(0x4F, 0x8B, 0xFF),
            Color.FromRgb(0x2E, 0xC8, 0xE6),
            Color.FromRgb(0x43, 0xC9, 0x6B),
            Color.FromRgb(0xF5, 0xB7, 0x00),
            Color.FromRgb(0xFF, 0x8A, 0x3D),
        ];

        private static readonly Color Ink = Color.FromRgb(0x3B, 0x2C, 0x4A);
        private static readonly FontFamily PopFont = new("Yu Gothic UI");

        protected override string ThemeName => "ポップ";

        protected override string PrepareStyle() => ThemeName;

        protected override void BeginDraw()
        {
        }

        protected override FrameworkElement CreateLabel(OutputHint hint, int number, double labelWidth)
        {
            var accent = GetAccent(number);
            var accentBrush = Frozen(accent);
            var buttonsWidth = hint.CopyText != null ? 90 : 0;

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = "★",
                Foreground = accentBrush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = hint.Text,
                Foreground = Frozen(Ink),
                FontFamily = PopFont,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = Math.Max(80, labelWidth - buttonsWidth - 40),
            });
            if (hint.CopyText is { } copyText)
                content.Children.Add(CreateButton(copyText, accentBrush));

            var bubble = new Border
            {
                Background = Frozen(Mix(accent, Colors.White, 0.78)),
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 3, 8, 3),
                Child = content,
                ToolTip = hint.ToolTip,
                Effect = new DropShadowEffect { Color = Mix(accent, Colors.Black, 0.35), BlurRadius = 0, ShadowDepth = 3, Direction = 300, Opacity = 0.55 },
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new ScaleTransform(1, 1),
            };
            bubble.Measure(new Size(labelWidth, double.PositiveInfinity));
            return bubble;
        }

        protected override void AddHint(OutputHint hint, int number, Rect target, FrameworkElement label, Point[] points, double labelLeft, double labelY)
        {
            var accentBrush = Frozen(GetAccent(number));

            var outline = new Rectangle
            {
                Width = target.Width + OutlineGap * 2,
                Height = target.Height + OutlineGap * 2,
                Stroke = accentBrush,
                StrokeThickness = OutlineThickness,
                StrokeDashArray = [1.6, 1.4],
                StrokeDashCap = PenLineCap.Round,
                RadiusX = 7,
                RadiusY = 7,
            };
            Canvas.SetLeft(outline, target.Left - OutlineGap);
            Canvas.SetTop(outline, target.Top - OutlineGap);

            var line = new Polyline
            {
                Points = [.. points],
                Stroke = accentBrush,
                StrokeThickness = LineThickness,
                StrokeDashArray = [0, 2],
                StrokeDashCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };

            var dot = new Ellipse { Width = StartDot, Height = StartDot, Fill = accentBrush, Stroke = Brushes.White, StrokeThickness = 1.5 };
            Canvas.SetLeft(dot, points[0].X - StartDot / 2);
            Canvas.SetTop(dot, points[0].Y - StartDot / 2);

            PlaceLabel(label, labelLeft, labelY);

            Canvas.Children.Add(outline);
            Canvas.Children.Add(line);
            Canvas.Children.Add(dot);
            Canvas.Children.Add(label);

            if (IsFirstShow(hint))
                PopIn(number, label, outline, line, dot);
        }

        private static void PopIn(int number, FrameworkElement label, params UIElement[] parts)
        {
            var begin = TimeSpan.FromMilliseconds((number - 1) * 70);
            foreach (var part in parts)
            {
                part.Opacity = 0;
                part.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { BeginTime = begin });
            }

            if (label.RenderTransform is not ScaleTransform scale)
                return;
            var animation = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(520))
            {
                BeginTime = begin + TimeSpan.FromMilliseconds(80),
                EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = EasingMode.EaseOut },
            };
            scale.ScaleX = 0;
            scale.ScaleY = 0;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private static Border CreateButton(string copyText, Brush accent)
        {
            var label = new TextBlock { Text = "コピー", Foreground = accent, FontFamily = PopFont, FontSize = 11, FontWeight = FontWeights.Bold };
            var button = new Border
            {
                Background = Brushes.White,
                BorderBrush = accent,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 0, 8, 1),
                Margin = new Thickness(8, 1, 0, 1),
                Cursor = Cursors.Hand,
                ToolTip = copyText,
                Child = label,
            };
            button.MouseEnter += (_, _) => button.Background = Frozen(Color.FromRgb(0xFF, 0xF4, 0xFA));
            button.MouseLeave += (_, _) => button.Background = Brushes.White;
            button.MouseLeftButtonUp += (_, e) =>
            {
                Clipboard.SetText(copyText);
                label.Text = "コピーしました";
                e.Handled = true;
            };
            return button;
        }

        private static Color GetAccent(int number) => Accents[(number - 1) % Accents.Length];

        private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));
    }
}
