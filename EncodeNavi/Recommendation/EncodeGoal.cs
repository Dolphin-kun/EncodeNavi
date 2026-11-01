using System.ComponentModel.DataAnnotations;

namespace EncodeNavi.Recommendation
{
    internal enum EncodeGoal
    {
        [Display(Name = "容量優先")]
        Size,

        [Display(Name = "投稿先に最適化")]
        Platform,

        [Display(Name = "速度優先")]
        Speed,
    }
}
