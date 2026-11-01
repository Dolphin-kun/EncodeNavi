using System.ComponentModel;
using EncodeNavi.Recommendation;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg;
using YukkuriMovieMaker.VideoFileWriter;

namespace EncodeNavi.Settings
{
    internal static class SettingsWatcher
    {
        private static bool isStarted;
        private static event EventHandler? changed;

        public static event EventHandler? Changed
        {
            add
            {
                Start();
                changed += value;
            }
            remove => changed -= value;
        }

        private static void Start()
        {
            if (isStarted)
                return;
            isStarted = true;

            Watch(SettingsBase<VideoFileWriterSettings>.Default);
            Watch(SettingsBase<VideoFileWriterSettings>.Default.LoudnessNormalization);
            Watch(SettingsBase<VideoFileWriterSettings>.Default.Compressor);
            Watch(SettingsBase<FFmpegVideoFileWriterSettings>.Default);
            Watch(CurrentOutputSettings.GetMediaFoundationSettings() as INotifyPropertyChanged);
            Watch(EncodeNaviSettings.Default);
        }

        private static void Watch(INotifyPropertyChanged? settings)
        {
            if (settings != null)
                settings.PropertyChanged += (_, _) => changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
