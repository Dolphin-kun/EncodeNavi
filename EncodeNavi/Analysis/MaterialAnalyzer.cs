using System.Collections.Concurrent;
using System.IO;
using YukkuriMovieMaker.Plugin;

namespace EncodeNavi.Analysis
{
    internal static class MaterialAnalyzer
    {
        private static readonly ConcurrentDictionary<(string Path, int Track, bool UsesAudio, long Length, DateTime LastWriteTime), MaterialInfo> cache = new();

        public static async Task<AnalysisResult> AnalyzeAsync(IReadOnlyList<MaterialReference> references, ProjectInfo project, IProgress<string> progress, CancellationToken token)
        {
            var encodersTask = EncoderCapabilities.DetectAsync(project.Width, project.Height, token);

            var materials = new MaterialInfo[references.Count];
            var completed = 0;
            progress.Report($"素材を解析中… 0/{references.Count}");

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
                CancellationToken = token,
            };
            await Parallel.ForEachAsync(Enumerable.Range(0, references.Count), options, (i, ct) =>
            {
                materials[i] = Analyze(references[i], ct);
                progress.Report($"素材を解析中… {Interlocked.Increment(ref completed)}/{references.Count}");
                return ValueTask.CompletedTask;
            });

            progress.Report("使用できるエンコーダを確認中…");
            var detected = await encodersTask;
            progress.Report("このPCのエンコード速度を測定中…");
            var encoders = await detected.MeasureSpeedAsync(project.Width, project.Height, token);
            return new AnalysisResult(materials, encoders);
        }

        private static MaterialInfo Analyze(MaterialReference reference, CancellationToken token)
        {
            var file = new FileInfo(reference.FilePath);
            if (!file.Exists)
                return new MaterialInfo(reference, null, null, "ファイルが見つかりません");

            var key = (file.FullName.ToLowerInvariant(), reference.AudioTrackIndex, reference.UsesAudio, file.Length, file.LastWriteTimeUtc);
            if (cache.TryGetValue(key, out var cached))
                return cached with { Reference = reference };

            var result = AnalyzeFile(reference, token);
            if (result.Error is null)
                cache[key] = result;
            return result;
        }

        private static MaterialInfo AnalyzeFile(MaterialReference reference, CancellationToken token)
        {
            try
            {
                var probe = MediaProbe.Probe(reference.FilePath, reference.AudioTrackIndex);

                AudioContentInfo? content = null;
                if (reference.UsesAudio && probe.Audio != null)
                {
                    using var source = AudioFileSourceFactory.Create(reference.FilePath, reference.AudioTrackIndex);
                    if (source != null)
                        content = AudioContentAnalyzer.Analyze(source, token);
                }

                return new MaterialInfo(reference, probe, content, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new MaterialInfo(reference, null, null, ex.Message);
            }
        }
    }
}
