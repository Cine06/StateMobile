using System.Globalization;
using Microsoft.Maui.Controls;

namespace StateMobile.Converters
{
    public class NullToBoolConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isNull = value == null;
            if (value is string str) isNull = string.IsNullOrEmpty(str);

            return Invert ? isNull : !isNull;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
