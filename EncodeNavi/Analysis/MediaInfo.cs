namespace EncodeNavi.Analysis
{
    internal record VideoStreamInfo(
        int Width,
        int Height,
        double Fps,
        long BitRate,
        string Codec,
        int BitDepth,
        string PixelFormat,
        bool IsStillImage);

    internal record AudioStreamInfo(
        int SampleRate,
        int Channels,
        long BitRate,
        string Codec,
        bool IsLossless);

    internal record AudioContentInfo(
        double BandwidthHz,
        double NyquistHz,
        bool IsMono,
        bool IsSilent)
    {
        public bool IsHighCut => !IsSilent && BandwidthHz < NyquistHz * 0.9;
    }

    internal record MediaProbeResult(TimeSpan Duration, VideoStreamInfo? Video, AudioStreamInfo? Audio);

    internal record MaterialReference(
        string FilePath,
        int AudioTrackIndex,
        bool UsesVideo,
        bool UsesAudio,
        bool IsPanned);

    internal record MaterialInfo(
        MaterialReference Reference,
        MediaProbeResult? Probe,
        AudioContentInfo? AudioContent,
        string? Error);

    internal record ProjectInfo(int Width, int Height, int Fps, int Hz, TimeSpan Duration);

    internal record LoudnessInfo(double IntegratedLufs, double PeakDb);

    internal record AnalysisResult(IReadOnlyList<MaterialInfo> Materials, EncoderCapabilities Encoders, LoudnessInfo? Loudness = null);
}
