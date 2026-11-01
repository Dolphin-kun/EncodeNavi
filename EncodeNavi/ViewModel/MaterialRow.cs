using System.IO;
using EncodeNavi.Analysis;

namespace EncodeNavi.ViewModel
{
    internal record MaterialRow(string FileName, string FilePath, string Video, string Audio, string? Error)
    {
        public bool HasError => Error != null;

        public static MaterialRow From(MaterialInfo material)
        {
            var reference = material.Reference;
            var probe = material.Probe;

            var video = !reference.UsesVideo ? ""
                : probe?.Video is { } v ? FormatVideo(v)
                : probe != null ? "映像: なし"
                : "";

            var audio = !reference.UsesAudio ? (reference.UsesVideo ? "音声: 音量0のため対象外" : "")
                : probe?.Audio is { } a ? FormatAudio(a, material.AudioContent)
                : probe != null ? "音声: なし"
                : "";

            return new MaterialRow(Path.GetFileName(reference.FilePath), reference.FilePath, video, audio, material.Error);
        }

        private static string FormatVideo(VideoStreamInfo video)
        {
            if (video.IsStillImage)
                return $"映像: {video.Codec} 静止画 {video.Width}x{video.Height}";

            var bitRate = video.BitRate > 0 ? $" {video.BitRate / 1000:N0} kbps" : "";
            return $"映像: {video.Codec} {video.Width}x{video.Height} {video.Fps:0.##}fps {video.BitDepth}bit ({video.PixelFormat}){bitRate}";
        }

        private static string FormatAudio(AudioStreamInfo audio, AudioContentInfo? content)
        {
            var channels = audio.Channels switch { 1 => "モノラル", 2 => "ステレオ", var n => $"{n}ch" };
            var bitRate = audio.IsLossless ? " ロスレス" : audio.BitRate > 0 ? $" {audio.BitRate / 1000:N0} kbps" : "";
            var text = $"音声: {audio.Codec} {audio.SampleRate}Hz {channels}{bitRate}";

            if (content is null)
                return text + " / 中身は解析できませんでした";
            if (content.IsSilent)
                return text + " / 無音";

            var monoNote = audio.Channels >= 2 && content.IsMono ? " / 中身はモノラル（左右同じ）" : "";
            var cutNote = content.IsHighCut ? "（高域カット）" : "";
            return text + $" / 帯域 {content.BandwidthHz / 1000:0.0}kHz{cutNote}{monoNote}";
        }
    }
}
