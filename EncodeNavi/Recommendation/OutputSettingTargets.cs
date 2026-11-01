namespace EncodeNavi.Recommendation
{
    internal static class OutputSettingTargets
    {
        public const string General = "SelectedVideoFileWriterPlugin.Value";

        public const string VideoBitRateMode = "VideoBitRateMode";
        public const string AudioBitRate = "AudioBitRate";

        public const string VideoOptions = "VideoOptions";
        public const string AudioOptions = "AudioOptions";
        public const string FFmpegHardwareEncoder = "IsHardwareEncoderEnabled";

        public const string MediaFoundationHardwareEncoder = "IsHardwareAcceleration";
        public const string EncodeSpeed = "EncodeSpeed";
        public const string NoiseFix = "IsFFmpegAACEncoderEnabled";

        public const string LoudnessMode = "LoudnessNormalization.Mode";
        public const string LoudnessTarget = "LoudnessNormalization.Target";
        public const string CompressorMode = "Compressor.RatioMode";
        public const string PeakLimit = "Compressor.PeakLimit";
    }
}
