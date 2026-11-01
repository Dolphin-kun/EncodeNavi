using FFmpeg.AutoGen;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg.Utilities;

namespace EncodeNavi.Analysis
{
    internal static unsafe class MediaProbe
    {
        private static readonly HashSet<string> LosslessAudioCodecs = ["flac", "alac", "wavpack", "tta", "ape", "mlp", "truehd"];
        private static readonly HashSet<string> StillImageCodecs = ["png", "mjpeg", "bmp", "tiff", "webp", "jpeg2000", "qoi"];

        public static void EnsureInitialized()
        {
            FFmpegModule.EnsureInitialized(FFmpegResourceLocator.GetFFmpegDllDirectory());
        }

        public static MediaProbeResult Probe(string filePath, int audioTrackIndex)
        {
            EnsureInitialized();

            AVFormatContext* context = null;
            if (ffmpeg.avformat_open_input(&context, filePath, null, null) < 0)
                throw new InvalidOperationException("ファイルを開けませんでした");

            try
            {
                if (ffmpeg.avformat_find_stream_info(context, null) < 0)
                    throw new InvalidOperationException("ストリーム情報を読み取れませんでした");

                var targetAudioNumber = Math.Max(audioTrackIndex, 1);
                var audioNumber = 0;
                VideoStreamInfo? video = null;
                AudioStreamInfo? audio = null;
                AudioStreamInfo? firstAudio = null;

                for (var i = 0; i < context->nb_streams; i++)
                {
                    var stream = context->streams[i];
                    var codecpar = stream->codecpar;

                    if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        if (video is null && (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                            video = ReadVideo(context, stream);
                    }
                    else if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    {
                        var info = ReadAudio(stream);
                        firstAudio ??= info;
                        if (++audioNumber == targetAudioNumber)
                            audio = info;
                    }
                }

                var duration = context->duration > 0 ? TimeSpan.FromMicroseconds(context->duration) : TimeSpan.Zero;
                return new MediaProbeResult(duration, video, audio ?? firstAudio);
            }
            finally
            {
                ffmpeg.avformat_close_input(&context);
            }
        }

        private static VideoStreamInfo ReadVideo(AVFormatContext* context, AVStream* stream)
        {
            var codecpar = stream->codecpar;
            var codec = ffmpeg.avcodec_get_name(codecpar->codec_id) ?? "unknown";
            var pixelFormat = (AVPixelFormat)codecpar->format;

            var fps = ToDouble(stream->avg_frame_rate);
            if (fps <= 0)
                fps = ToDouble(stream->r_frame_rate);

            var bitDepth = codecpar->bits_per_raw_sample;
            if (bitDepth <= 0)
            {
                var descriptor = ffmpeg.av_pix_fmt_desc_get(pixelFormat);
                bitDepth = descriptor != null ? descriptor->comp[0].depth : 8;
            }

            var bitRate = codecpar->bit_rate;
            if (bitRate <= 0 && context->bit_rate > 0 && !HasAudio(context))
                bitRate = context->bit_rate;

            var isStillImage = StillImageCodecs.Contains(codec) && stream->nb_frames <= 1;

            return new VideoStreamInfo(
                codecpar->width,
                codecpar->height,
                fps,
                bitRate,
                codec,
                bitDepth,
                ffmpeg.av_get_pix_fmt_name(pixelFormat) ?? "unknown",
                isStillImage);
        }

        private static AudioStreamInfo ReadAudio(AVStream* stream)
        {
            var codecpar = stream->codecpar;
            var codec = ffmpeg.avcodec_get_name(codecpar->codec_id) ?? "unknown";
            var isLossless = codec.StartsWith("pcm_", StringComparison.Ordinal) || LosslessAudioCodecs.Contains(codec);

            return new AudioStreamInfo(
                codecpar->sample_rate,
                codecpar->ch_layout.nb_channels,
                codecpar->bit_rate,
                codec,
                isLossless);
        }

        private static bool HasAudio(AVFormatContext* context)
        {
            for (var i = 0; i < context->nb_streams; i++)
            {
                if (context->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    return true;
            }
            return false;
        }

        private static double ToDouble(AVRational rational) => rational.num > 0 && rational.den > 0 ? (double)rational.num / rational.den : 0;
    }
}
