using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using EncodeNavi.Analysis;
using EncodeNavi.Settings;
using EncodeNavi.View;
using YukkuriMovieMaker.Views;

namespace EncodeNavi.OutputDialog
{
    internal static class OutputDialogHook
    {
        private static bool isRegistered;

        public static void Register()
        {
            if (isRegistered)
                return;
            isRegistered = true;

            EventManager.RegisterClassHandler(typeof(Mp4ConfigView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Mp4ConfigView view || OutputDialogSession.IsAttached(view))
                return;

            try
            {
                OutputDialogSession.Attach(view);
            }
            catch (Exception ex)
            {
                YukkuriMovieMaker.Commons.Log.Default.Write("EncodeNavi: 動画出力画面への表示に失敗しました: " + ex.Message);
            }
        }
    }

    internal sealed class OutputDialogSession
    {
        private static readonly DependencyProperty SessionProperty =
            DependencyProperty.RegisterAttached("Session", typeof(OutputDialogSession), typeof(OutputDialogSession));

        private readonly Mp4ConfigView view;
        private IHintPresenter? presenter;
        private OutputHintTheme presenterTheme;
        private DetailsWindow? detailsWindow;
        private bool isRefreshPending;

        private OutputDialogSession(Mp4ConfigView view)
        {
            this.view = view;
        }

        public static bool IsAttached(Mp4ConfigView view) => view.GetValue(SessionProperty) != null;

        public static void Attach(Mp4ConfigView view)
        {
            var session = new OutputDialogSession(view);
            view.SetValue(SessionProperty, session);
            session.Start();
        }

        private void Start()
        {
            InsertGoalSelector();
            view.Unloaded += View_Unloaded;
            view.AddHandler(Expander.ExpandedEvent, new RoutedEventHandler(OnLayoutChanged));
            view.AddHandler(Expander.CollapsedEvent, new RoutedEventHandler(OnLayoutChanged));
            AnalysisStore.Changed += OnChanged;
            SettingsWatcher.Changed += OnChanged;

            if (AnalysisStore.FindOutputTarget() is { } project)
            {
                if (EncodeNaviSettings.Default.IsAutoAnalyzeEnabled)
                    _ = AnalysisStore.AnalyzeAsync(project.Timeline, project.Scenes);
                else
                    AnalysisStore.SetTimeline(project.Timeline);
            }

            ScheduleRefresh();
        }

        private void InsertGoalSelector()
        {
            if (view.Content is not Grid grid || grid.Children.OfType<GoalSelectorControl>().Any())
                return;
            grid.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
            foreach (UIElement child in grid.Children)
                Grid.SetRow(child, Grid.GetRow(child) + 1);
            var selector = new GoalSelectorControl(ShowDetails);
            grid.Children.Add(selector);
            if (grid.Children.OfType<ScrollViewer>().FirstOrDefault() is not { } viewer)
                return;
            void AlignToViewport() => selector.Margin = new Thickness(0, 0, Math.Max(0, viewer.ActualWidth - viewer.ViewportWidth), 0);
            viewer.ScrollChanged += (_, _) => AlignToViewport();
            AlignToViewport();
        }

        private void View_Unloaded(object sender, RoutedEventArgs e)
        {
            view.Unloaded -= View_Unloaded;
            view.RemoveHandler(Expander.ExpandedEvent, new RoutedEventHandler(OnLayoutChanged));
            view.RemoveHandler(Expander.CollapsedEvent, new RoutedEventHandler(OnLayoutChanged));
            AnalysisStore.Changed -= OnChanged;
            SettingsWatcher.Changed -= OnChanged;
            detailsWindow?.Close();
            presenter?.Dispose();
            presenter = null;
            view.ClearValue(SessionProperty);
        }

        private void OnChanged(object? sender, EventArgs e) => ScheduleRefresh();

        private void OnLayoutChanged(object sender, RoutedEventArgs e) => ScheduleRefresh();

        private void ShowDetails()
        {
            if (detailsWindow is { IsLoaded: true })
            {
                detailsWindow.Activate();
                return;
            }

            detailsWindow = new DetailsWindow(Window.GetWindow(view), preferLeft: EncodeNaviSettings.Default.HintTheme is OutputHintTheme.Cyber or OutputHintTheme.Pop);
            detailsWindow.Closing += (_, _) => (presenter as OverlayHintPresenter)?.KeepAbove(null);
            detailsWindow.Closed += (_, _) => detailsWindow = null;
            detailsWindow.Show();
            (presenter as OverlayHintPresenter)?.KeepAbove(detailsWindow);
        }

        private void ScheduleRefresh()
        {
            if (isRefreshPending)
                return;
            isRefreshPending = true;
            view.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                isRefreshPending = false;
                try
                {
                    Refresh();
                }
                catch (Exception ex)
                {
                    YukkuriMovieMaker.Commons.Log.Default.Write("EncodeNavi: 動画出力画面の推奨表示の更新に失敗しました: " + ex.Message);
                }
            });
        }

        private void Refresh()
        {
            if (!view.IsLoaded || Window.GetWindow(view) is not { } window)
                return;

            var hints = OutputHintBuilder.Build();
            var targets = FindTargets(view, hints.Select(x => x.Target).ToHashSet());
            var placed = new List<PlacedHint>();

            var hiddenCounts = new Dictionary<Expander, int>();
            foreach (var hint in hints)
            {
                if (!targets.TryGetValue(hint.Target, out var element))
                    continue;

                if (element.IsVisible)
                    placed.Add(new(element, hint));
                else if (hint.Kind == OutputHintKind.Warning && FindCollapsedExpander(element) is { } expander)
                    hiddenCounts[expander] = hiddenCounts.GetValueOrDefault(expander) + 1;
            }

            foreach (var (expander, count) in hiddenCounts)
            {
                if (expander.IsVisible)
                    placed.Add(new(expander, new OutputHint("", OutputHintKind.Warning, $"この中に推奨と違う設定が {count} 件")));
            }

            GetPresenter(window).Show(placed);
        }

        private IHintPresenter GetPresenter(Window window)
        {
            var theme = EncodeNaviSettings.Default.HintTheme;
            if (presenter != null && presenterTheme == theme)
                return presenter;

            presenter?.Dispose();
            presenterTheme = theme;
            if (theme is OutputHintTheme.Cyber or OutputHintTheme.Pop)
            {
                OverlayHintPresenter overlay = theme == OutputHintTheme.Cyber ? new CyberHintPresenter(window) : new PopHintPresenter(window);
                if (detailsWindow is { IsLoaded: true })
                    overlay.KeepAbove(detailsWindow);
                presenter = overlay;
            }
            else
            {
                presenter = new AdornerHintPresenter(theme == OutputHintTheme.Default);
            }
            return presenter;
        }

        private static Dictionary<string, FrameworkElement> FindTargets(DependencyObject root, HashSet<string> paths)
        {
            var found = new Dictionary<string, FrameworkElement>();
            var visited = new HashSet<DependencyObject>();
            var queue = new Queue<DependencyObject>([root]);
            while (queue.Count > 0 && found.Count < paths.Count)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                    continue;

                if (current is FrameworkElement element && GetBindingPath(element, paths) is { } path && !found.ContainsKey(path))
                    found[path] = element;

                if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                {
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                        queue.Enqueue(VisualTreeHelper.GetChild(current, i));
                }
                foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                    queue.Enqueue(child);
            }
            return found;
        }

        private static string? GetBindingPath(DependencyObject element, HashSet<string> paths)
        {
            var enumerator = element.GetLocalValueEnumerator();
            while (enumerator.MoveNext())
            {
                if (BindingOperations.GetBinding(element, enumerator.Current.Property) is { Path.Path: { } path, RelativeSource: null, ElementName: null }
                    && paths.Contains(path))
                {
                    return path;
                }
            }
            return null;
        }

        private static Expander? FindCollapsedExpander(DependencyObject element)
        {
            Expander? collapsed = null;
            for (var current = GetParent(element); current != null; current = GetParent(current))
            {
                if (current is Expander { IsExpanded: false } expander)
                    collapsed = expander;
            }
            return collapsed;
        }

        private static DependencyObject? GetParent(DependencyObject element) =>
            (element is Visual ? VisualTreeHelper.GetParent(element) : null) ?? LogicalTreeHelper.GetParent(element);
    }
}
