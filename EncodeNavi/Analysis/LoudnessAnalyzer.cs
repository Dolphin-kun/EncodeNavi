using YukkuriMovieMaker.Player.Audio.Effects;

namespace EncodeNavi.Analysis
{
    internal static class LoudnessAnalyzer
    {
        private const double AbsoluteGateLufs = -70;
        private const double RelativeGateLu = -10;
        private const double SilencePeakDb = -90;

        public static LoudnessInfo? Analyze(IAudioStream stream, long totalSamples, IProgress<double>? progress, CancellationToken token)
        {
            var hz = stream.Hz;
            var stepFrames = hz / 10;
            var filters = new[] { new KWeighting(hz), new KWeighting(hz) };
            var stepEnergies = new List<double>();
            var buffer = new float[stepFrames * 2];
            var peak = 0f;
            var stepEnergy = 0.0;
            var stepFilled = 0;
            long read = 0;

            stream.Seek(0L);
            while (read < totalSamples)
            {
                token.ThrowIfCancellationRequested();
                var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, (totalSamples - read) * 2));
                if (count <= 0)
                    break;

                for (var i = 0; i + 1 < count; i += 2)
                {
                    var left = buffer[i];
                    var right = buffer[i + 1];
                    peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));
                    var l = filters[0].Process(left);
                    var r = filters[1].Process(right);
                    stepEnergy += l * l + r * r;
                    if (++stepFilled == stepFrames)
                    {
                        stepEnergies.Add(stepEnergy / stepFrames);
                        stepEnergy = 0;
                        stepFilled = 0;
                    }
                }
                read += count / 2;
                progress?.Report(Math.Min(1.0, (double)read / totalSamples));
            }

            var peakDb = peak > 0 ? 20 * Math.Log10(peak) : double.NegativeInfinity;
            if (peakDb < SilencePeakDb || stepEnergies.Count < 4)
                return null;

            var blocks = new List<double>();
            for (var i = 0; i + 4 <= stepEnergies.Count; i++)
                blocks.Add((stepEnergies[i] + stepEnergies[i + 1] + stepEnergies[i + 2] + stepEnergies[i + 3]) / 4);

            var aboveAbsolute = blocks.Where(x => ToLufs(x) > AbsoluteGateLufs).ToList();
            if (aboveAbsolute.Count == 0)
                return null;
            var relativeGate = ToLufs(aboveAbsolute.Average()) + RelativeGateLu;
            var gated = aboveAbsolute.Where(x => ToLufs(x) > relativeGate).ToList();
            if (gated.Count == 0)
                return null;

            return new LoudnessInfo(ToLufs(gated.Average()), peakDb);
        }

        private static double ToLufs(double energy) => energy > 0 ? -0.691 + 10 * Math.Log10(energy) : double.NegativeInfinity;

        private sealed class KWeighting
        {
            private readonly Biquad shelf;
            private readonly Biquad highPass;

            public KWeighting(int hz)
            {
                var k = Math.Tan(Math.PI * 1681.974450955533 / hz);
                var q = 0.7071752369554196;
                var vh = Math.Pow(10, 3.999843853973347 / 20);
                var vb = Math.Pow(vh, 0.4996667741545416);
                var a0 = 1 + k / q + k * k;
                shelf = new Biquad(
                    (vh + vb * k / q + k * k) / a0,
                    2 * (k * k - vh) / a0,
                    (vh - vb * k / q + k * k) / a0,
                    2 * (k * k - 1) / a0,
                    (1 - k / q + k * k) / a0);

                k = Math.Tan(Math.PI * 38.13547087602444 / hz);
                q = 0.5003270373238773;
                a0 = 1 + k / q + k * k;
                highPass = new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
            }

            public double Process(double sample) => highPass.Process(shelf.Process(sample));
        }

        private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            private double z1;
            private double z2;

            public double Process(double input)
            {
                var output = b0 * input + z1;
                z1 = b1 * input - a1 * output + z2;
                z2 = b2 * input - a2 * output;
                return output;
            }
        }
    }
}
