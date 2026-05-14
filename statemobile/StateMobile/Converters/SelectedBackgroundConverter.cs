using System.Globalization;

namespace StateMobile.Converters
{
    public class SelectedBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                return Application.Current?.RequestedTheme == AppTheme.Dark 
                    ? Color.FromArgb("#2A3E3E") 
                    : Color.FromArgb("#E7F3FF"); 
            }
            return Colors.Transparent; 
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}