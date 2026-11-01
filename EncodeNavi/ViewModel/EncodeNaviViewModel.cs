using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EncodeNavi.Analysis;
using EncodeNavi.Commons;
using EncodeNavi.OutputDialog;
using EncodeNavi.Recommendation;
using EncodeNavi.Settings;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace EncodeNavi.ViewModel
{
    internal class EncodeNaviViewModel : Bindable, ITimelineToolViewModel, IDisposable
    {
        private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        private Timeline? watchedTimeline;
        private OutputMethod outputMethod = OutputMethodDetector.GetSelected();
        private OutputMethod? lastDetectedOutputMethod = OutputMethodDetector.GetSelectedOrNull();
        private bool isUpdatePending;
        private string projectText = "-";
        private IReadOnlyList<RecommendationItem> recommendations = [];
        private IReadOnlyList<MaterialRow> materials = [];
        private AnalysisResult? shownAnalysis;
        private string videoOptions = "";
        private string audioOptions = "";
        private string statusText = AnalysisStore.StatusText;
        private bool isAnalyzing = AnalysisStore.IsAnalyzing;

        private EncodeNaviSettings Settings { get; } = EncodeNaviSettings.Default;

        public EncodeGoal Goal
        {
            get => Settings.Goal;
            set => SaveSetting(Settings.Goal != value, () => Settings.Goal = value);
        }

        public TargetPlatform Platform
        {
            get => Settings.Platform;
            set => SaveSetting(Settings.Platform != value, () => Settings.Platform = value);
        }

        public bool IsPlatformSelectable => Goal == EncodeGoal.Platform;

        public bool IsAutoAnalyzeEnabled
        {
            get => Settings.IsAutoAnalyzeEnabled;
            set
            {
                SaveSetting(Settings.IsAutoAnalyzeEnabled != value, () => Settings.IsAutoAnalyzeEnabled = value);
                if (value)
                    AnalyzeIfNeeded();
            }
        }

        public OutputHintTheme HintTheme
        {
            get => Settings.HintTheme;
            set => SaveSetting(Settings.HintTheme != value, () => Settings.HintTheme = value);
        }

        public bool IsRenderPreviewEnabled
        {
            get => Settings.IsRenderPreviewEnabled;
            set => SaveSetting(Settings.IsRenderPreviewEnabled != value, () => Settings.IsRenderPreviewEnabled = value);
        }

        public bool IsLoudnessCheckEnabled
        {
            get => Settings.IsLoudnessCheckEnabled;
            set
            {
                if (Settings.IsLoudnessCheckEnabled == value)
                    return;
                SaveSetting(true, () => Settings.IsLoudnessCheckEnabled = value);
                if (!value)
                    AnalysisStore.ClearLoudness();
                else if (AnalysisStore.Result != null && !AnalysisStore.IsAnalyzing && GetTargetProject() is { } target)
                    _ = AnalysisStore.AnalyzeAsync(target.Timeline, target.Scenes);
            }
        }

        public UpdateChecker Update { get; } = UpdateChecker.Instance;

        public bool IsManualAnalyzeVisible => !IsAutoAnalyzeEnabled;

        public OutputMethod OutputMethod
        {
            get => outputMethod;
            set { if (Set(ref outputMethod, value)) ScheduleUpdate(); }
        }

        public string ProjectText
        {
            get => projectText;
            private set => Set(ref projectText, value);
        }

        public string StatusText
        {
            get => statusText;
            private set => Set(ref statusText, value);
        }

        public bool IsAnalyzing
        {
            get => isAnalyzing;
            private set => Set(ref isAnalyzing, value);
        }

        public IReadOnlyList<RecommendationItem> Recommendations
        {
            get => recommendations;
            private set => Set(ref recommendations, value, nameof(Recommendations), nameof(HasRecommendations), nameof(HasMisconfigured), nameof(MisconfiguredSummary));
        }

        public bool HasRecommendations => Recommendations.Count > 0;

        public bool HasMisconfigured => Recommendations.Any(x => x.IsMisconfigured);

        public string MisconfiguredSummary
        {
            get
            {
                var misconfigured = Recommendations.Where(x => x.IsMisconfigured).Select(x => x.Name).ToList();
                if (misconfigured.Count > 0)
                    return $"推奨と違う設定が {misconfigured.Count} 件あります（強調表示した項目）: {string.Join("、", misconfigured)}";
                return Recommendations.Any(x => x.Current == CurrentOutputSettings.Unknown)
                    ? "読み取れた設定はすべて推奨どおりです"
                    : "すべて推奨どおりに設定されています";
            }
        }

        public IReadOnlyList<MaterialRow> Materials
        {
            get => materials;
            private set => Set(ref materials, value);
        }

        public string VideoOptions
        {
            get => videoOptions;
            private set => Set(ref videoOptions, value, nameof(VideoOptions), nameof(HasOptions));
        }

        public bool HasOptions => !string.IsNullOrEmpty(VideoOptions);

        public string AudioOptions
        {
            get => audioOptions;
            private set => Set(ref audioOptions, value);
        }

        public ICommand AnalyzeCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopyVideoOptionsCommand { get; }
        public ICommand CopyAudioOptionsCommand { get; }

        public EncodeNaviViewModel()
        {
            OutputDialogHook.Register();
            ProgressPreviewHook.Register();
            UpdateChecker.Instance.EnsureChecked();

            AnalyzeCommand = new ActionCommand(
                _ => !AnalysisStore.IsAnalyzing && GetTargetProject() != null,
                async _ =>
                {
                    if (GetTargetProject() is { } target)
                        await AnalysisStore.AnalyzeAsync(target.Timeline, target.Scenes);
                });
            CancelCommand = new ActionCommand(
                _ => AnalysisStore.IsAnalyzing,
                _ => AnalysisStore.Cancel());
            CopyVideoOptionsCommand = new ActionCommand(
                _ => !string.IsNullOrEmpty(VideoOptions),
                _ => Clipboard.SetText(VideoOptions));
            CopyAudioOptionsCommand = new ActionCommand(
                _ => !string.IsNullOrEmpty(AudioOptions),
                _ => Clipboard.SetText(AudioOptions));

            AnalysisStore.Changed += AnalysisStore_Changed;
            SettingsWatcher.Changed += SettingsWatcher_Changed;
            Refresh();
        }

        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            AnalysisStore.SetToolInfo(info);
            Refresh();
            AnalyzeIfNeeded();
        }

        private void AnalyzeIfNeeded()
        {
            if (!IsAutoAnalyzeEnabled || AnalysisStore.IsAnalyzing || AnalysisStore.Result != null || GetTargetProject() is not { } target)
                return;
            _ = AnalysisStore.AnalyzeAsync(target.Timeline, target.Scenes);
        }

        public void Dispose()
        {
            AnalysisStore.Changed -= AnalysisStore_Changed;
            SettingsWatcher.Changed -= SettingsWatcher_Changed;
            WatchTimeline(null);
        }

        private void SaveSetting(bool isChanged, Action set)
        {
            if (!isChanged)
                return;
            set();
            Settings.Save();
        }

        private static (Timeline Timeline, Scenes Scenes)? GetTargetProject() => AnalysisStore.FindTarget();

        private void AnalysisStore_Changed(object? sender, EventArgs e)
        {
            StatusText = AnalysisStore.StatusText;
            IsAnalyzing = AnalysisStore.IsAnalyzing;
            CommandManager.InvalidateRequerySuggested();
            ScheduleUpdate();
        }

        private void SettingsWatcher_Changed(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(Goal));
            OnPropertyChanged(nameof(Platform));
            OnPropertyChanged(nameof(IsPlatformSelectable));
            OnPropertyChanged(nameof(IsAutoAnalyzeEnabled));
            OnPropertyChanged(nameof(IsManualAnalyzeVisible));
            OnPropertyChanged(nameof(HintTheme));
            OnPropertyChanged(nameof(IsRenderPreviewEnabled));
            OnPropertyChanged(nameof(IsLoudnessCheckEnabled));

            var detected = OutputMethodDetector.GetSelectedOrNull();
            if (detected != lastDetectedOutputMethod)
            {
                lastDetectedOutputMethod = detected;
                if (detected is { } method)
                    OutputMethod = method;
            }
            ScheduleUpdate();
        }

        private void VideoInfo_PropertyChanged(object? sender, PropertyChangedEventArgs e) => ScheduleUpdate();

        private void ScheduleUpdate()
        {
            if (isUpdatePending)
                return;
            isUpdatePending = true;
            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                isUpdatePending = false;
                Refresh();
            });
        }

        private void Refresh()
        {
            var target = AnalysisStore.Timeline ?? GetTargetProject()?.Timeline;
            WatchTimeline(target);

            var project = target is null ? null : AnalysisStore.GetProject(target);
            ProjectText = project is null
                ? "-"
                : $"{project.Width}x{project.Height} / {project.Fps} fps / {project.Hz} Hz / 長さ {project.Duration:hh\\:mm\\:ss}";

            var analysis = AnalysisStore.Result;
            if (analysis != shownAnalysis)
            {
                shownAnalysis = analysis;
                Materials = analysis?.Materials.Select(MaterialRow.From).ToList() ?? [];
            }

            if (analysis is null || project is null)
            {
                Recommendations = [];
                VideoOptions = "";
                AudioOptions = "";
                return;
            }

            var plan = Recommender.Recommend(Goal, Platform, OutputMethod, project, analysis, CurrentOutputSettings.Read(OutputMethod));
            Recommendations = plan.Items;
            VideoOptions = plan.VideoOptions;
            AudioOptions = plan.AudioOptions;
        }

        private void WatchTimeline(Timeline? timeline)
        {
            if (watchedTimeline == timeline)
                return;
            if (watchedTimeline != null)
                watchedTimeline.VideoInfo.PropertyChanged -= VideoInfo_PropertyChanged;
            watchedTimeline = timeline;
            if (timeline != null)
                timeline.VideoInfo.PropertyChanged += VideoInfo_PropertyChanged;
        }
    }
}
