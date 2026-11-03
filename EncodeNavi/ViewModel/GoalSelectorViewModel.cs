using EncodeNavi.Recommendation;
using EncodeNavi.Settings;
using YukkuriMovieMaker.Commons;

namespace EncodeNavi.ViewModel
{
    internal class GoalSelectorViewModel : Bindable, IDisposable
    {
        private EncodeNaviSettings Settings { get; } = EncodeNaviSettings.Default;

        public GoalSelectorViewModel()
        {
            SettingsWatcher.Changed += SettingsWatcher_Changed;
        }

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

        public void Dispose() => SettingsWatcher.Changed -= SettingsWatcher_Changed;

        private void SaveSetting(bool isChanged, Action set)
        {
            if (!isChanged)
                return;
            set();
            Settings.Save();
        }

        private void SettingsWatcher_Changed(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(Goal));
            OnPropertyChanged(nameof(Platform));
            OnPropertyChanged(nameof(IsPlatformSelectable));
        }
    }
}
