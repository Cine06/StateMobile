using System.Globalization;

namespace StateMobile.Converters
{
    public class WeatherToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int weather)
                return weather == 1 ? "Workable" : "Not Workable";

            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
