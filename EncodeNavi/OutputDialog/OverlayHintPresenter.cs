using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EncodeNavi.OutputDialog
{
    internal abstract partial class OverlayHintPresenter : IHintPresenter
    {
        private const double Extension = 400;
        private const double Padding = 56;

        protected const double Elbow = 18;
        protected const double Run = 44;
        private const double Rise = 28;
        private const double MinLabelArea = 240;
        private const double MinCoverage = 0.97;

        private const double FoldInset = 10;
        private const double MaxInsideLabelWidth = 320;

        private const double LaneInset = 10;
        private const double LaneGap = 7;
        private const double StackGap = 14;
        private const double Chamfer = 10;
        private const double LabelHeight = 28;

        private enum Placement
        {
            Right,
            Left,
            Above,
            Below,
            Inside,
        }

        private readonly Window owner;
        private readonly Window overlay;
        private readonly HashSet<string> shownKeys = [];
        private IReadOnlyList<PlacedHint> hints = [];
        private string lastSignature = "";
        private bool isLayoutPending;
        private bool isDetailsOpen;

        protected OverlayHintPresenter(Window owner)
        {
            this.owner = owner;
            Canvas = new Canvas();
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
                Content = Canvas,
            };
            overlay.SourceInitialized += (_, _) => MakeNonActivating(overlay);

            owner.LocationChanged += OnOwnerChanged;
            owner.LayoutUpdated += OnOwnerChanged;
        }

        protected Window Owner => owner;

        protected Canvas Canvas { get; }

        protected virtual double LabelSpacing => 46;

        protected abstract string ThemeName { get; }

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
            OnHidden();
            overlay.Close();
        }

        protected abstract string PrepareStyle();

        protected abstract void BeginDraw();

        protected virtual void EndDraw()
        {
        }

        protected virtual void OnHidden()
        {
        }

        protected abstract FrameworkElement CreateLabel(OutputHint hint, int number, double labelWidth);

        protected abstract void AddHint(OutputHint hint, int number, Rect target, FrameworkElement label, Point[] points, double labelLeft, double labelY);

        protected bool IsFirstShow(OutputHint hint) => shownKeys.Add(hint.Target + "\n" + hint.Text);

        protected static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        protected static void PlaceLabel(FrameworkElement label, double labelLeft, double labelY)
        {
            Canvas.SetLeft(label, labelLeft);
            Canvas.SetTop(label, labelY - label.DesiredSize.Height - 4);
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
                    YukkuriMovieMaker.Commons.Log.Default.Write($"EncodeNavi: {ThemeName}表示の描画に失敗しました: " + ex.Message);
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
            var rowsBottom = screenItems.Count > 0 ? screenItems.Max(x => x.Rect.Bottom) + Padding : ownerRect.Bottom;
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

            var signature = $"{placement}|{PrepareStyle()}|{BuildSignature(bounds, items)}";
            if (signature == lastSignature)
                return;
            lastSignature = signature;

            if (items.Count == 0)
            {
                OnHidden();
                overlay.Hide();
                return;
            }

            overlay.Left = bounds.Left;
            overlay.Top = bounds.Top;
            overlay.Width = bounds.Width;
            overlay.Height = bounds.Height;
            if (!overlay.IsVisible)
                overlay.Show();

            Canvas.Children.Clear();
            BeginDraw();
            var ownerLocal = Rect.Offset(ownerRect, offset);
            if (placement is Placement.Above or Placement.Below)
                DrawStacked(items, ownerLocal, placement == Placement.Above);
            else
                DrawBeside(items, ownerLocal, bounds.Width, placement);
            EndDraw();
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
                var label = CreateLabel(hint, i + 1, labelWidth);
                var labelY = Math.Max(y - Rise, previousLabelY + Math.Max(LabelSpacing, label.DesiredSize.Height + 8));
                previousLabelY = labelY;

                var length = Math.Max(Math.Min(180, labelWidth), label.DesiredSize.Width + 16);
                var startX = placement == Placement.Left ? rect.Right : rect.Left;
                AddHint(hint, i + 1, rect, label,
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
                var number = above ? i + 1 : ordered.Count - i;
                var label = CreateLabel(hint, number, labelWidth);
                var length = Math.Max(120, label.DesiredSize.Width + 16);
                AddHint(hint, number, rect, label,
                    [new(rect.Left, y), new(laneX, y), new(laneX, cornerY), new(laneX - Chamfer, rowY), new(laneX - Chamfer - length, rowY)],
                    laneX - Chamfer - 6 - label.DesiredSize.Width,
                    rowY);
            }
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
