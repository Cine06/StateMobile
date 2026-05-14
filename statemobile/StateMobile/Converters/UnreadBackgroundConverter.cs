using System.Globalization;

namespace StateMobile.Converters
{
    public class UnreadBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isUnread && isUnread)
            {
                return Application.Current?.RequestedTheme == AppTheme.Dark 
                    ? Color.FromArgb("#2A3E3E") 
                    : Color.FromArgb("#F0F4F8"); 
            }
            return Colors.Transparent; 
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
