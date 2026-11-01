using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EncodeNavi.OutputDialog
{
    internal sealed partial class CyberHintPresenter : IHintPresenter
    {
        private const double Extension = 400;
        private const double Padding = 56;

        private const double Elbow = 18;
        private const double Run = 44;
        private const double Rise = 28;
        private const double LabelSpacing = 46;
        private const double LineThickness = 2.5;

        private const double MinLabelArea = 240;
        private const double MinCoverage = 0.97;

        private const double FoldInset = 10;
        private const double MaxInsideLabelWidth = 320;

        private const double LaneInset = 10;
        private const double LaneGap = 7;
        private const double StackGap = 14;
        private const double Chamfer = 10;
        private const double LabelHeight = 28;

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

        private enum Placement
        {
            Right,
            Left,
            Above,
            Below,
            Inside,
        }

        private record CyberPalette(Color Line, Color Text, double GlowOpacity);

        private CyberPalette palette = new(Cyan, Cyan, 0.8);

        private readonly Window owner;
        private readonly Action showDetails;
        private readonly Window overlay;
        private readonly Canvas canvas;
        private readonly HashSet<string> shownKeys = [];
        private readonly List<(Polyline Packet, double Length, Ellipse Ring)> pulses = [];
        private readonly DispatcherTimer pulseTimer;
        private IReadOnlyList<PlacedHint> hints = [];
        private string lastSignature = "";
        private bool isLayoutPending;
        private bool isDetailsOpen;

        public CyberHintPresenter(Window owner, Action showDetails)
        {
            this.owner = owner;
            this.showDetails = showDetails;

            canvas = new Canvas
            {
                Effect = new DropShadowEffect { Color = palette.Line, BlurRadius = 10, ShadowDepth = 0, Opacity = palette.GlowOpacity },
            };
            overlay = new Window
            {
                Title = "EncodeNavi",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Owner = owner,
                Content = canvas,
            };
            overlay.SourceInitialized += (_, _) => MakeNonActivating(overlay);
            pulseTimer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = PulseInterval };
            pulseTimer.Tick += (_, _) => Pulse();

            owner.LocationChanged += OnOwnerChanged;
            owner.LayoutUpdated += OnOwnerChanged;
        }

        public void Show(IReadOnlyList<PlacedHint> hints)
        {
            this.hints = hints;
            lastSignature = "";
            ScheduleLayout();
        }

        public void KeepAbove(Window? window)
        {
            overlay.Owner = window ?? owner;
            isDetailsOpen = window != null;
            lastSignature = "";
            ScheduleLayout();
        }

        public void Dispose()
        {
            owner.LocationChanged -= OnOwnerChanged;
            owner.LayoutUpdated -= OnOwnerChanged;
            pulseTimer.Stop();
            overlay.Close();
        }

        private void OnOwnerChanged(object? sender, EventArgs e) => ScheduleLayout();

        private void ScheduleLayout()
        {
            if (isLayoutPending)
                return;
            isLayoutPending = true;
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                isLayoutPending = false;
                try
                {
                    Layout();
                }
                catch (Exception ex)
                {
                    YukkuriMovieMaker.Commons.Log.Default.Write("EncodeNavi: サイバー表示の描画に失敗しました: " + ex.Message);
                }
            });
        }

        private void Layout()
        {
            if (!owner.IsVisible || owner.WindowState == WindowState.Minimized || PresentationSource.FromVisual(owner) is not { CompositionTarget: { } target })
                return;

            var fromDevice = target.TransformFromDevice;
            var toDevice = target.TransformToDevice;
            var ownerRect = GetWindowRect(owner, fromDevice);

            var screenItems = new List<(Rect Rect, OutputHint Hint)>();
            foreach (var (element, hint) in hints)
            {
                if (GetVisibleRect(element, fromDevice) is { } rect)
                    screenItems.Add((rect, hint));
            }

            var workAreas = GetWorkAreas();
            bool Fits(Rect dipRect) => GetCoverage(workAreas, new Rect(toDevice.Transform(dipRect.TopLeft), toDevice.Transform(dipRect.BottomRight))) >= MinCoverage;

            var rowsTop = screenItems.Count > 0 ? screenItems.Min(x => x.Rect.Top) - Padding : ownerRect.Top;
            var rowsBottom = screenItems.Count > 0 ? screenItems.Max(x => x.Rect.Bottom) : ownerRect.Bottom;
            Rect RightRegion(double width) => new(ownerRect.Right, rowsTop, width, rowsBottom - rowsTop);
            Rect LeftRegion(double width) => new(ownerRect.Left - width, rowsTop, width, rowsBottom - rowsTop);

            var besideWidth = Elbow + Run + MinLabelArea;
            var stackHeight = screenItems.Count * LabelSpacing + StackGap;
            var fits = new Dictionary<Placement, bool>
            {
                [Placement.Right] = Fits(RightRegion(besideWidth)),
                [Placement.Left] = Fits(LeftRegion(besideWidth)),
                [Placement.Above] = Fits(new Rect(ownerRect.Left, ownerRect.Top - stackHeight, ownerRect.Width, stackHeight)),
                [Placement.Below] = Fits(new Rect(ownerRect.Left, ownerRect.Bottom, ownerRect.Width, stackHeight)),
            };
            Placement[] order = isDetailsOpen
                ? [Placement.Right, Placement.Above, Placement.Below, Placement.Left]
                : [Placement.Right, Placement.Left, Placement.Above, Placement.Below];
            var placement = order.FirstOrDefault(x => fits[x], Placement.Inside);

            double[] widths = [Extension, (Extension + besideWidth) / 2, besideWidth];
            var bounds = new Rect(ownerRect.Left - Padding, ownerRect.Top - Padding, ownerRect.Width + Padding * 2, ownerRect.Height + Padding * 2);
            if (placement == Placement.Right)
                bounds.Union(RightRegion(widths.First(x => Fits(RightRegion(x)) || x == besideWidth)));
            else if (placement == Placement.Left)
                bounds.Union(LeftRegion(widths.First(x => Fits(LeftRegion(x)) || x == besideWidth)));
            else if (placement == Placement.Above)
                bounds.Union(new Rect(ownerRect.Left, ownerRect.Top - stackHeight - Padding, ownerRect.Width, stackHeight + Padding));
            else if (placement == Placement.Below)
                bounds.Union(new Rect(ownerRect.Left, ownerRect.Bottom, ownerRect.Width, stackHeight + Padding));

            var offset = new Vector(-bounds.Left, -bounds.Top);
            var items = screenItems.Select(x => (Rect: Rect.Offset(x.Rect, offset), x.Hint)).OrderBy(x => x.Rect.Top).ToList();

            palette = GetPalette();
            var signature = $"{placement}|{palette}|{BuildSignature(bounds, items)}";
            if (signature == lastSignature)
                return;
            lastSignature = signature;

            if (items.Count == 0)
            {
                pulseTimer.Stop();
                overlay.Hide();
                return;
            }

            overlay.Left = bounds.Left;
            overlay.Top = bounds.Top;
            overlay.Width = bounds.Width;
            overlay.Height = bounds.Height;
            if (!overlay.IsVisible)
                overlay.Show();

            canvas.Children.Clear();
            pulses.Clear();
            if (canvas.Effect is DropShadowEffect glow)
            {
                glow.Color = palette.Line;
                glow.Opacity = palette.GlowOpacity;
            }
            var ownerLocal = Rect.Offset(ownerRect, offset);
            if (placement is Placement.Above or Placement.Below)
                DrawStacked(items, ownerLocal, placement == Placement.Above);
            else
                DrawBeside(items, ownerLocal, bounds.Width, placement);
            pulseTimer.Start();
        }

        private void DrawBeside(List<(Rect Rect, OutputHint Hint)> items, Rect ownerLocal, double boundsWidth, Placement placement)
        {
            var (edgeX, direction) = placement switch
            {
                Placement.Right => (ownerLocal.Right + Elbow, 1),
                Placement.Left => (ownerLocal.Left - Elbow, -1),
                _ => (ownerLocal.Right - FoldInset, -1),
            };
            var bendX = edgeX + direction * Run;
            var labelWidth = placement switch
            {
                Placement.Right => boundsWidth - bendX - 12,
                Placement.Left => bendX - 12,
                _ => Math.Min(MaxInsideLabelWidth, bendX - ownerLocal.Left - 20),
            };

            var previousLabelY = double.NegativeInfinity;
            for (var i = 0; i < items.Count; i++)
            {
                var (rect, hint) = items[i];
                var y = rect.Bottom + 2;
                var labelY = Math.Max(y - Rise, previousLabelY + LabelSpacing);
                previousLabelY = labelY;

                var (label, text) = CreateLabel(hint, i + 1, labelWidth);
                var length = Math.Max(Math.Min(180, labelWidth), label.DesiredSize.Width + 16);
                var startX = placement == Placement.Left ? rect.Right : rect.Left;
                AddHint(hint, rect, label, text,
                    [new(startX, y), new(edgeX, y), new(bendX, labelY), new(bendX + direction * length, labelY)],
                    direction > 0 ? bendX + 6 : bendX - 6 - label.DesiredSize.Width,
                    labelY);
            }
        }

        private void DrawStacked(List<(Rect Rect, OutputHint Hint)> items, Rect ownerLocal, bool above)
        {
            var ordered = above ? items : Enumerable.Reverse(items).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var (rect, hint) = ordered[i];
                var y = rect.Bottom + 2;
                var laneX = ownerLocal.Right - LaneInset - (ordered.Count - 1 - i) * LaneGap;
                var rowY = above
                    ? ownerLocal.Top - StackGap - i * LabelSpacing
                    : ownerLocal.Bottom + StackGap + LabelHeight + i * LabelSpacing;
                var cornerY = above ? rowY + Chamfer : rowY - Chamfer;

                var labelWidth = Math.Min(MaxInsideLabelWidth, laneX - Chamfer - ownerLocal.Left - 20);
                var (label, text) = CreateLabel(hint, above ? i + 1 : ordered.Count - i, labelWidth);
                var length = Math.Max(120, label.DesiredSize.Width + 16);
                AddHint(hint, rect, label, text,
                    [new(rect.Left, y), new(laneX, y), new(laneX, cornerY), new(laneX - Chamfer, rowY), new(laneX - Chamfer - length, rowY)],
                    laneX - Chamfer - 6 - label.DesiredSize.Width,
                    rowY);
            }
        }

        private void AddHint(OutputHint hint, Rect target, FrameworkElement label, TextBlock text, Point[] points, double labelLeft, double labelY)
        {
            var brush = GetBrush();
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

            Canvas.SetLeft(label, labelLeft);
            Canvas.SetTop(label, labelY - label.DesiredSize.Height - 4);

            canvas.Children.Add(line);
            canvas.Children.Add(packet);
            canvas.Children.Add(brackets);
            canvas.Children.Add(ring);
            canvas.Children.Add(marker);
            canvas.Children.Add(label);
            pulses.Add((packet, GetLength(line) / LineThickness, ring));

            var key = hint.Target + "\n" + hint.Text;
            if (shownKeys.Add(key))
            {
                AnimateDraw(line, label, brackets);
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

        private SolidColorBrush GetBrush() => Frozen(palette.Line);

        private CyberPalette GetPalette()
        {
            var control = (owner.TryFindResource(SystemColors.ControlBrushKey) as SolidColorBrush)?.Color ?? Color.FromRgb(0x20, 0x20, 0x20);
            var isLight = (0.299 * control.R + 0.587 * control.G + 0.114 * control.B) / 255 > 0.5;
            return isLight ? new(Cyan, DarkCyan, 0.45) : new(Cyan, Cyan, 0.8);
        }

        private static readonly Color Cyan = Color.FromRgb(0x00, 0xE5, 0xFF);
        private static readonly Color DarkCyan = Color.FromRgb(0x00, 0x83, 0xA6);
        private static readonly Color Spark = Color.FromRgb(0xF0, 0xFF, 0xFF);
        private static readonly Color TagText = Color.FromRgb(0x00, 0x16, 0x1C);
        private static readonly FontFamily HudFont = new("Bahnschrift, Yu Gothic UI");

        private SolidColorBrush GetTextBrush() => Frozen(palette.Text);

        private (StackPanel Panel, TextBlock Text) CreateLabel(OutputHint hint, int number, double labelWidth)
        {
            var brush = GetBrush();
            var buttonsWidth = TagWidth + (hint.CopyText != null ? 70 : 0) + (hint.ShowsDetailsButton ? 50 : 0);
            var text = new TextBlock
            {
                Text = hint.Text,
                Foreground = GetTextBrush(),
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

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
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
            if (hint.ShowsDetailsButton)
                panel.Children.Add(CreateButton("詳細", brush, "推奨値の理由や注意点を別ウィンドウで表示します", _ => showDetails()));
            panel.Measure(new Size(labelWidth, double.PositiveInfinity));
            return (panel, text);
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

        private Rect? GetVisibleRect(FrameworkElement element, Matrix fromDevice)
        {
            if (!element.IsVisible || !owner.IsAncestorOf(element))
                return null;

            var rect = ToScreenRect(element, new Rect(element.RenderSize), fromDevice);
            for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not ScrollViewer viewer)
                    continue;
                var viewport = ToScreenRect(viewer, new Rect(0, 0, viewer.ViewportWidth, viewer.ViewportHeight), fromDevice);
                var center = rect.Top + rect.Height / 2;
                if (center < viewport.Top || center > viewport.Bottom)
                    return null;
            }
            return rect;
        }

        private static Rect ToScreenRect(Visual visual, Rect rect, Matrix fromDevice) =>
            new(fromDevice.Transform(visual.PointToScreen(rect.TopLeft)), fromDevice.Transform(visual.PointToScreen(rect.BottomRight)));

        private static Rect GetWindowRect(Window window, Matrix fromDevice)
        {
            GetWindowRect(new WindowInteropHelper(window).Handle, out var rect);
            return new Rect(fromDevice.Transform(new Point(rect.Left, rect.Top)), fromDevice.Transform(new Point(rect.Right, rect.Bottom)));
        }

        private static unsafe List<Rect> GetWorkAreas()
        {
            var areas = new List<Rect>();
            var handle = GCHandle.Alloc(areas);
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &AddWorkArea, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }
            return areas;
        }

        [UnmanagedCallersOnly]
        private static unsafe int AddWorkArea(IntPtr monitor, IntPtr hdc, NativeRect* rect, IntPtr data)
        {
            var areas = (List<Rect>)GCHandle.FromIntPtr(data).Target!;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                areas.Add(new Rect(new Point(info.Work.Left, info.Work.Top), new Point(info.Work.Right, info.Work.Bottom)));
            return 1;
        }

        private static double GetCoverage(List<Rect> workAreas, Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return 0;
            var covered = 0.0;
            foreach (var area in workAreas)
            {
                var intersection = Rect.Intersect(area, rect);
                if (!intersection.IsEmpty)
                    covered += intersection.Width * intersection.Height;
            }
            return covered / (rect.Width * rect.Height);
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, delegate* unmanaged<IntPtr, IntPtr, NativeRect*, IntPtr, int> callback, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        private static string BuildSignature(Rect bounds, List<(Rect Rect, OutputHint Hint)> items)
        {
            var builder = new StringBuilder();
            builder.Append($"{bounds.Left:0},{bounds.Top:0},{bounds.Width:0},{bounds.Height:0}");
            foreach (var (rect, hint) in items)
                builder.Append($"|{rect.Left:0},{rect.Top:0},{rect.Width:0},{rect.Height:0},{hint}");
            return builder.ToString();
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static void MakeNonActivating(Window window)
        {
            const int GwlExStyle = -20;
            const int WsExNoActivate = 0x08000000;
            const int WsExToolWindow = 0x00000080;
            var handle = new WindowInteropHelper(window).Handle;
            _ = SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow);
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static partial int GetWindowLong(IntPtr hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
