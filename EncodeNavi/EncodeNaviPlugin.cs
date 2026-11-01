using System.Reflection;
using EncodeNavi.OutputDialog;
using EncodeNavi.View;
using EncodeNavi.ViewModel;
using YukkuriMovieMaker.Plugin;

namespace EncodeNavi
{
    [PluginDetails(AuthorName = "いるかぁぁ")]
    public class EncodeNaviPlugin : IToolPlugin
    {
        public string Name => "EncodeNavi";
        public Type ViewModelType => typeof(EncodeNaviViewModel);
        public Type ViewType => typeof(EncodeNaviControl);

        public PluginDetailsAttribute Details => GetType().GetCustomAttribute<PluginDetailsAttribute>() ?? new();

        public EncodeNaviPlugin()
        {
            OutputDialogHook.Register();
            ProgressPreviewHook.Register();
        }
    }
}
