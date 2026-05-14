using System.Globalization;
using StateMobile.Services;

namespace StateMobile.Converters
{
    public class EngineerImageConverter : IValueConverter
    {
        private const string MalePlaceholder = "https://static.vecteezy.com/system/resources/thumbnails/036/594/092/small_2x/man-empty-avatar-photo-placeholder-for-social-networks-resumes-forums-and-dating-sites-male-and-female-no-photo-images-for-unfilled-user-profile-free-vector.jpg";
        private const string FemalePlaceholder = "https://static.vecteezy.com/system/resources/previews/042/332/066/non_2x/person-photo-placeholder-woman-default-avatar-profile-icon-grey-photo-placeholder-female-no-photo-images-for-unfilled-user-profile-greyscale-illustration-for-social-media-free-vector.jpg";

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string entityCode && !string.IsNullOrEmpty(entityCode))
            {
                string baseUrl = AppSettings.GetBaseUrl();
                // We'll call the API endpoint for engineer pic
                // Note: The SP implementation might handle the controlNo context if needed, 
                // but usually the EntityCode is enough for a profile photo.
                return $"{baseUrl}/api/Project/engineers-pic/0?entityCodes={entityCode}";
            }
            
            return MalePlaceholder;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
