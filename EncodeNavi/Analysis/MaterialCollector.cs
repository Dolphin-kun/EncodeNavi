using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace EncodeNavi.Analysis
{
    internal static class MaterialCollector
    {
        public static IReadOnlyList<MaterialReference> Collect(Timeline timeline, Scenes scenes)
        {
            var materials = new Dictionary<(string, int), MaterialReference>();
            Visit(timeline, scenes, false, materials, []);
            return [.. materials.Values];
        }

        private static void Visit(Timeline timeline, Scenes scenes, bool isParentPanned, Dictionary<(string, int), MaterialReference> materials, HashSet<Guid> visited)
        {
            if (!visited.Add(timeline.ID))
                return;

            foreach (var item in timeline.Items)
            {
                if (item.IsHidden)
                    continue;

                switch (item)
                {
                    case VideoItem videoItem:
                        Add(materials, new MaterialReference(
                            videoItem.FilePath ?? "",
                            videoItem.AudioTrackIndex,
                            UsesVideo: true,
                            UsesAudio: !IsMuted(videoItem.Volume),
                            IsPanned: isParentPanned || IsPanned(videoItem.Pan)));
                        break;

                    case AudioItem audioItem:
                        Add(materials, new MaterialReference(
                            audioItem.FilePath ?? "",
                            audioItem.AudioTrackIndex,
                            UsesVideo: false,
                            UsesAudio: !IsMuted(audioItem.Volume),
                            IsPanned: isParentPanned || IsPanned(audioItem.Pan)));
                        break;

                    case SceneItem sceneItem:
                        var sceneTimeline = scenes.Timelines.FirstOrDefault(x => x.ID == sceneItem.SceneId);
                        if (sceneTimeline != null)
                            Visit(sceneTimeline, scenes, isParentPanned || IsPanned(sceneItem.Pan), materials, visited);
                        break;
                }
            }

            visited.Remove(timeline.ID);
        }

        private static void Add(Dictionary<(string, int), MaterialReference> materials, MaterialReference reference)
        {
            if (string.IsNullOrEmpty(reference.FilePath))
                return;

            var key = (reference.FilePath.ToLowerInvariant(), reference.AudioTrackIndex);
            if (materials.TryGetValue(key, out var existing))
            {
                reference = existing with
                {
                    UsesVideo = existing.UsesVideo || reference.UsesVideo,
                    UsesAudio = existing.UsesAudio || reference.UsesAudio,
                    IsPanned = existing.IsPanned || reference.IsPanned,
                };
            }
            materials[key] = reference;
        }

        private static bool IsMuted(Animation volume) => volume.ActiveValues.All(x => x.Value <= 0);

        private static bool IsPanned(Animation pan) => pan.ActiveValues.Any(x => x.Value != 0);
    }
}
