using System.Globalization;
using StateMobile.Models;

namespace StateMobile.Converters
{
    public class ProjectProfileImageConverter : IValueConverter
    {
        private const string DefaultImageUrl = "https://img.lamudi.com/eyJidWNrZXQiOiJwcmQtbGlmdWxsY29ubmVjdC1wcm9qZWN0cy1hZG1pbi1pbWFnZXMiLCJrZXkiOiIyYmQzMzA5Zi0yYTBkLTQ0YjQtOGJhNy1kNmFiODczYmY1NjEvMmJkMzMwOWYtMmEwZC00NGI0LThiYTctZDZhYjg3M2JmNTYxXzdiMmNhYWY5LTBjZjYtNDMzNi1iNTVhLWQyNDExNDE1Y2RkMi5qcGciLCJicmFuZCI6ImxhbXVkaSIsImVkaXRzIjp7InJvdGF0ZSI6bnVsbCwicmVzaXplIjp7IndpZHRoIjo3NTMsImhlaWdodCI6NDE1LCJmaXQiOiJjb3ZlciJ9fX0=";

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ProjectModel project)
            {
                // 1. Use cover photo from API if available (from trx_ProjectProfileCoverPhoto)
                if (!string.IsNullOrEmpty(project.CoverPhotoUrl))
                {
                    // CoverPhotoUrl is a relative path like "/api/Project/profile-pic/HC001"
                    // Prepend the correct base URL (handles Android emulator 10.0.2.2 translation)
                    var baseUrl = AppSettings.GetBaseUrl();
                    return $"{baseUrl}{project.CoverPhotoUrl}";
                }

                // 2. Fallback: default placeholder
                return DefaultImageUrl;
            }
            
            return DefaultImageUrl;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
