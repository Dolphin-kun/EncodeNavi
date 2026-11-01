using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.VideoFileWriter;

namespace EncodeNavi.Recommendation
{
    internal enum OutputMethod
    {
        [Display(Name = "FFmpeg 出力")]
        FFmpeg,

        [Display(Name = "MediaFoundation出力")]
        MediaFoundation,
    }

    internal static class OutputMethodDetector
    {
        private const string MediaFoundationPluginTypeName = "YukkuriMovieMaker.Plugin.FileWriter.MediaFoundation.MFVideoFileWriterPlugin";
        private const string FFmpegPluginTypeName = "YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegVideoFileWriterPlugin";

        public static OutputMethod GetSelected() => GetSelectedOrNull() ?? OutputMethod.FFmpeg;

        public static OutputMethod? GetSelectedOrNull()
        {
            try
            {
                return SettingsBase<VideoFileWriterSettings>.Default.Plugin switch
                {
                    MediaFoundationPluginTypeName => OutputMethod.MediaFoundation,
                    FFmpegPluginTypeName or null or "" => OutputMethod.FFmpeg,
                    _ => null,
                };
            }
            catch
            {
                return OutputMethod.FFmpeg;
            }
        }
    }
}
