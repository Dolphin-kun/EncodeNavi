using System.Reflection;
using System.Windows;
using EncodeNavi.Settings;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.ItemEditor;
using FrameTime = YukkuriMovieMaker.Player.Video.FrameTime;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace EncodeNavi.Analysis
{
    internal static class AnalysisStore
    {
        private static CancellationTokenSource? cancellation;
        private static WeakReference<TimelineToolInfo>? toolInfo;
        private static WeakReference<Timeline>? timeline;

        public static Timeline? Timeline => timeline is not null && timeline.TryGetTarget(out var target) ? target : null;
        public static AnalysisResult? Result { get; private set; }
        public static bool IsAnalyzing { get; private set; }
        public static string StatusText { get; private set; } = "「解析」を押すと、タイムライン上の素材を調べて出力設定を提案します。";

        public static event EventHandler? Changed;

        public static ProjectInfo? Project => Timeline is { } timeline ? GetProject(timeline) : null;

        public static ProjectInfo GetProject(Timeline timeline)
        {
            var videoInfo = timeline.VideoInfo;
            return new ProjectInfo(videoInfo.Width, videoInfo.Height, videoInfo.FPS, videoInfo.Hz, videoInfo.GetTimeFrom(timeline.Length));
        }

        public static void SetTimeline(Timeline target)
        {
            if (Timeline == target)
                return;
            cancellation?.Cancel();
            timeline = new(target);
            Result = null;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static async Task AnalyzeAsync(Timeline target, Scenes scenes)
        {
            cancellation?.Cancel();
            using var current = new CancellationTokenSource();
            cancellation = current;

            if (Timeline != target)
                Result = null;
            timeline = new(target);
            IsAnalyzing = true;
            Changed?.Invoke(null, EventArgs.Empty);

            try
            {
                var project = GetProject(target);
                var references = MaterialCollector.Collect(target, scenes);
                var progress = new Progress<string>(x =>
                {
                    if (cancellation != current)
                        return;
                    StatusText = x;
                    Changed?.Invoke(null, EventArgs.Empty);
                });
                var result = await Task.Run(() => MaterialAnalyzer.AnalyzeAsync(references, project, progress, current.Token), current.Token);

                LoudnessInfo? loudness = null;
                if (EncodeNaviSettings.Default.IsLoudnessCheckEnabled)
                {
                    Result = result with { Loudness = Result?.Loudness };
                    StatusText = "タイムラインの音量を解析中…";
                    Changed?.Invoke(null, EventArgs.Empty);
                    try
                    {
                        loudness = await Task.Run(() => AnalyzeLoudness(target, scenes, progress, current.Token), current.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        YukkuriMovieMaker.Commons.Log.Default.Write("EncodeNavi: タイムラインの音量の解析に失敗しました: " + ex.Message);
                    }
                }

                Result = result with { Loudness = loudness };
                StatusText = $"{result.Materials.Count} 件の素材を解析しました（{DateTime.Now:HH:mm:ss}）";
            }
            catch (OperationCanceledException)
            {
                if (cancellation == current)
                    StatusText = "解析を中止しました";
            }
            catch (Exception ex)
            {
                if (cancellation == current)
                    StatusText = "解析に失敗しました: " + ex.Message;
            }
            finally
            {
                if (cancellation == current)
                {
                    cancellation = null;
                    IsAnalyzing = false;
                }
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        public static void Cancel() => cancellation?.Cancel();

        public static void ClearLoudness()
        {
            if (Result is not { Loudness: not null } result)
                return;
            Result = result with { Loudness = null };
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static LoudnessInfo? AnalyzeLoudness(Timeline timeline, Scenes scenes, IProgress<string> progress, CancellationToken token)
        {
            var scene = scenes.AllScenes.FirstOrDefault(x => x.Timeline == timeline) ?? new Scene(timeline, scenes, []);
            var fps = timeline.VideoInfo.FPS;
            var start = new FrameTime(0, fps);
            var duration = new FrameTime(timeline.Length, fps);
            var editor = new EditorInfo(start, duration, start, duration, 0, null!, null!, scene, EditorType.None, null!, null!);
            using var stream = editor.CreateTimelineAudioSource();
            var totalSamples = (long)(timeline.VideoInfo.GetTimeFrom(timeline.Length).TotalSeconds * stream.Hz);
            var lastPercent = -1;
            var loudnessProgress = new Progress<double>(x =>
            {
                var percent = (int)(x * 100);
                if (percent == lastPercent)
                    return;
                lastPercent = percent;
                progress.Report($"タイムラインの音量を解析中… {percent}%");
            });
            return LoudnessAnalyzer.Analyze(stream, totalSamples, loudnessProgress, token);
        }

        public static void SetToolInfo(TimelineToolInfo info)
        {
            toolInfo = new(info);
            if (info.Timeline is { } target && Timeline != null)
                SetTimeline(target);
        }

        public static (Timeline Timeline, Scenes Scenes)? FindTarget()
        {
            if (toolInfo is not null && toolInfo.TryGetTarget(out var info) && info is { Timeline: { } target, Scenes: { } scenes })
                return (target, scenes);
            return FindMainProject();
        }

        public static (Timeline Timeline, Scenes Scenes)? FindOutputTarget() => FindMainProject() ?? FindTarget();

        private static (Timeline Timeline, Scenes Scenes)? FindMainProject()
        {
            try
            {
                var mainViewModel = Application.Current?.MainWindow?.DataContext;
                var model = mainViewModel?.GetType().GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mainViewModel);
                if (model?.GetType().GetProperty("Timeline")?.GetValue(model) is Timeline timeline
                    && model.GetType().GetProperty("Scenes")?.GetValue(model) is Scenes scenes)
                {
                    return (timeline, scenes);
                }
            }
            catch
            {
            }
            return null;
        }
    }
}
