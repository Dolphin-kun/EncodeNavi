namespace EncodeNavi.Recommendation
{
    internal record PlatformSpec(
        TargetPlatform Platform,
        string Name,
        int MaxLongEdge,
        int MaxShortEdge,
        int MaxFps,
        int StereoAudioKbps,
        int MonoAudioKbps,
        int MinAudioKbps,
        int SampleRate,
        TimeSpan? MaxDuration,
        long? MaxFileBytes,
        string VideoBitRateSource,
        string AudioNote)
    {
        public static PlatformSpec Get(TargetPlatform platform) => platform switch
        {
            TargetPlatform.YouTube => new PlatformSpec(
                platform, "YouTube", 7680, 4320, 60,
                StereoAudioKbps: 384, MonoAudioKbps: 128, MinAudioKbps: 0, SampleRate: 48000,
                MaxDuration: null, MaxFileBytes: null,
                VideoBitRateSource: "YouTube公式の推奨ビットレート",
                AudioNote: "YouTube公式の推奨はステレオ384kbps・モノラル128kbps"),

            TargetPlatform.Niconico => new PlatformSpec(
                platform, "ニコニコ動画", 1920, 1080, 60,
                StereoAudioKbps: 320, MonoAudioKbps: 128, MinAudioKbps: 0, SampleRate: 48000,
                MaxDuration: null, MaxFileBytes: null,
                VideoBitRateSource: "1080pまでの投稿で画質を保てる目安",
                AudioNote: "2025年3月以降、256kbps以上で投稿すると高音質(最大576kbps)で配信される場合がある"),

            TargetPlatform.X => new PlatformSpec(
                platform, "X", 1920, 1200, 60,
                StereoAudioKbps: 192, MonoAudioKbps: 128, MinAudioKbps: 128, SampleRate: 48000,
                MaxDuration: TimeSpan.FromSeconds(140), MaxFileBytes: 512L * 1024 * 1024,
                VideoBitRateSource: "Xの推奨（映像5,000kbps以上）",
                AudioNote: "Xの推奨は AAC-LC 128kbps以上"),

            _ => throw new ArgumentOutOfRangeException(nameof(platform)),
        };

        public int GetVideoKbps(int shortEdge, int fps)
        {
            var isHighFps = fps > 30;
            if (Platform == TargetPlatform.X)
                return shortEdge >= 1080 ? (isHighFps ? 10000 : 8000) : (isHighFps ? 6000 : 5000);

            return shortEdge switch
            {
                >= 2160 => isHighFps ? 53000 : 35000,
                >= 1440 => isHighFps ? 24000 : 16000,
                >= 1080 => isHighFps ? 12000 : 8000,
                >= 720 => isHighFps ? 7500 : 5000,
                >= 480 => isHighFps ? 4000 : 2500,
                _ => isHighFps ? 1500 : 1000,
            };
        }
    }
}
