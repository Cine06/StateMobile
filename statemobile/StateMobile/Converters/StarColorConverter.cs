using System.Globalization;

namespace StateMobile.Converters
{
    public class StarColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int rating)
            {
                return rating >= 4 ? Color.FromArgb("#F59E0B") : Color.FromArgb("#D1D5DB"); // Amber-400 : Gray-300
            }

            if (value is double doubleRating)
            {
                return doubleRating >= 4 ? Color.FromArgb("#F59E0B") : Color.FromArgb("#D1D5DB");
            }

            return Color.FromArgb("#D1D5DB");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
