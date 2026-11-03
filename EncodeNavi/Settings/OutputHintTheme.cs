using System.ComponentModel.DataAnnotations;

namespace EncodeNavi.Settings
{
    internal enum OutputHintTheme
    {
        [Display(Name = "デフォルト")]
        Default,

        [Display(Name = "カラー")]
        Color,

        [Display(Name = "サイバー")]
        Cyber,

        [Display(Name = "ポップ")]
        Pop,
    }
}
