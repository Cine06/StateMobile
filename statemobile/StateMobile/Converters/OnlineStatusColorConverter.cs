using System.Globalization;

namespace StateMobile.Converters
{
    public class OnlineStatusColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isOnline)
            {
                return isOnline ? Color.FromArgb("#10B981") : Color.FromArgb("#9CA3AF"); // Green-500 : Gray-400
            }
            return Color.FromArgb("#9CA3AF");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
