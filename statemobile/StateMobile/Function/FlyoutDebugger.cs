using Microsoft.Maui.Controls;
using System.Reflection;

namespace StateMobile.Function
{
    public class FlyoutDebugger
    {
        public static string GetFlyoutMethods()
        {
            var type = typeof(FlyoutBase);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            return string.Join("\n", methods.Select(m => m.Name));
        }
    }
}
