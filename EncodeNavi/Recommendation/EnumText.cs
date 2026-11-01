using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace EncodeNavi.Recommendation
{
    internal static class EnumText
    {
        public static string Get(Enum value) =>
            value.GetType().GetField(value.ToString())?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
    }
}
