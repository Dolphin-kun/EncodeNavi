using System.Reflection;
using System.Text.RegularExpressions;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg;
using YukkuriMovieMaker.VideoFileWriter;

namespace EncodeNavi.Recommendation
{
    internal enum MediaFoundationRateControl
    {
        ConstantBitRate = 0,
        AverageBitRate = 2,
        Quality = 3,
    }

    internal partial record CurrentOutputSettings(
        string VideoEncoder,
        string VideoQuality,
        string Preset,
        string VideoBitRateSetting,
        string AudioBitRate,
        string Channels,
        string HardwareEncoder,
        string EncodeSpeed,
        string NoiseFix)
    {
        public const string Unknown = "—";

        private const string MediaFoundationSettingsTypeName =
            "YukkuriMovieMaker.Plugin.FileWriter.MediaFoundation.MFVideoFileWriterSettings, YukkuriMovieMaker.Plugin.FileSource.MediaFoundation";

        public static CurrentOutputSettings Empty { get; } = new(Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown);

        public string LoudnessMode { get; init; } = Unknown;
        public string LoudnessTarget { get; init; } = Unknown;
        public string CompressorMode { get; init; } = Unknown;
        public string PeakLimit { get; init; } = Unknown;

        public static CurrentOutputSettings Read(OutputMethod method)
        {
            try
            {
                var settings = method == OutputMethod.MediaFoundation ? ReadMediaFoundation() : ReadFFmpeg();
                var writer = SettingsBase<VideoFileWriterSettings>.Default;
                return settings with
                {
                    LoudnessMode = EnumText.Get(writer.LoudnessNormalization.Mode),
                    LoudnessTarget = FormatLufs(writer.LoudnessNormalization.Target),
                    CompressorMode = EnumText.Get(writer.Compressor.RatioMode),
                    PeakLimit = FormatDb(writer.Compressor.PeakLimit),
                };
            }
            catch
            {
                return Empty;
            }
        }

        public static string FormatLufs(double value) => $"{value:0.0} LUFS";

        public static string FormatDb(double value) => $"{value:0.0} dB";

        private static CurrentOutputSettings ReadFFmpeg()
        {
            var settings = SettingsBase<FFmpegVideoFileWriterSettings>.Default;
            var videoArgs = settings.VideoArgs;
            var audioArgs = settings.AudioArgs;

            var videoQuality = videoArgs.TryGetValue("crf:v", out var crf) ? $"CRF {crf}"
                : videoArgs.TryGetValue("b:v", out var videoBitRate) ? FormatKbps(videoBitRate)
                : "指定なし";

            return Empty with
            {
                VideoEncoder = videoArgs.GetValueOrDefault("c:v", "h264"),
                VideoQuality = videoQuality,
                Preset = videoArgs.TryGetValue("preset", out var preset) ? preset
                    : videoArgs.TryGetValue("cpu-used", out var cpuUsed) ? $"cpu-used {cpuUsed}"
                    : "指定なし",
                VideoBitRateSetting = FormatVideoBitRateSetting(settings),
                AudioBitRate = audioArgs.TryGetValue("b:a", out var audioBitRate) ? FormatKbps(audioBitRate) : "指定なし",
                Channels = audioArgs.GetValueOrDefault("ac") == "1" ? "モノラル" : "ステレオ",
                HardwareEncoder = settings.IsHardwareEncoderEnabled ? "ON" : "OFF",
            };
        }

        public static object? GetMediaFoundationSettings() =>
            Type.GetType(MediaFoundationSettingsTypeName)
                ?.GetProperty("Default", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                ?.GetValue(null);

        private static CurrentOutputSettings ReadMediaFoundation()
        {
            if (GetMediaFoundationSettings() is not { } settings)
                return Empty;
            var type = settings.GetType();

            int GetInt(string name) => Convert.ToInt32(type.GetProperty(name)?.GetValue(settings));
            bool GetBool(string name) => type.GetProperty(name)?.GetValue(settings) is true;

            var videoBitRateSetting = GetInt("VideoBitRateMode") == 0
                ? "自動"
                : FormatMediaFoundationVideoBitRate((MediaFoundationRateControl)GetInt("VideoBitRateControlMode"), GetInt("VideoBitRate"), GetInt("VideoQuality"));

            return Empty with
            {
                VideoEncoder = "H.264",
                VideoBitRateSetting = videoBitRateSetting,
                AudioBitRate = $"{GetInt("AudioBitRate")} kbps",
                Channels = "ステレオ",
                HardwareEncoder = GetBool("IsHardwareAcceleration") ? "ON" : "OFF",
                EncodeSpeed = GetInt("EncodeSpeed").ToString(),
                NoiseFix = GetBool("IsFFmpegAACEncoderEnabled") ? "ON" : "OFF",
            };
        }

        public static string FormatVideoBitRateSetting(VideoBitRateMode mode, VideoBitRateControlMode controlMode, int? quality)
        {
            if (mode == VideoBitRateMode.None)
                return "指定しない";

            var modeName = mode == VideoBitRateMode.Auto ? "自動" : "手動";
            return controlMode == VideoBitRateControlMode.Quality
                ? $"{modeName} / 映像品質 {quality}"
                : $"{modeName} / 固定ビットレート";
        }

        public static string FormatMediaFoundationVideoBitRate(MediaFoundationRateControl control, int kbps, int quality) => control switch
        {
            MediaFoundationRateControl.Quality => $"手動 / 映像品質 {quality}",
            MediaFoundationRateControl.AverageBitRate => $"手動 / 平均ビットレート {kbps} kbps",
            _ => $"手動 / 固定ビットレート {kbps} kbps",
        };

        private static string FormatVideoBitRateSetting(FFmpegVideoFileWriterSettings settings) =>
            FormatVideoBitRateSetting(settings.VideoBitRateMode, settings.VideoBitRateControlMode, settings.VideoQuality);

        private static string FormatKbps(string value)
        {
            var match = NumberRegex().Match(value);
            if (!match.Success || !long.TryParse(match.Value, out var number))
                return value;
            return $"{(number >= 10000 ? number / 1000 : number)} kbps";
        }

        [GeneratedRegex(@"\d+")]
        private static partial Regex NumberRegex();
    }
}
