using System.IO;
using EncodeNavi.Analysis;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg;
using YukkuriMovieMaker.VideoFileWriter;

namespace EncodeNavi.Recommendation
{
    internal static class Recommender
    {
        private static readonly int[] StandardShortEdges = [360, 480, 540, 720, 1080, 1440, 2160, 4320];
        private static readonly int[] AudioKbpsSteps = [64, 96, 128, 160, 192, 256, 320, 384];
        private static readonly int[] StandardFps = [24, 25, 30, 48, 50, 60, 90, 120, 144, 240];

        private const int HardwareVideoQuality = 53;
        private const int HardwareAv1Quality = 52;

        private const string CommandModeNote =
            "下の映像/音声コマンドを使うときは、YMM4のFFmpeg出力設定で「映像ビットレート」を「指定しない」、「ハードウェアエンコード」をOFFにしてください。そのままだとYMM4側でcrf/preset/ビットレートの指定が上書き・削除されます。" +
            "コマンド欄の下の「プレビュー」はこの削除の前の内容なので、プレビューに出ていてもハードウェアエンコードがONだと実際の出力では消えます。";

        private const string AudioBitRateNote =
            "YMM4の「音声ビットレート」の選択肢はどれでも構いません（音声コマンドの -b:a が優先されます。選択肢にない値も指定できます）。";

        private const string ConvertedByCommand = "（コマンドで変換）";

        private record VideoPlan(
            string Encoder,
            string EncoderReason,
            string Options,
            string Quality,
            string QualityReason,
            string Preset,
            string PresetReason,
            string? HardwareEncoder,
            int? Kbps,
            int? HardwareQuality = null);

        private record SourceSummary(
            IReadOnlyList<VideoStreamInfo> Videos,
            IReadOnlyList<MaterialInfo> Audios);

        private record FormatPlan(
            int Width,
            int Height,
            int Fps,
            int Hz,
            bool IsMono,
            string ChannelReason,
            int AudioKbps,
            string AudioReason);

        public static EncodePlan Recommend(EncodeGoal goal, TargetPlatform platform, OutputMethod method, ProjectInfo project, AnalysisResult analysis, CurrentOutputSettings current)
        {
            var spec = goal == EncodeGoal.Platform ? PlatformSpec.Get(platform) : null;
            var sources = Summarize(analysis);
            var notes = new List<string>();
            var items = new List<RecommendationItem>();
            var isMediaFoundation = method == OutputMethod.MediaFoundation;

            var changeHint = isMediaFoundation ? "（プロジェクト設定で変更）" : ConvertedByCommand;
            var formatTarget = isMediaFoundation ? OutputSettingTargets.General : OutputSettingTargets.VideoOptions;

            var (width, height, resolutionReason) = RecommendResolution(goal, spec, project, sources);
            if (isMediaFoundation && (width != project.Width || height != project.Height))
                resolutionReason += "。※MediaFoundation出力は出力時に解像度を変換できないので、プロジェクト設定で変更する。文字や立ち絵の配置・大きさも変わるので、変更後に見た目を確認すること";
            items.Add(new("解像度", $"{width}x{height}", $"{project.Width}x{project.Height}", resolutionReason, changeHint, !isMediaFoundation) { Target = formatTarget });

            var (fps, fpsReason) = RecommendFps(goal, spec, project, sources);
            if (isMediaFoundation && fps != project.Fps)
                fpsReason += "。※MediaFoundation出力は出力時にfpsを変換できないので、プロジェクト設定で変更する。変更後にアイテムのタイミングがずれていないか確認すること";
            items.Add(new("フレームレート", $"{fps} fps", $"{project.Fps} fps", fpsReason, changeHint, !isMediaFoundation) { Target = formatTarget });

            var (hz, hzReason) = RecommendSampleRate(goal, spec, project, sources);
            items.Add(new("サンプリングレート", $"{hz} Hz", $"{project.Hz} Hz", hzReason, changeHint, !isMediaFoundation)
            {
                Target = isMediaFoundation ? OutputSettingTargets.General : OutputSettingTargets.AudioOptions,
            });

            var (isMono, channelReason) = RecommendChannels(sources);
            var (audioKbps, audioReason) = RecommendAudioBitRate(goal, spec, isMono, sources);
            var format = new FormatPlan(width, height, fps, hz, isMono, channelReason, audioKbps, audioReason);

            if (isMediaFoundation)
                AddMediaFoundationItems(goal, spec, project, analysis, sources, current, format, items, notes);
            else
                return RecommendFFmpeg(goal, spec, project, analysis, sources, current, format, items, notes);

            AddLoudnessItems(spec, analysis, current, items);

            AddAnalysisNotes(analysis, sources, notes);
            return new EncodePlan(items, "", "", notes);
        }

        private static EncodePlan RecommendFFmpeg(EncodeGoal goal, PlatformSpec? spec, ProjectInfo project, AnalysisResult analysis, SourceSummary sources,
            CurrentOutputSettings current, FormatPlan format, List<RecommendationItem> items, List<string> notes)
        {
            var (width, height, fps, hz, isMono, channelReason, audioKbps, audioReason) = format;

            var video = goal switch
            {
                EncodeGoal.Size => PlanSizeVideo(width, height, fps, analysis.Encoders, sources),
                EncodeGoal.Platform => PlanPlatformVideo(width, height, fps, analysis.Encoders, spec!),
                _ => PlanSpeedVideo(width, height, fps, analysis.Encoders),
            };
            var videoBitRateSetting = video.HardwareEncoder != null
                ? CurrentOutputSettings.FormatVideoBitRateSetting(VideoBitRateMode.Custom, VideoBitRateControlMode.Quality, video.HardwareQuality)
                : "指定しない";

            var encoderCurrent = current.VideoEncoder == GetCodec(video.Options) ? video.Encoder : current.VideoEncoder;
            var qualityCurrent = video.HardwareEncoder != null && current.VideoBitRateSetting == videoBitRateSetting ? video.Quality : current.VideoQuality;
            var qualityTarget = video.HardwareEncoder != null ? OutputSettingTargets.VideoBitRateMode : OutputSettingTargets.VideoOptions;

            items.Add(new("映像エンコーダ", video.Encoder, encoderCurrent, video.EncoderReason) { Target = OutputSettingTargets.VideoOptions });
            items.Add(new("映像品質", video.Quality, qualityCurrent, video.QualityReason) { Target = qualityTarget });
            items.Add(new("preset", video.Preset, current.Preset, video.PresetReason) { Target = OutputSettingTargets.VideoOptions });
            items.Add(new("ハードウェアエンコード", video.HardwareEncoder != null ? "ON" : "OFF", current.HardwareEncoder, HardwareReason(goal, video, analysis.Encoders, fps))
            {
                Target = OutputSettingTargets.FFmpegHardwareEncoder,
            });

            items.Add(new("音声チャンネル", isMono ? "モノラル" : "ステレオ", current.Channels, channelReason) { Target = OutputSettingTargets.AudioOptions });
            items.Add(new("音声ビットレート", $"{audioKbps} kbps", current.AudioBitRate, audioReason) { Target = OutputSettingTargets.AudioOptions });

            items.Add(new("YMM4の映像ビットレート設定", videoBitRateSetting, current.VideoBitRateSetting,
                video.HardwareEncoder != null
                    ? "ハードウェアエンコードでは、この設定からYMM4がGPUエンコーダ用のレート制御を組み立てるため"
                    : "映像コマンドの指定をそのまま使わせるため（「指定しない」以外だとYMM4がビットレート指定を追加・上書きする）")
            {
                Target = OutputSettingTargets.VideoBitRateMode,
            });

            var videoOptions = video.Options;
            if (width != project.Width || height != project.Height)
                videoOptions += $" -vf scale={width}:{height}:flags=lanczos";
            if (fps != project.Fps)
                videoOptions += $" -r {fps}";

            var audioOptions = $"-c:a aac -b:a {audioKbps}k"
                + (isMono ? " -ac 1" : "")
                + (hz != project.Hz ? $" -ar {hz}" : "");

            if (video.HardwareEncoder != null)
                notes.Add($"YMM4のFFmpeg出力設定で「ハードウェアエンコード」をON、「映像ビットレート」を「手動」、種類を「映像品質」にして値を{video.HardwareQuality}にしてください。映像コマンドにcrfなどを書いてもYMM4が削除します。");
            else
                notes.Add(CommandModeNote);
            notes.Add(AudioBitRateNote);
            notes.Add("設定し終えたら「プリセット」横の保存ボタンで保存しておくと、次回は選ぶだけで使えます。");

            if (video.Encoder.Contains("AV1"))
                notes.Add("AV1は容量効率が高い反面、古い端末やソフト・一部の動画編集ソフトでは再生/読み込みできないことがあります。互換性を優先するなら「投稿先に最適化」(H.264)を選んでください。");

            if (spec != null)
                AddPlatformLimitNotes(spec, project, video.Kbps, audioKbps, notes);

            AddLoudnessItems(spec, analysis, current, items);

            AddAnalysisNotes(analysis, sources, notes);

            return new EncodePlan(items, videoOptions, audioOptions, notes);
        }

        private static string? GetCodec(string options)
        {
            var tokens = options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var index = Array.IndexOf(tokens, "-c:v");
            return index >= 0 && index + 1 < tokens.Length ? tokens[index + 1] : null;
        }

        private static void AddMediaFoundationItems(EncodeGoal goal, PlatformSpec? spec, ProjectInfo project, AnalysisResult analysis, SourceSummary sources,
            CurrentOutputSettings current, FormatPlan format, List<RecommendationItem> items, List<string> notes)
        {
            var hardware = analysis.Encoders.HardwareH264Encoder;
            var useHardware = goal == EncodeGoal.Speed && hardware != null;
            int? videoKbps = null;

            string videoBitRate, videoBitRateReason;
            if (goal == EncodeGoal.Platform)
            {
                var kbps = spec!.GetVideoKbps(Math.Min(format.Width, format.Height), format.Fps);
                videoKbps = kbps;
                videoBitRate = CurrentOutputSettings.FormatMediaFoundationVideoBitRate(MediaFoundationRateControl.AverageBitRate, kbps, 0);
                videoBitRateReason = $"{spec.VideoBitRateSource}（{Math.Min(format.Width, format.Height)}p・{format.Fps}fps）。投稿先で再エンコードされるので、元の画質を十分に残せる値にする。" +
                    "「自動」はファイルサイズ最大3GBを目標にするため、必要以上に大きくなりやすい";
            }
            else
            {
                var isHighlyCompressed = sources.Videos.Count > 0 && sources.Videos.All(x => x.BitRate > 0)
                    && sources.Videos.Max(x => x.BitRate / ((double)x.Width * x.Height * Math.Max(x.Fps, 1))) < 0.04;
                var quality = isHighlyCompressed ? 55 : 60;
                videoBitRate = CurrentOutputSettings.FormatMediaFoundationVideoBitRate(MediaFoundationRateControl.Quality, 0, quality);
                videoBitRateReason = "「映像品質」は動きの少ない場面ではビットレートを自動で下げるので、立ち絵や静止画が中心の動画ほど小さくなる。" +
                    $"「自動」はファイルサイズ最大3GBを目標にするため大きくなりやすい。YMM4の初期値70は、動きの多い映像だと素材より大きくなることがあり、{quality}でも見た目の差は小さい" +
                    (isHighlyCompressed ? "（動画素材がすでに高圧縮なので少し下げている）" : "");
            }
            items.Add(new("映像ビットレート", videoBitRate, current.VideoBitRateSetting, videoBitRateReason) { Target = OutputSettingTargets.VideoBitRateMode });

            items.Add(goal == EncodeGoal.Speed
                ? new("エンコード速度", "100", current.EncodeSpeed, "大きいほど速くなるが画質が落ちる（YMM4の説明より）。速度優先なので最大にする") { Target = OutputSettingTargets.EncodeSpeed }
                : new("エンコード速度", "50", current.EncodeSpeed, "YMM4の初期値。大きくすると速くなるが画質が落ちる") { Target = OutputSettingTargets.EncodeSpeed });

            var hardwareReason = useHardware
                ? $"このPCではGPUエンコーダ（{hardware}）が使えるので、エンコードがCPUより大幅に速い{RenderBoundNote}"
                : hardware != null
                    ? $"GPUエンコードは環境によって画質が落ちる場合があるので（YMM4の説明より）、安定した画質にするためOFF。このPCでは{hardware}が使えるので、急ぐならONでもよい"
                    : "このPCで使えるGPUエンコーダが見つからなかった";
            items.Add(new("ハードウェアエンコード", useHardware ? "ON" : "OFF", current.HardwareEncoder, hardwareReason) { Target = OutputSettingTargets.MediaFoundationHardwareEncoder });

            items.Add(new("音声チャンネル", "ステレオ", current.Channels, format.IsMono
                ? "素材は実質モノラルだが、MediaFoundation出力はモノラルを選べない。左右が同じ音ならAACは効率よく圧縮できるので、ビットレートを抑えれば容量の無駄は小さい。モノラルにしたい場合はFFmpeg 出力を使う"
                : format.ChannelReason));

            var audioKbps = SnapToAudioChoice(format.AudioKbps);
            var audioReason = audioKbps == format.AudioKbps
                ? format.AudioReason
                : $"{format.AudioReason}。YMM4の選択肢にある値のうち、それ以上で最も近い{audioKbps} kbpsにする";
            items.Add(new("音声ビットレート", $"{audioKbps} kbps", current.AudioBitRate, audioReason) { Target = OutputSettingTargets.AudioBitRate });

            items.Add(new("ノイズ対策", "ON", current.NoiseFix,
                "OFFだとWindowsのAACエンコーダになり、音声に高周波ノイズがのる可能性がある（YMM4の説明より）。OFFでは96〜192kbps・44100/48000Hzにしか対応しない")
            {
                Target = OutputSettingTargets.NoiseFix,
            });

            notes.Add("MediaFoundation出力はH.264固定で、映像/音声コマンドの欄はありません。上の値をYMM4の動画出力画面（「エンコード設定」と「詳細設定」）で設定してください。");
            if (goal == EncodeGoal.Size)
                notes.Add("さらに容量を減らしたい場合は、出力方法をFFmpeg 出力にするとAV1やモノラル音声が使え、同じ画質でより小さくできます。");

            if (spec != null)
                AddPlatformLimitNotes(spec, project, videoKbps, audioKbps, notes);
        }

        private const double DefaultTargetLufs = -14;
        private static readonly (int Post, int Delivered)[] NiconicoAudioTiers = [(128, 128), (192, 192), (256, 320)];
        private const double TargetPeakLimitDb = -1;
        private const double LoudnessTolerance = 1;

        private static void AddLoudnessItems(PlatformSpec? spec, AnalysisResult analysis, CurrentOutputSettings current, List<RecommendationItem> items)
        {
            if (analysis.Loudness is not { } loudness)
                return;

            var targetLufs = spec?.TargetLufs ?? DefaultTargetLufs;
            var measured = $"今のミックスは {loudness.IntegratedLufs:0.0} LUFS（ピーク {loudness.PeakDb:0.0} dBFS）";
            var difference = targetLufs - loudness.IntegratedLufs;
            var targetReason = spec?.LoudnessReason ?? "動画サイトでよく使われる -14 LUFS に合わせると、他の動画と同じくらいの大きさで聞こえる";
            var modeReason = Math.Abs(difference) <= LoudnessTolerance
                ? $"{measured}で、ほぼ目標どおり。素材を差し替えても揃うように、出力時に目標音量へ合わせる設定にしておく"
                : difference > 0
                    ? $"{measured}で、目標より {difference:0.0} dB 小さい。YMM4の初期設定「最大音量を0dB以下にする」では小さい音は大きくならないので、「目標音量にする」で持ち上げる"
                    : $"{measured}で、目標より {-difference:0.0} dB 大きい。「目標音量にする」で下げて、聞き疲れしにくい大きさにする";

            items.Add(new("音量調整", EnumText.Get(LoudnessFixMode.Both), current.LoudnessMode, modeReason) { Target = OutputSettingTargets.LoudnessMode });
            items.Add(new("目標音量", CurrentOutputSettings.FormatLufs(targetLufs), current.LoudnessTarget, targetReason) { Target = OutputSettingTargets.LoudnessTarget });

            var predictedPeak = loudness.PeakDb + difference;
            var compressorReason = predictedPeak > TargetPeakLimitDb
                ? $"目標音量に合わせると最大で {predictedPeak:0.0} dBFS になり音割れするので、最大音量を超える部分だけ自動で圧縮する"
                : $"目標音量に合わせても最大 {predictedPeak:0.0} dBFS で音割れしないので通常は圧縮されないが、素材を足したときのために自動にしておく";
            items.Add(new("コンプレッサー", EnumText.Get(CompressorRatioMode.Auto), current.CompressorMode, compressorReason) { Target = OutputSettingTargets.CompressorMode });
            items.Add(new("最大音量", CurrentOutputSettings.FormatDb(TargetPeakLimitDb), current.PeakLimit,
                "AACなどに圧縮すると波形の山がわずかに大きくなり、0dB近くまで使うと再生時に音割れすることがあるので、1dBの余裕を持たせる")
            {
                Target = OutputSettingTargets.PeakLimit,
            });
        }

        private static readonly int[] AudioBitRateChoices = [96, 128, 160, 192, 256, 320, 384, 512, 576];

        private static int SnapToAudioChoice(int kbps) => AudioBitRateChoices.FirstOrDefault(x => x >= kbps, AudioBitRateChoices[^1]);

        private static SourceSummary Summarize(AnalysisResult analysis)
        {
            var videos = analysis.Materials
                .Where(x => x.Reference.UsesVideo && x.Probe?.Video is { IsStillImage: false, Width: > 0, Height: > 0 })
                .Select(x => x.Probe!.Video!)
                .ToList();
            var audios = analysis.Materials
                .Where(x => x.Reference.UsesAudio && x.Probe?.Audio != null && x.AudioContent?.IsSilent != true)
                .ToList();
            return new SourceSummary(videos, audios);
        }

        private static (int Width, int Height, string Reason) RecommendResolution(EncodeGoal goal, PlatformSpec? spec, ProjectInfo project, SourceSummary sources)
        {
            var projectText = $"{project.Width}x{project.Height}";
            var projectShortEdge = Math.Min(project.Width, project.Height);

            if (goal == EncodeGoal.Speed)
                return (project.Width, project.Height, "解像度は画質に直結するため変えない。それでも遅い場合は、プロジェクト設定の解像度を下げるとYMM4の描画・エンコードとも速くなる");

            if (spec != null)
            {
                var longEdge = Math.Max(project.Width, project.Height);
                var scale = Math.Min(1.0, Math.Min((double)spec.MaxLongEdge / longEdge, (double)spec.MaxShortEdge / projectShortEdge));
                if (scale < 1)
                {
                    var (w, h) = Scale(project, scale);
                    return spec.Platform == TargetPlatform.Niconico
                        ? (w, h, $"ニコニコ動画の配信は最高1080pで（公式）、それより大きく投稿しても配信画質は変わらないため縮小する（アップロードも軽くなる）")
                        : (w, h, $"{spec.Name}の上限（{spec.MaxLongEdge}x{spec.MaxShortEdge}）に収まるように縮小");
                }
                return spec.Platform == TargetPlatform.Niconico
                    ? (project.Width, project.Height, $"プロジェクト解像度（{projectText}）のまま" + NiconicoTierNote(project.Width, project.Height))
                    : (project.Width, project.Height, $"{spec.Name}の上限内なのでプロジェクト解像度（{projectText}）のまま。投稿先で再エンコードされるので、大きめで投稿した方が画質が残りやすい");
            }

            if (sources.Videos.Count == 0)
                return (project.Width, project.Height, "動画素材がないため、プロジェクト解像度のまま（文字や画像はこの解像度で描画される）");

            var largest = sources.Videos.MaxBy(x => (long)x.Width * x.Height)!;
            var sourceShortEdge = Math.Min(largest.Width, largest.Height);
            if (sourceShortEdge >= projectShortEdge)
                return (project.Width, project.Height, $"動画素材（最大 {largest.Width}x{largest.Height}）がプロジェクト解像度以上なので、そのまま");

            var targetShortEdge = Math.Min(StandardShortEdges.First(x => x >= sourceShortEdge), projectShortEdge);
            var (width, height) = Scale(project, (double)targetShortEdge / projectShortEdge);
            if (width == project.Width && height == project.Height)
                return (width, height, $"動画素材（最大 {largest.Width}x{largest.Height}）に近い標準解像度がプロジェクト解像度なので、そのまま");

            return (width, height,
                $"動画素材は最大 {largest.Width}x{largest.Height} で、それ以上の解像度にしても素材の情報量は増えないため。" +
                $"ただし文字・立ち絵などYMM4で描画する要素もこの解像度になるので、文字のくっきりさを優先するならプロジェクト解像度（{projectText}）のままでもよい");
        }

        private static string NiconicoTierNote(int width, int height)
        {
            var (tier, nextCondition, nextTier) = PlatformSpec.GetNiconicoTier(width, height);
            var note = $"。ニコニコ動画は投稿した解像度で配信画質が決まり（公式）、{width}x{height}なら最高{tier}pで配信される";
            return nextCondition is null
                ? note
                : note + $"。{nextTier}pでも配信するには{nextCondition}が必要なので、プロジェクト設定の解像度を上げるとよい";
        }

        private static (int Width, int Height) Scale(ProjectInfo project, double scale) =>
            (ToEven(project.Width * scale), ToEven(project.Height * scale));

        private static int ToEven(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);

        private static (int Fps, string Reason) RecommendFps(EncodeGoal goal, PlatformSpec? spec, ProjectInfo project, SourceSummary sources)
        {
            if (goal == EncodeGoal.Speed)
                return (project.Fps, "fpsを下げると動きの滑らかさが変わるため、プロジェクト設定のまま");

            if (spec != null)
            {
                if (project.Fps > spec.MaxFps)
                    return (spec.MaxFps, $"{spec.Name}は{spec.MaxFps}fpsまでのため");
                return (project.Fps, $"{spec.Name}の上限（{spec.MaxFps}fps）内なので、プロジェクト設定のまま");
            }

            if (sources.Videos.Count == 0)
            {
                return project.Fps > 30
                    ? (project.Fps, "動画素材がないため、プロジェクト設定のまま。動きが少ない動画なら30fpsに下げるとさらに容量を減らせる")
                    : (project.Fps, "動画素材がないため、プロジェクト設定のまま");
            }

            var sourceFps = sources.Videos.Max(x => x.Fps);
            var standardFps = StandardFps.FirstOrDefault(x => x >= sourceFps * 0.98, (int)Math.Ceiling(sourceFps));
            if (standardFps >= project.Fps)
                return (project.Fps, $"動画素材（最大 {sourceFps:0.##} fps）がプロジェクト以上なので、そのまま");

            var fps = Math.Min(project.Fps, standardFps >= 24 ? standardFps : 30);
            return (fps,
                $"動画素材は最大 {sourceFps:0.##} fps で、それ以上にしても素材にない動きは増えず容量だけが増えるため。" +
                "※立ち絵の口パクやアニメーションなどYMM4上の動きもこのfpsになる");
        }

        private static (int Hz, string Reason) RecommendSampleRate(EncodeGoal goal, PlatformSpec? spec, ProjectInfo project, SourceSummary sources)
        {
            const string sizeNote = "（AACのファイルサイズはビットレートで決まるので、サンプリングレートを下げても小さくはならない）";

            if (spec != null)
                return (spec.SampleRate, $"{spec.Name}の推奨は{spec.SampleRate}Hz");

            if (project.Hz is not (44100 or 48000))
                return (48000, $"プロジェクトの{project.Hz}Hzは動画では一般的でないため、標準的な48000Hzに変換する" + sizeNote);

            var reason = "YMM4はプロジェクトのサンプリングレートで音声を合成するので、出力もそのままにする（変換すると再サンプリングが1回増える）";
            var rates = sources.Audios.Select(x => x.Probe!.Audio!.SampleRate).Where(x => x > 0).Distinct().ToList();
            if (rates.Count == 1 && rates[0] is 44100 or 48000 && rates[0] != project.Hz)
                reason += $"。なお音声素材はすべて{rates[0]}Hzなので、プロジェクト設定を{rates[0]}Hzにすれば素材の再サンプリング自体をなくせる";
            return (project.Hz, reason + sizeNote);
        }

        private static (bool IsMono, string Reason) RecommendChannels(SourceSummary sources)
        {
            if (sources.Audios.Count == 0)
                return (false, "解析できた音声素材がないため、ステレオのまま");

            var stereo = sources.Audios.Where(x => !IsMono(x)).ToList();
            if (stereo.Count > 0)
            {
                var names = string.Join("、", stereo.Take(2).Select(x => Path.GetFileName(x.Reference.FilePath)));
                return (false, $"左右で違う音が入っている素材があるため（{names}{(stereo.Count > 2 ? " など" : "")}）");
            }

            if (sources.Audios.Any(x => x.Reference.IsPanned))
                return (false, "素材はすべてモノラルだが、パン（左右の定位）を使っているアイテムがあるため");

            return (true,
                "すべての音声素材が実質モノラル（左右が同じ音）で、パンも使われていないため。モノラルにすると同じ音質で音声の容量を約半分にできる。" +
                "※ボイスアイテムや音声エフェクトで左右差をつけている場合はステレオのままにする");
        }

        private static bool IsMono(MaterialInfo material) =>
            material.Probe!.Audio!.Channels == 1 || material.AudioContent?.IsMono == true;

        private static (int Kbps, string Reason) RecommendAudioBitRate(EncodeGoal goal, PlatformSpec? spec, bool isMono, SourceSummary sources)
        {
            var reasons = new List<string>();
            var contents = sources.Audios.Select(x => x.AudioContent).OfType<AudioContentInfo>().ToList();

            double? bandwidth = contents.Count > 0 ? contents.Max(x => x.BandwidthHz) : null;
            if (bandwidth is { } bw)
            {
                var cutNote = contents.All(x => x.IsHighCut) ? "（どの素材も高域が途中で切れている＝圧縮音源や低いサンプリングレート由来）" : "";
                reasons.Add($"音声素材に実際に入っている音は最大 {bw / 1000:0.0} kHz まで{cutNote}");
            }
            else
            {
                reasons.Add("音声の中身を解析できなかったため一般的な値");
            }

            var kbps = ContentKbps(isMono, bandwidth);

            var streams = sources.Audios.Select(x => x.Probe!.Audio!).ToList();
            if (streams.Count > 0 && streams.All(x => !x.IsLossless && x.BitRate > 0))
            {
                var sourceKbps = (int)(streams.Max(x => x.BitRate) / 1000);
                var cap = SnapUp(sourceKbps);
                if (cap < kbps)
                {
                    kbps = cap;
                    reasons.Add($"素材は最大 {sourceKbps} kbps の圧縮音声で、それ以上にしても音質は戻らない");
                }
            }

            switch (goal)
            {
                case EncodeGoal.Size:
                    reasons.Add($"この内容なら AAC {kbps} kbps{(isMono ? "（モノラル）" : "")} で聴感上の劣化はほぼない");
                    return (kbps, string.Join("。", reasons));

                case EncodeGoal.Speed:
                    reasons.Add("音声の設定はエンコード時間にほとんど影響しないため、容量優先と同じ値");
                    return (kbps, string.Join("。", reasons));

                case EncodeGoal.Platform when spec!.Platform == TargetPlatform.Niconico:
                    var desired = (int)(kbps * 1.5);
                    var (post, delivered) = NiconicoAudioTiers.FirstOrDefault(x => x.Delivered >= desired, NiconicoAudioTiers[^1]);
                    reasons.Add(spec.AudioNote);
                    reasons.Add(post == NiconicoAudioTiers[^1].Post
                        ? $"{post} kbpsで最高の{delivered} kbps配信になり、それ以上にしても配信音質は変わらない"
                        : $"再エンコードの劣化を見込んで素材相当（{kbps} kbps）より余裕を持たせても、{post} kbpsで作られる{delivered} kbps配信で足りる");
                    return (post, string.Join("。", reasons));

                default:
                    var limit = isMono ? spec!.MonoAudioKbps : spec!.StereoAudioKbps;
                    var withHeadroom = SnapUp((int)(kbps * 1.5));
                    var recommended = Math.Clamp(withHeadroom, Math.Min(spec.MinAudioKbps, limit), limit);
                    reasons.Add(spec.AudioNote);
                    reasons.Add(recommended < limit
                        ? $"投稿先での再エンコードによる劣化を見込んで素材相当（{kbps} kbps）より余裕を持たせたが、素材の音質からはこれ以上は不要"
                        : "投稿先での再エンコードによる劣化を見込み、推奨値いっぱいにする");
                    return (recommended, string.Join("。", reasons));
            }
        }

        private static int ContentKbps(bool isMono, double? bandwidth)
        {
            if (isMono)
                return bandwidth is null or >= 13000 ? 96 : 64;
            return bandwidth switch
            {
                null or >= 17000 => 160,
                >= 13000 => 128,
                _ => 96,
            };
        }

        private static int SnapUp(int kbps) => AudioKbpsSteps.FirstOrDefault(x => x >= kbps, AudioKbpsSteps[^1]);

        private static VideoPlan PlanSizeVideo(int width, int height, int fps, EncoderCapabilities encoders, SourceSummary sources)
        {
            var shortEdge = Math.Min(width, height);

            var bitsPerPixel = sources.Videos.Count > 0 && sources.Videos.All(x => x.BitRate > 0)
                ? sources.Videos.Max(x => x.BitRate / ((double)x.Width * x.Height * Math.Max(x.Fps, 1)))
                : (double?)null;
            var isHighlyCompressed = bitsPerPixel < 0.04;
            var compressedNote = isHighlyCompressed ? $"。動画素材がすでに高圧縮（{bitsPerPixel:0.000} bit/画素）なので、少し上げても見た目の差は出にくい" : "";

            var threads = EncoderCapabilities.CpuThreads;
            var cpuText = $"このPCのCPUは {threads} スレッド";
            var svtAv1 = ChooseSvtAv1Preset(encoders.SvtAv1Fps, fps, threads);
            var isCpuTooSlow = encoders.SvtAv1Fps is { } measured ? measured < fps * SlowCpuRatio : threads < MediumCpuThreads;

            if (isCpuTooSlow && encoders.HardwareAv1Encoder is { } av1Hardware)
            {
                var cq = QualityToHardwareScale(HardwareAv1Quality, 63);
                return new VideoPlan(
                    $"{av1Hardware} (AV1・GPU)",
                    $"{svtAv1.SpeedText}で、ソフトウェアのAV1だと出力にかなり時間がかかる。このPCのGPUはAV1のエンコードに対応しているので、GPUのAV1なら速く、H.264より小さくできる。映像コマンドを -c:v libsvtav1 にしてハードウェアエンコードをONにすると、YMM4がGPUのAV1エンコーダに置き換える",
                    "-c:v libsvtav1",
                    $"{HardwareAv1Quality}（GPUの品質値 {cq} 相当）",
                    $"YMM4の「映像品質」{HardwareAv1Quality}は、AV1のGPUエンコーダの品質値 {cq} 相当（AV1は0〜63の目盛り）。見た目の劣化を抑えつつ容量を減らせる目安",
                    "指定なし",
                    "YMM4がGPUエンコーダを自動選択するので、メーカーごとに書式が違うpresetは指定しない",
                    av1Hardware, null, HardwareAv1Quality);
            }

            if (encoders.Has("libsvtav1"))
            {
                var crf = (shortEdge <= 720 ? 30 : shortEdge <= 1080 ? 32 : shortEdge <= 1440 ? 34 : 36) + (isHighlyCompressed ? 2 : 0);
                var preset = svtAv1.Preset;
                return new VideoPlan(
                    "libsvtav1 (AV1)",
                    "同じ画質ならH.264より大幅に小さくできるAV1を使う。YMM4のFFmpegで画質(CRF)を指定できるソフトウェアエンコーダの中で最も圧縮効率が高い",
                    $"-c:v libsvtav1 -crf {crf} -preset {preset} -pix_fmt yuv420p",
                    $"CRF {crf}",
                    $"SVT-AV1のCRFは小さいほど高画質・大容量。{shortEdge}pでは{crf}前後が、見た目の劣化をほぼ感じずに容量を抑えられる目安{compressedNote}",
                    preset.ToString(),
                    $"SVT-AV1のpresetは0(最遅)〜13(最速)で、小さいほど同じ画質でファイルが小さくなるが時間がかかる。{svtAv1.SpeedText}なので、出力時間とのバランスで{preset}にする",
                    null, null);
            }

            if (encoders.Has("libx264"))
            {
                var crf = (shortEdge <= 720 ? 20 : shortEdge <= 1080 ? 21 : 22) + (isHighlyCompressed ? 2 : 0);
                var preset = GetX264Preset(threads);
                return new VideoPlan(
                    "libx264 (H.264)",
                    "このFFmpegにはAV1エンコーダがないため、CRFで画質を指定できるlibx264を使う",
                    $"-c:v libx264 -crf {crf} -preset {preset} -pix_fmt yuv420p",
                    $"CRF {crf}",
                    $"x264のCRFは小さいほど高画質・大容量。{crf}前後は劣化をほぼ感じない範囲{compressedNote}",
                    preset,
                    $"遅いpresetほど同じ画質でファイルが小さくなる（画質は変わらず時間だけが変わる）。{cpuText}なので、出力時間とのバランスで{preset}にする",
                    null, null);
            }

            if (encoders.Has("libvpx-vp9"))
            {
                var crf = (shortEdge <= 720 ? 32 : shortEdge <= 1080 ? 31 : shortEdge <= 1440 ? 24 : 15) + (isHighlyCompressed ? 2 : 0);
                var cpuUsed = threads >= FastCpuThreads ? 2 : threads >= MediumCpuThreads ? 3 : 4;
                return new VideoPlan(
                    "libvpx-vp9 (VP9)",
                    "このFFmpegにはAV1/x264がないため、CRFで画質を指定できるVP9を使う",
                    $"-c:v libvpx-vp9 -crf {crf} -b:v 0 -deadline good -cpu-used {cpuUsed} -row-mt 1 -pix_fmt yuv420p",
                    $"CRF {crf}",
                    $"Googleが推奨する解像度別のVP9 CRF値{compressedNote}",
                    $"cpu-used {cpuUsed}",
                    $"VP9は -cpu-used が小さいほど高効率・低速。{cpuText}なので、出力時間とのバランスで{cpuUsed}にする",
                    null, null);
            }

            var kbps = EstimateKbps(width, height, fps, 0.06);
            return OpenH264Plan(kbps, "画質指定(CRF)に対応したエンコーダがないため、libopenh264をビットレート指定で使う", $"{width}x{height}・{fps}fpsで画質を保てる最低限の目安");
        }

        private static VideoPlan PlanPlatformVideo(int width, int height, int fps, EncoderCapabilities encoders, PlatformSpec spec)
        {
            var shortEdge = Math.Min(width, height);
            var kbps = spec.GetVideoKbps(shortEdge, fps);
            var qualityReason = $"{spec.VideoBitRateSource}（{shortEdge}p・{fps}fps）。投稿先で再エンコードされるので、元の画質を十分に残せる値にする";
            var hardwareNote = encoders.HardwareH264Encoder != null ? $"。急ぐ場合は「速度優先」でGPU（{encoders.HardwareH264Encoder}）を使ってもこのビットレートなら十分きれい" : "";

            if (encoders.Has("libx264"))
            {
                var threads = EncoderCapabilities.CpuThreads;
                var preset = GetX264Preset(threads);
                return new VideoPlan(
                    "libx264 (H.264)",
                    $"{spec.Name}の推奨はH.264（最も互換性が高く、投稿時の変換トラブルが少ない）{hardwareNote}",
                    $"-c:v libx264 -preset {preset} -b:v {kbps}k -maxrate {kbps * 3 / 2}k -bufsize {kbps * 2}k -profile:v high -pix_fmt yuv420p",
                    $"{kbps} kbps",
                    qualityReason,
                    preset,
                    $"遅いpresetほど同じビットレートで画質が上がる。このPCのCPUは {threads} スレッドなので、出力時間とのバランスで{preset}にする",
                    null, kbps);
            }

            return OpenH264Plan(kbps,
                $"{spec.Name}の推奨はH.264（最も互換性が高い）。このFFmpegのソフトウェアH.264エンコーダはlibopenh264だけなので、ビットレート指定で画質を確保する{hardwareNote}",
                qualityReason);
        }

        private static VideoPlan PlanSpeedVideo(int width, int height, int fps, EncoderCapabilities encoders)
        {
            if (encoders.HardwareH264Encoder is { } hardware)
            {
                return new VideoPlan(
                    $"{hardware} (H.264・GPU)",
                    $"このPCではGPUエンコーダ（{hardware}）が使えるので、エンコードがCPUより大幅に速い{RenderBoundNote}。映像コマンドは -c:v h264 のままにすると、YMM4がGPUエンコーダに自動で置き換える",
                    "-c:v h264",
                    $"{HardwareVideoQuality}（GPUの品質値 24 相当）",
                    $"YMM4の「映像品質」{HardwareVideoQuality}は、GPUエンコーダの品質値(cq/QP) 24 相当。GPUエンコードは同じ画質だと容量が大きくなりやすいので、少し高めの画質にしておく",
                    "指定なし",
                    "YMM4がGPUエンコーダを自動選択するので、メーカーごとに書式が違うpresetは指定しない",
                    hardware, null, HardwareVideoQuality);
            }

            if (encoders.Has("libx264"))
            {
                return new VideoPlan(
                    "libx264 (H.264)",
                    "GPUエンコーダが見つからなかったため、ソフトウェアで速いlibx264を使う",
                    "-c:v libx264 -crf 22 -preset veryfast -pix_fmt yuv420p",
                    "CRF 22",
                    "速いpresetは同じCRFでも容量が増えるので、CRFは標準的な値にしておく",
                    "veryfast",
                    "slowより数倍速い。画質は同じで、ファイルが少し大きくなる",
                    null, null);
            }

            if (encoders.Has("libopenh264"))
            {
                var kbps = EstimateKbps(width, height, fps, 0.08);
                return OpenH264Plan(kbps,
                    "GPUエンコーダが見つからなかったため、ソフトウェアH.264の中で軽いlibopenh264を使う",
                    $"libopenh264は画質指定ができないので、{width}x{height}・{fps}fpsで画質を保てるビットレートを指定する");
            }

            return new VideoPlan(
                "libsvtav1 (AV1)",
                "GPUエンコーダとH.264のソフトウェアエンコーダが見つからなかったため、高速presetのSVT-AV1を使う",
                "-c:v libsvtav1 -crf 34 -preset 10 -pix_fmt yuv420p",
                "CRF 34",
                "高速presetでも十分な画質になる値",
                "10",
                "SVT-AV1のpresetは0(最遅)〜13(最速)。10は速度重視",
                null, null);
        }

        private static VideoPlan OpenH264Plan(int kbps, string encoderReason, string qualityReason) => new(
            "libopenh264 (H.264)",
            encoderReason,
            $"-c:v libopenh264 -rc_mode bitrate -b:v {kbps}k -profile high -pix_fmt yuv420p",
            $"{kbps} kbps",
            qualityReason,
            "指定なし",
            "libopenh264にはpresetの設定がない",
            null, kbps);

        private const int FastCpuThreads = 16;
        private const int MediumCpuThreads = 8;

        private const double SlowCpuRatio = 0.5;
        private const string RenderBoundNote = "（エフェクトが重く描画に時間がかかる動画では、出力全体の差は小さくなる）";
        private const double RealtimeMargin = 1.25;

        private static readonly (int Preset, double Speed)[] SvtAv1PresetSpeeds = [(5, 0.4), (6, 0.5), (8, 1.0)];
        private const int SvtAv1FastPreset = 10;
        private const double SvtAv1FastSpeed = 1.6;

        private record SvtAv1Choice(int Preset, double? Fps, string SpeedText);

        private static SvtAv1Choice ChooseSvtAv1Preset(double? measuredFps, int fps, int threads)
        {
            if (measuredFps is not { } fps8)
            {
                var preset = threads >= FastCpuThreads ? 5 : threads >= MediumCpuThreads ? 6 : 8;
                return new(preset, null, $"このPCのCPUは {threads} スレッド");
            }

            var (chosen, speed) = SvtAv1PresetSpeeds.FirstOrDefault(x => fps8 * x.Speed >= fps * RealtimeMargin);
            if (chosen == 0)
                (chosen, speed) = fps8 >= fps * SlowCpuRatio ? (EncoderCapabilities.SvtAv1MeasurePreset, 1.0) : (SvtAv1FastPreset, SvtAv1FastSpeed);
            return new(chosen, fps8 * speed,
                $"このPCでエンコードの速さを測ったところ、preset {EncoderCapabilities.SvtAv1MeasurePreset} で毎秒約 {fps8:0} フレーム");
        }

        private static string GetX264Preset(int threads) => threads >= FastCpuThreads ? "slow" : threads >= MediumCpuThreads ? "medium" : "fast";

        private static int QualityToHardwareScale(int quality, int max) =>
            (int)Math.Round((100 - Math.Clamp(quality, 0, 100)) / 100.0 * max, MidpointRounding.AwayFromZero);

        private static int EstimateKbps(int width, int height, int fps, double bitsPerPixel)
        {
            var kbps = width * (double)height * fps * bitsPerPixel / 1000;
            return Math.Clamp((int)Math.Round(kbps / 100) * 100, 500, 100000);
        }

        private static string HardwareReason(EncodeGoal goal, VideoPlan video, EncoderCapabilities encoders, int fps)
        {
            if (video.HardwareEncoder != null)
                return $"GPU（{video.HardwareEncoder}）でエンコードすると、エンコードが大幅に速くなる{RenderBoundNote}";

            var availability = encoders.HardwareH264Encoder != null
                ? $"このPCでは{encoders.HardwareH264Encoder}が使えるが、"
                : "このPCで使えるGPUエンコーダは見つからなかった。";
            return goal switch
            {
                EncodeGoal.Size => availability + GpuSpeedText(video, encoders, fps) + "同じ画質ならAV1のほうが小さくなることが多い（静止画中心の動画では差が小さく、GPUでも同じくらいの容量になることがある）。ONだとYMM4が映像コマンドのcrf/preset指定を削除してしまうので、時間を優先するなら「速度優先」を選ぶ",
                EncodeGoal.Platform => availability + "ONだとYMM4が映像コマンドのビットレート/preset指定を削除してGPU用の設定に置き換えてしまう",
                _ => availability + "ONのままでもソフトウェアに切り替わるが、その際に映像コマンドのcrf/ビットレート指定が削除される",
            };
        }

        private static string GpuSpeedText(VideoPlan video, EncoderCapabilities encoders, int fps)
        {
            if (!video.Encoder.StartsWith("libsvtav1", StringComparison.Ordinal)
                || encoders.HardwareH264Fps is not { } hardwareFps
                || ChooseSvtAv1Preset(encoders.SvtAv1Fps, fps, EncoderCapabilities.CpuThreads).Fps is not { } svtAv1Fps)
                return "";
            return $"このPCで測った速さでは、エンコードはGPUのほうが約 {hardwareFps / svtAv1Fps:0.0} 倍速い{RenderBoundNote}。ただし";
        }

        private static void AddPlatformLimitNotes(PlatformSpec spec, ProjectInfo project, int? videoKbps, int audioKbps, List<string> notes)
        {
            if (spec.MaxDuration is { } maxDuration && project.Duration > maxDuration)
                notes.Add($"動画の長さ（{project.Duration:mm\\:ss}）が{spec.Name}の通常アカウントの上限（{maxDuration:m\\:ss}）を超えています。");

            if (videoKbps is { } kbps)
            {
                var bytes = (long)((kbps + audioKbps) * 1000 / 8 * project.Duration.TotalSeconds);
                var sizeText = $"予想ファイルサイズ: 約 {bytes / 1024.0 / 1024:0} MB";
                if (spec.MaxFileBytes is { } maxBytes && bytes > maxBytes)
                    notes.Add($"{sizeText}。{spec.Name}の上限（{maxBytes / 1024 / 1024} MB）を超えるため、映像ビットレートを下げるか動画を短くしてください。");
                else
                    notes.Add(sizeText);
            }
        }

        private static void AddAnalysisNotes(AnalysisResult analysis, SourceSummary sources, List<string> notes)
        {
            var errors = analysis.Materials.Count(x => x.Error != null);
            if (errors > 0)
                notes.Add($"{errors} 件の素材を解析できませんでした（素材一覧を参照）。その素材は推奨値の計算に含まれていません。");

            if (analysis.Materials.Count == 0)
                notes.Add("タイムライン上に動画・音声アイテムがありません。プロジェクト設定と一般的な値から推奨しています。");
            else if (sources.Videos.Count == 0 && sources.Audios.Count == 0)
                notes.Add("解析できた動画・音声素材がないため、プロジェクト設定と一般的な値から推奨しています。");

            notes.Add("解析対象は動画アイテム・音声アイテム（シーン内も含む）のファイルです。ボイスアイテムや画像アイテムは含まれません。");
        }
    }
}
