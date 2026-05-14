using System.Globalization;

namespace StateMobile.Converters
{
    public class PasswordVisibilityIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isHidden)
            {
                return isHidden ? "view.png" : "hide.png";
            }
            return "view.png";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}