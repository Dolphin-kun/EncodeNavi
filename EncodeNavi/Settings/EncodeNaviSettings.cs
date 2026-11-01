using EncodeNavi.Recommendation;
using YukkuriMovieMaker.Plugin;

namespace EncodeNavi.Settings
{
    internal class EncodeNaviSettings : SettingsBase<EncodeNaviSettings>
    {
        private EncodeGoal goal = EncodeGoal.Size;
        private TargetPlatform platform = TargetPlatform.YouTube;
        private bool isAutoAnalyzeEnabled = true;
        private OutputHintTheme hintTheme = OutputHintTheme.Default;
        private bool isRenderPreviewEnabled = true;
        private bool isLoudnessCheckEnabled = true;

        public override SettingsCategory Category => SettingsCategory.None;
        public override string Name => "EncodeNavi";
        public override bool HasSettingView => false;
        public override object? SettingView => null;

        public EncodeGoal Goal
        {
            get => goal;
            set => Set(ref goal, value);
        }

        public TargetPlatform Platform
        {
            get => platform;
            set => Set(ref platform, value);
        }

        public bool IsAutoAnalyzeEnabled
        {
            get => isAutoAnalyzeEnabled;
            set => Set(ref isAutoAnalyzeEnabled, value);
        }

        public OutputHintTheme HintTheme
        {
            get => hintTheme;
            set => Set(ref hintTheme, value);
        }

        public bool IsRenderPreviewEnabled
        {
            get => isRenderPreviewEnabled;
            set => Set(ref isRenderPreviewEnabled, value);
        }

        public bool IsLoudnessCheckEnabled
        {
            get => isLoudnessCheckEnabled;
            set => Set(ref isLoudnessCheckEnabled, value);
        }

        public override void Initialize()
        {
        }
    }
}
