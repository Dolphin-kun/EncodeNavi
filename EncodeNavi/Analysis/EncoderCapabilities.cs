using System.Collections.Concurrent;
using System.Diagnostics;
using FFmpeg.AutoGen;
using Vortice.DXGI;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg;

namespace EncodeNavi.Analysis
{
    internal record EncoderCapabilities(IReadOnlySet<string> SoftwareEncoders, string? HardwareH264Encoder, string? HardwareAv1Encoder = null)
    {
        private const long MinDiscreteVideoMemory = 512L * 1024 * 1024;
        private const int NvidiaVendorId = 0x10DE;
        private const int AmdVendorId = 0x1002;
        private const int IntelVendorId = 0x8086;

        private static readonly string[] SoftwareCandidates = ["libx264", "libsvtav1", "libvpx-vp9", "libopenh264"];

        private static readonly string[] HardwareH264Candidates = ["h264_nvenc", "h264_qsv", "h264_amf"];

        private static readonly string[] HardwareAv1Candidates = ["av1_nvenc", "av1_qsv", "av1_amf"];

        private static readonly ConcurrentDictionary<string, string?> hardwareCache = new();

        private static readonly Lazy<HashSet<int>> discreteGpuVendors = new(DetectDiscreteGpuVendors);

        public static int CpuThreads => Environment.ProcessorCount;

        public bool Has(string encoder) => SoftwareEncoders.Contains(encoder);

        public static async Task<EncoderCapabilities> DetectAsync(int width, int height, CancellationToken token)
        {
            var software = await Task.Run(DetectSoftwareEncoders, token);
            var hardware = await Task.WhenAll(
                DetectHardwareEncoderAsync(HardwareH264Candidates, width, height, token),
                DetectHardwareEncoderAsync(HardwareAv1Candidates, width, height, token));
            return new EncoderCapabilities(software, hardware[0], hardware[1]);
        }

        private static unsafe IReadOnlySet<string> DetectSoftwareEncoders()
        {
            MediaProbe.EnsureInitialized();
            return SoftwareCandidates.Where(x => ffmpeg.avcodec_find_encoder_by_name(x) != null).ToHashSet();
        }

        private static async Task<string?> DetectHardwareEncoderAsync(string[] candidates, int width, int height, CancellationToken token)
        {
            var key = $"{candidates[0]}:{width}x{height}";
            if (hardwareCache.TryGetValue(key, out var cached))
                return cached;

            var discrete = discreteGpuVendors.Value;
            var ordered = candidates.OrderByDescending(x => discrete.Contains(VendorOf(x))).ToArray();
            var results = await Task.WhenAll(ordered.Select(x => ProbeAsync(x, width, height, token)));
            var encoder = ordered.Where((_, i) => results[i]).FirstOrDefault();
            hardwareCache[key] = encoder;
            return encoder;
        }

        private static int VendorOf(string encoder) =>
            encoder.Contains("nvenc") ? NvidiaVendorId
            : encoder.Contains("amf") ? AmdVendorId
            : encoder.Contains("qsv") ? IntelVendorId
            : 0;

        private static HashSet<int> DetectDiscreteGpuVendors()
        {
            var vendors = new HashSet<int>();
            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                for (var i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
                {
                    using (adapter)
                    {
                        var description = adapter.Description1;
                        if ((description.Flags & AdapterFlags.Software) == 0 && (long)description.DedicatedVideoMemory >= MinDiscreteVideoMemory)
                            vendors.Add((int)description.VendorId);
                    }
                }
            }
            catch
            {
            }
            return vendors;
        }

        private static async Task<bool> ProbeAsync(string encoder, int width, int height, CancellationToken token)
        {
            var startInfo = new ProcessStartInfo(FFmpegResourceLocator.GetFFmpegExePath())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            string[] arguments =
            [
                "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", $"color=c=black:s={width}x{height}:r=30",
                "-frames:v", "1", "-pix_fmt", "yuv420p",
                "-c:v", encoder, "-f", "null", "-",
            ];
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                    return false;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));

                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                    await Task.WhenAll(output, error);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    token.ThrowIfCancellationRequested();
                    return false;
                }
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }
    }
}
