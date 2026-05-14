using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Microsoft.Maui.Controls;

namespace StateMobile.Converters
{
    public class Base64ImageConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string base64Str && !string.IsNullOrWhiteSpace(base64Str))
            {
                try
                {
                    if (Cache.TryGetValue(base64Str, out var cachedSource))
                    {
                        return cachedSource;
                    }

                    if (base64Str.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        var uriSource = ImageSource.FromUri(new Uri(base64Str));
                        Cache[base64Str] = uriSource;
                        return uriSource;
                    }
                    
                    var bytes = System.Convert.FromBase64String(base64Str);
                    var imageSource = ImageSource.FromStream(() => new MemoryStream(bytes));
                    Cache[base64Str] = imageSource;
                    return imageSource;
                }
                catch
                {
                    return ImageSource.FromFile("user_avatar_placeholder.png");
                }
            }

            return ImageSource.FromFile("user_avatar_placeholder.png");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
