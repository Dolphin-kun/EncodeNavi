using YukkuriMovieMaker.Plugin.FileSource;

namespace EncodeNavi.Analysis
{
    internal static class AudioContentAnalyzer
    {
        private const int FftSize = 8192;
        private const int SegmentCount = 16;
        private const double SegmentSeconds = 2;

        private const double BandwidthThresholdDb = 75;
        private const double NoiseFloorDbfs = -120;

        private const double MonoSideRatio = 1e-5;

        private const float SilenceLevel = 1e-4f;

        private static readonly double[] Window = CreateHannWindow(FftSize);

        public static AudioContentInfo? Analyze(IAudioFileSource source, CancellationToken token)
        {
            var hz = source.Hz;
            if (hz <= 0)
                return null;

            var power = new double[FftSize / 2 + 1];
            var re = new double[FftSize];
            var im = new double[FftSize];
            var buffer = new float[FftSize * 2];
            var blockCount = 0;
            var totalFrames = 0L;
            var midEnergy = 0.0;
            var sideEnergy = 0.0;
            var peak = 0f;

            foreach (var (start, frames) in GetSegments(source.Duration, hz))
            {
                token.ThrowIfCancellationRequested();
                source.Seek(start);

                for (var remaining = frames; remaining >= FftSize; remaining -= FftSize)
                {
                    var read = ReadFully(source, buffer);
                    if (read < FftSize)
                        break;
                    totalFrames += read;

                    var blockPeak = 0f;
                    for (var i = 0; i < FftSize; i++)
                    {
                        var left = buffer[i * 2];
                        var right = buffer[i * 2 + 1];
                        var mid = (left + right) * 0.5;
                        var side = (left - right) * 0.5;
                        midEnergy += mid * mid;
                        sideEnergy += side * side;
                        blockPeak = Math.Max(blockPeak, Math.Max(Math.Abs(left), Math.Abs(right)));
                        re[i] = mid * Window[i];
                        im[i] = 0;
                    }
                    peak = Math.Max(peak, blockPeak);

                    if (blockPeak < SilenceLevel)
                        continue;

                    Fft(re, im);
                    for (var k = 0; k < power.Length; k++)
                        power[k] += re[k] * re[k] + im[k] * im[k];
                    blockCount++;
                }
            }

            if (totalFrames == 0)
                return null;

            var nyquist = hz / 2.0;
            if (blockCount == 0 || peak < SilenceLevel)
                return new AudioContentInfo(0, nyquist, IsMono: true, IsSilent: true);

            var isMono = sideEnergy <= midEnergy * MonoSideRatio;
            var bandwidth = EstimateBandwidth(power, blockCount, hz);
            return new AudioContentInfo(bandwidth, nyquist, isMono, IsSilent: false);
        }

        private static IEnumerable<(TimeSpan Start, long Frames)> GetSegments(TimeSpan duration, int hz)
        {
            var segmentFrames = (long)(SegmentSeconds * hz);
            if (duration <= TimeSpan.FromSeconds(SegmentSeconds * SegmentCount))
            {
                yield return (TimeSpan.Zero, (long)(duration.TotalSeconds * hz) + FftSize);
                yield break;
            }

            for (var i = 0; i < SegmentCount; i++)
            {
                var center = duration.TotalSeconds * (i + 0.5) / SegmentCount;
                yield return (TimeSpan.FromSeconds(center - SegmentSeconds / 2), segmentFrames);
            }
        }

        private static int ReadFully(IAudioFileSource source, float[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = source.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                    break;
                offset += read;
            }
            return offset / 2;
        }

        private static double EstimateBandwidth(double[] power, int blockCount, int hz)
        {
            var binHz = (double)hz / FftSize;

            var fullScale = Math.Pow(FftSize / 4.0, 2);
            var halfWidth = Math.Max(1, (int)(150 / binHz));
            var smoothed = new double[power.Length];
            for (var k = 0; k < power.Length; k++)
            {
                var from = Math.Max(0, k - halfWidth);
                var to = Math.Min(power.Length - 1, k + halfWidth);
                var sum = 0.0;
                for (var j = from; j <= to; j++)
                    sum += power[j];
                var average = sum / (to - from + 1) / blockCount / fullScale;
                smoothed[k] = 10 * Math.Log10(average + 1e-30);
            }

            var referenceTo = Math.Min((int)(8000 / binHz), (int)(power.Length * 0.8));
            var reference = double.MinValue;
            for (var k = (int)(100 / binHz); k <= referenceTo; k++)
                reference = Math.Max(reference, smoothed[k]);

            var threshold = Math.Max(reference - BandwidthThresholdDb, NoiseFloorDbfs);
            for (var k = power.Length - 1; k > 0; k--)
            {
                if (smoothed[k] > threshold)
                    return Math.Min(k * binHz, hz / 2.0);
            }
            return 0;
        }

        private static double[] CreateHannWindow(int size)
        {
            var window = new double[size];
            for (var i = 0; i < size; i++)
                window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);
            return window;
        }

        private static void Fft(double[] re, double[] im)
        {
            var n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                var bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (var length = 2; length <= n; length <<= 1)
            {
                var angle = -2 * Math.PI / length;
                var stepRe = Math.Cos(angle);
                var stepIm = Math.Sin(angle);
                var half = length / 2;
                for (var i = 0; i < n; i += length)
                {
                    var wRe = 1.0;
                    var wIm = 0.0;
                    for (var k = 0; k < half; k++)
                    {
                        var a = i + k;
                        var b = a + half;
                        var xRe = re[b] * wRe - im[b] * wIm;
                        var xIm = re[b] * wIm + im[b] * wRe;
                        re[b] = re[a] - xRe;
                        im[b] = im[a] - xIm;
                        re[a] += xRe;
                        im[a] += xIm;
                        var nextRe = wRe * stepRe - wIm * stepIm;
                        wIm = wRe * stepIm + wIm * stepRe;
                        wRe = nextRe;
                    }
                }
            }
        }
    }
}
