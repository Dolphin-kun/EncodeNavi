using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EncodeNavi.OutputDialog
{
    internal sealed class CyberHintPresenter : OverlayHintPresenter
    {
        private const double LineThickness = 2.5;
        private const double TagWidth = 26;
        private const double PlateChamfer = 7;
        private const double BracketSize = 7;
        private const double BracketGap = 3;
        private const double BracketOpacity = 0.85;
        private const double MarkerSize = 7;
        private const double RingSize = 8;
        private const double PacketLength = 4;
        private const int DecodeSteps = 14;
        private const string DecodeGlyphs = "01<>/#*+=";
        private static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(3.2);

        private static readonly Color Cyan = Color.FromRgb(0x00, 0xE5, 0xFF);
        private static readonly Color DarkCyan = Color.FromRgb(0x00, 0x83, 0xA6);
        private static readonly Color Spark = Color.FromRgb(0xF0, 0xFF, 0xFF);
        private static readonly Color TagText = Color.FromRgb(0x00, 0x16, 0x1C);
        private static readonly FontFamily HudFont = new("Bahnschrift, Yu Gothic UI");

        private record CyberPalette(Color Line, Color Text, double GlowOpacity);

        private readonly DropShadowEffect glow;
        private readonly List<(Polyline Packet, double Length, Ellipse Ring)> pulses = [];
        private readonly DispatcherTimer pulseTimer;
        private CyberPalette palette = new(Cyan, Cyan, 0.8);

        public CyberHintPresenter(Window owner) : base(owner)
        {
            glow = new DropShadowEffect { Color = palette.Line, BlurRadius = 10, ShadowDepth = 0, Opacity = palette.GlowOpacity };
            Canvas.Effect = glow;
            pulseTimer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = PulseInterval };
            pulseTimer.Tick += (_, _) => Pulse();
        }

        protected override string ThemeName => "サイバー";

        protected override string PrepareStyle()
        {
            var control = (Owner.TryFindResource(SystemColors.ControlBrushKey) as SolidColorBrush)?.Color ?? Color.FromRgb(0x20, 0x20, 0x20);
            var isLight = (0.299 * control.R + 0.587 * control.G + 0.114 * control.B) / 255 > 0.5;
            palette = isLight ? new(Cyan, DarkCyan, 0.45) : new(Cyan, Cyan, 0.8);
            return palette.ToString();
        }

        protected override void BeginDraw()
        {
            pulses.Clear();
            glow.Color = palette.Line;
            glow.Opacity = palette.GlowOpacity;
        }

        protected override void EndDraw() => pulseTimer.Start();

        protected override void OnHidden() => pulseTimer.Stop();

        protected override FrameworkElement CreateLabel(OutputHint hint, int number, double labelWidth)
        {
            var brush = Frozen(palette.Line);
            var buttonsWidth = TagWidth + (hint.CopyText != null ? 70 : 0);
            var text = new TextBlock
            {
                Text = hint.Text,
                Foreground = Frozen(palette.Text),
                FontFamily = HudFont,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(80, labelWidth - buttonsWidth - 18),
            };
            var outline = new Polygon { Stroke = brush, StrokeThickness = 1 };
            outline.SetResourceReference(Shape.FillProperty, SystemColors.WindowBrushKey);
            var plate = new Grid { ToolTip = hint.ToolTip };
            plate.Children.Add(new Canvas { Children = { outline } });
            plate.Children.Add(new Border { Padding = new Thickness(8, 3, 10, 3), Child = text });
            plate.SizeChanged += (_, e) => outline.Points = GetPlatePoints(e.NewSize);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Tag = text };
            panel.Children.Add(CreateTag(number, brush));
            panel.Children.Add(plate);
            if (hint.CopyText is { } copyText)
            {
                panel.Children.Add(CreateButton("COPY", brush, copyText, button =>
                {
                    Clipboard.SetText(copyText);
                    ((TextBlock)button.Child).Text = "COPIED";
                }));
            }
            panel.Measure(new Size(labelWidth, double.PositiveInfinity));
            return panel;
        }

        protected override void AddHint(OutputHint hint, int number, Rect target, FrameworkElement label, Point[] points, double labelLeft, double labelY)
        {
            var brush = Frozen(palette.Line);
            var line = new Polyline
            {
                Points = [.. points],
                Stroke = brush,
                StrokeThickness = LineThickness,
                StrokeLineJoin = PenLineJoin.Bevel,
            };
            var packet = new Polyline
            {
                Points = [.. points],
                Stroke = Frozen(Spark),
                StrokeThickness = LineThickness,
                StrokeLineJoin = PenLineJoin.Bevel,
                StrokeDashArray = [PacketLength, 10000],
                StrokeDashOffset = PacketLength,
            };
            var marker = new Rectangle
            {
                Width = MarkerSize,
                Height = MarkerSize,
                Fill = brush,
                RenderTransform = new RotateTransform(45, MarkerSize / 2, MarkerSize / 2),
            };
            Canvas.SetLeft(marker, points[0].X - MarkerSize / 2);
            Canvas.SetTop(marker, points[0].Y - MarkerSize / 2);

            var ring = new Ellipse
            {
                Width = RingSize,
                Height = RingSize,
                Stroke = brush,
                StrokeThickness = 1.5,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(),
            };
            Canvas.SetLeft(ring, points[0].X - RingSize / 2);
            Canvas.SetTop(ring, points[0].Y - RingSize / 2);

            var brackets = CreateBrackets(target, brush);
            PlaceLabel(label, labelLeft, labelY);

            Canvas.Children.Add(line);
            Canvas.Children.Add(packet);
            Canvas.Children.Add(brackets);
            Canvas.Children.Add(ring);
            Canvas.Children.Add(marker);
            Canvas.Children.Add(label);
            pulses.Add((packet, GetLength(line) / LineThickness, ring));

            if (IsFirstShow(hint))
            {
                AnimateDraw(line, label, brackets);
                if (label.Tag is TextBlock text)
                    Decode(text, hint.Text);
            }
        }

        private static Path CreateBrackets(Rect target, Brush brush)
        {
            var left = target.Left - BracketGap;
            var right = target.Right + BracketGap;
            var top = target.Top - BracketGap;
            var geometry = new PathGeometry();
            geometry.Figures.Add(CreateFigure(new(left, top + BracketSize), new(left, top), new(left + BracketSize, top)));
            geometry.Figures.Add(CreateFigure(new(right - BracketSize, top), new(right, top), new(right, top + BracketSize)));
            geometry.Freeze();
            return new Path { Data = geometry, Stroke = brush, StrokeThickness = 1.5, Opacity = BracketOpacity };
        }

        private static PathFigure CreateFigure(Point start, params Point[] points)
        {
            var figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(new PolyLineSegment(points, true));
            return figure;
        }

        private static Border CreateTag(int number, Brush brush) => new()
        {
            Background = brush,
            Width = TagWidth,
            Margin = new Thickness(0, 0, 2, 0),
            Child = new TextBlock
            {
                Text = number.ToString("00"),
                Foreground = Frozen(TagText),
                FontFamily = HudFont,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        private static PointCollection GetPlatePoints(Size size)
        {
            var right = size.Width - 0.5;
            var bottom = size.Height - 0.5;
            return
            [
                new(0.5, 0.5),
                new(right - PlateChamfer, 0.5),
                new(right, 0.5 + PlateChamfer),
                new(right, bottom),
                new(0.5 + PlateChamfer, bottom),
                new(0.5, bottom - PlateChamfer),
            ];
        }

        private static Border CreateButton(string text, Brush brush, string toolTip, Action<Border> onClick)
        {
            var button = new Border
            {
                BorderBrush = brush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.Hand,
                ToolTip = toolTip,
                Child = new TextBlock { Text = text, Foreground = brush, FontFamily = HudFont, FontSize = 11, FontWeight = FontWeights.Bold },
            };
            button.SetResourceReference(Border.BackgroundProperty, SystemColors.WindowBrushKey);
            button.MouseEnter += (_, _) => button.SetResourceReference(Border.BackgroundProperty, SystemColors.ControlBrushKey);
            button.MouseLeave += (_, _) => button.SetResourceReference(Border.BackgroundProperty, SystemColors.WindowBrushKey);
            button.MouseLeftButtonUp += (_, e) =>
            {
                onClick(button);
                e.Handled = true;
            };
            return button;
        }

        private static double GetLength(Polyline line)
        {
            var length = 0.0;
            for (var i = 1; i < line.Points.Count; i++)
                length += (line.Points[i] - line.Points[i - 1]).Length;
            return length;
        }

        private static void AnimateDraw(Polyline line, FrameworkElement label, FrameworkElement brackets)
        {
            var dash = GetLength(line) / LineThickness;
            line.StrokeDashArray = [dash, dash];
            line.StrokeDashOffset = dash;
            line.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(dash, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

            brackets.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new DiscreteDoubleKeyFrame(BracketOpacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))),
                    new DiscreteDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))),
                    new DiscreteDoubleKeyFrame(BracketOpacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))),
                },
            });

            label.Opacity = 0;
            label.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(220),
            });
        }

        private static void Decode(TextBlock text, string value)
        {
            var step = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Render, text.Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                step++;
                if (step >= DecodeSteps)
                {
                    text.Text = value;
                    timer.Stop();
                    return;
                }
                var chars = value.ToCharArray();
                for (var i = value.Length * step / DecodeSteps; i < chars.Length; i++)
                {
                    if (!char.IsWhiteSpace(chars[i]))
                        chars[i] = DecodeGlyphs[Random.Shared.Next(DecodeGlyphs.Length)];
                }
                text.Text = new string(chars);
            };
            timer.Start();
        }

        private void Pulse()
        {
            for (var i = 0; i < pulses.Count; i++)
            {
                var (packet, length, ring) = pulses[i];
                var begin = TimeSpan.FromMilliseconds(i * 90);
                packet.BeginAnimation(Shape.StrokeDashOffsetProperty, CreatePulseAnimation(PacketLength, -length, 700, begin, new SineEase { EasingMode = EasingMode.EaseInOut }));
                ring.BeginAnimation(UIElement.OpacityProperty, CreatePulseAnimation(0.9, 0, 600, begin, null));
                if (ring.RenderTransform is ScaleTransform scale)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreatePulseAnimation(1, 3.2, 600, begin, null));
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreatePulseAnimation(1, 3.2, 600, begin, null));
                }
            }
        }

        private static DoubleAnimation CreatePulseAnimation(double from, double to, int milliseconds, TimeSpan begin, IEasingFunction? easing)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                BeginTime = begin,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = easing,
            };
            Timeline.SetDesiredFrameRate(animation, 40);
            return animation;
        }
    }
}
