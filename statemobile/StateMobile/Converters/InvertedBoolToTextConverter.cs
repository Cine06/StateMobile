using System.Globalization;

namespace StateMobile.Converters
{
    public class InvertedBoolToTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string defaultText = parameter?.ToString() ?? "OK";

            if (value is bool isBusy)
            {
                return isBusy ? string.Empty : defaultText;
            }
            return defaultText;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}