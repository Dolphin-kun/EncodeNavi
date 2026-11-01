using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EncodeNavi.Analysis;
using EncodeNavi.Settings;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Resources.Localization;
using YukkuriMovieMaker.VideoFileWriter;
using YukkuriMovieMaker.Views;

namespace EncodeNavi.OutputDialog
{
    internal static partial class ProgressPreviewHook
    {
        private static bool isRegistered;

        public static void Register()
        {
            if (isRegistered)
                return;
            isRegistered = true;

            EventManager.RegisterClassHandler(typeof(ProgressView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ProgressView view || !EncodeNaviSettings.Default.IsRenderPreviewEnabled || ProgressPreviewSession.IsAttached(view))
                return;

            try
            {
                ProgressPreviewSession.Attach(view);
            }
            catch (Exception ex)
            {
                YukkuriMovieMaker.Commons.Log.Default.Write("EncodeNavi: 出力中の映像の表示に失敗しました: " + ex.Message);
            }
        }
    }

    internal sealed partial class ProgressPreviewSession
    {
        private const double PreviewMargin = 6;
        private const double MinPreviewHeight = 120;
        private const double MaxPreviewHeight = 320;

        private static readonly DependencyProperty SessionProperty =
            DependencyProperty.RegisterAttached("Session", typeof(ProgressPreviewSession), typeof(ProgressPreviewSession));

        private readonly ProgressView view;
        private TextBox? messageBox;
        private Image? image;
        private FramePreviewRenderer? renderer;
        private bool isUnavailable;

        private ProgressPreviewSession(ProgressView view)
        {
            this.view = view;
        }

        public static bool IsAttached(ProgressView view) => view.GetValue(SessionProperty) != null;

        public static void Attach(ProgressView view)
        {
            var session = new ProgressPreviewSession(view);
            view.SetValue(SessionProperty, session);
            session.Start();
        }

        private void Start()
        {
            messageBox = FindFirst<TextBox>(view);
            if (messageBox is null)
                return;
            view.Unloaded += View_Unloaded;
            messageBox.TextChanged += MessageBox_TextChanged;
            Update(messageBox.Text);
        }

        private void View_Unloaded(object sender, RoutedEventArgs e)
        {
            view.Unloaded -= View_Unloaded;
            if (messageBox != null)
                messageBox.TextChanged -= MessageBox_TextChanged;
            renderer?.Dispose();
            renderer = null;
            view.ClearValue(SessionProperty);
        }

        private void MessageBox_TextChanged(object sender, TextChangedEventArgs e) => Update(messageBox?.Text ?? "");

        private void Update(string message)
        {
            if (isUnavailable || !message.StartsWith(Texts.VideoFileWriterProcessingMessage, StringComparison.Ordinal))
                return;
            var match = FrameRegex().Match(message);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var index))
                return;

            if (renderer is null && !TryStartRenderer())
            {
                isUnavailable = true;
                return;
            }
            renderer!.Request(SettingsBase<VideoFileWriterSettings>.Default.EncodeFrom + index);
        }

        private bool TryStartRenderer()
        {
            if (AnalysisStore.FindOutputTarget() is not { } target || view.Content is not Grid grid)
                return false;

            var timeline = target.Timeline;
            var scene = target.Scenes.AllScenes.FirstOrDefault(x => x.Timeline == timeline) ?? new Scene(timeline, target.Scenes, []);
            var width = Math.Max(1, grid.ActualWidth);
            var height = Math.Clamp(width * timeline.VideoInfo.Height / Math.Max(1, timeline.VideoInfo.Width), MinPreviewHeight, MaxPreviewHeight);

            image = new Image { Stretch = Stretch.Uniform };
            var frame = new Border
            {
                Background = Brushes.Black,
                Height = height,
                Margin = new Thickness(0, 0, 0, PreviewMargin),
                Child = image,
            };
            grid.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
            foreach (UIElement child in grid.Children)
                Grid.SetRow(child, Grid.GetRow(child) + 1);
            Grid.SetRow(frame, 0);
            grid.Children.Add(frame);

            var added = height + PreviewMargin;
            if (!double.IsNaN(grid.Height))
                grid.Height += added;
            if (!double.IsNaN(view.Height))
                view.Height += added;
            if (Window.GetWindow(view) is { } window && (window.SizeToContent & SizeToContent.Height) == 0)
                window.Height += added;

            renderer = new FramePreviewRenderer(scene, bitmap => view.Dispatcher.BeginInvoke(() =>
            {
                if (image != null)
                    image.Source = bitmap;
            }));
            return true;
        }

        private static T? FindFirst<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T found)
                    return found;
                if (FindFirst<T>(child) is { } nested)
                    return nested;
            }
            return null;
        }

        [GeneratedRegex(@"(\d+)\s*/\s*(\d+)")]
        private static partial Regex FrameRegex();
    }
}
