using System.Globalization;

namespace StateMobile.Converters
{
    public class SelectionColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                return Color.FromArgb("#A4C2C2"); // Selected color
            }
            return Colors.Transparent; // Default/Unselected color
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
