using System.Globalization;

namespace StateMobile.Converters
{
    public class PercentageToDoubleConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is decimal percentage)
            {
                return (double)(percentage / 100);
            }
            if (value is double d)
            {
                return d / 100;
            }
            if (value is int i)
            {
                return (double)i / 100;
            }
            return 0.0;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
