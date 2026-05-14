using System.Globalization;
using System.IO;

namespace StateMobile.Converters
{
    public class StringToImageSourceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        // Use FromStream as it's more reliable for local files across platforms
                        var bytes = File.ReadAllBytes(path);
                        return ImageSource.FromStream(() => new MemoryStream(bytes));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ StringToImageSourceConverter error: {ex.Message}");
                }
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
