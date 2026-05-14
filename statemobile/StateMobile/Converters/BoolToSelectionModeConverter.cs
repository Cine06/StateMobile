using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;

namespace StateMobile.Converters
{
    public class BoolToSelectionModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelectionMode)
            {
                return isSelectionMode ? SelectionMode.Multiple : SelectionMode.None;
            }
            return SelectionMode.None;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}