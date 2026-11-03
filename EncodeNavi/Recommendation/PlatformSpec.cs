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
        string AudioNote,
        double TargetLufs,
        string LoudnessReason)
    {
        private static readonly (int Tier, int Width, int Height)[] NiconicoLandscapeTiers = [(1080, 1920, 1080), (720, 1280, 720), (480, 854, 480)];
        private static readonly (int Tier, int Width, int Height)[] NiconicoPortraitTiers = [(1080, 1080, 1920), (720, 720, 1280), (480, 480, 854)];
        private const int NiconicoMinTier = 360;

        public static PlatformSpec Get(TargetPlatform platform) => platform switch
        {
            TargetPlatform.YouTube => new PlatformSpec(
                platform, "YouTube", 7680, 4320, 60,
                StereoAudioKbps: 384, MonoAudioKbps: 128, MinAudioKbps: 0, SampleRate: 48000,
                MaxDuration: null, MaxFileBytes: null,
                VideoBitRateSource: "YouTube公式の推奨ビットレート",
                AudioNote: "YouTube公式の推奨はステレオ384kbps・モノラル128kbps",
                TargetLufs: -14,
                LoudnessReason: "YouTubeは約 -14 LUFS より大きい動画の音量を下げて再生するので、-14 LUFS に合わせると他の動画と同じくらいの大きさで聞こえる"),

            TargetPlatform.Niconico => new PlatformSpec(
                platform, "ニコニコ動画", 1920, 1080, 60,
                StereoAudioKbps: 256, MonoAudioKbps: 256, MinAudioKbps: 0, SampleRate: 48000,
                MaxDuration: null, MaxFileBytes: null,
                VideoBitRateSource: "ニコニコ動画は公式の推奨ビットレートがないため、1080pまでの投稿で画質を保てる目安",
                AudioNote: "ニコニコ動画は投稿した音声のビットレートで配信音質が決まる（公式）。128kbps以上で128kbps、192kbps以上で192kbps、256kbps以上で320kbpsの音声が作られる",
                TargetLufs: -15,
                LoudnessReason: "ニコニコ動画は -15 LUFS（ITU-R BS.1770-3）を基準に音量を揃えて配信する（公式）ので、-15 LUFS に合わせておくと投稿後に音量を変えられない"),

            TargetPlatform.X => new PlatformSpec(
                platform, "X", 1920, 1200, 60,
                StereoAudioKbps: 192, MonoAudioKbps: 128, MinAudioKbps: 128, SampleRate: 48000,
                MaxDuration: TimeSpan.FromSeconds(140), MaxFileBytes: 512L * 1024 * 1024,
                VideoBitRateSource: "Xの推奨（映像5,000kbps以上）",
                AudioNote: "Xの推奨は AAC-LC 128kbps以上",
                TargetLufs: -14,
                LoudnessReason: "Xには公式の基準がないため、動画サイトでよく使われる -14 LUFS に合わせると他の動画と同じくらいの大きさで聞こえる"),

            _ => throw new ArgumentOutOfRangeException(nameof(platform)),
        };

        public static (int Tier, string? NextCondition, int NextTier) GetNiconicoTier(int width, int height)
        {
            var isPortrait = height > width;
            var tiers = isPortrait ? NiconicoPortraitTiers : NiconicoLandscapeTiers;
            for (var i = 0; i < tiers.Length; i++)
            {
                if (width >= tiers[i].Width || height >= tiers[i].Height)
                    return i == 0 ? (tiers[i].Tier, null, 0) : (tiers[i].Tier, GetCondition(tiers[i - 1], isPortrait), tiers[i - 1].Tier);
            }
            return (NiconicoMinTier, GetCondition(tiers[^1], isPortrait), tiers[^1].Tier);
        }

        private static string GetCondition((int Tier, int Width, int Height) tier, bool isPortrait) =>
            $"{(isPortrait ? "縦長" : "横長")}は幅{tier.Width}px以上か高さ{tier.Height}px以上";

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
