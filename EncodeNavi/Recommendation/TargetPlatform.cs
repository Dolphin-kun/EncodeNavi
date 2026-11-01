using System.ComponentModel.DataAnnotations;

namespace EncodeNavi.Recommendation
{
    internal enum TargetPlatform
    {
        [Display(Name = "YouTube")]
        YouTube,

        [Display(Name = "ニコニコ動画")]
        Niconico,

        [Display(Name = "X (Twitter)")]
        X,
    }
}
