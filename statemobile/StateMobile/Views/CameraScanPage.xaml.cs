using StateMobile.Models;
using StateMobile.Services;
using SkiaSharp;

namespace StateMobile.Views
{
    public partial class CameraScanPage : ContentPage
    {
        private ScannedDocument _document = new();
        private int _pageCount = 0;

        public CameraScanPage()
        {
            InitializeComponent();
        }

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo != null)
            {
                var localPath = Path.Combine(FileSystem.CacheDirectory, $"scan_{Guid.NewGuid()}.jpg");
                
                // Enhance image (CamScanner style: Contrast/Brightness)
                await EnhanceAndSaveImage(photo, localPath);

                _pageCount++;
                _document.Pages.Add(new ScannedPage 
                { 
                    PageNumber = _pageCount, 
                    ImagePath = localPath 
                });

                UpdateUI();
            }
        }

        private async Task EnhanceAndSaveImage(FileResult photo, string destinationPath)
        {
            using var stream = await photo.OpenReadAsync();
            using var bitmap = SKBitmap.Decode(stream);
            
            // Simple Document Enhancement: Increase Contrast and Brightness
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint
            {
                ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
                {
                    1.2f, 0, 0, 0, 0.1f, // Red
                    0, 1.2f, 0, 0, 0.1f, // Green
                    0, 0, 1.2f, 0, 0.1f, // Blue
                    0, 0, 0, 1, 0        // Alpha
                })
            };
            canvas.DrawBitmap(bitmap, 0, 0, paint);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            using var outputStream = File.OpenWrite(destinationPath);
            data.SaveTo(outputStream);
        }

        private void UpdateUI()
        {
            DoneButton.Text = $"Done ({_pageCount})";
            DoneButton.IsEnabled = _pageCount > 0;

            // Add thumbnail to list
            var lastPage = _document.Pages.Last();
            var img = new Image { Source = lastPage.ImagePath, WidthRequest = 60, HeightRequest = 80, Aspect = Aspect.AspectFill };
            ThumbnailList.Children.Add(new Frame { Content = img, Padding = 2, BorderColor = Colors.White });
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new PdfPreviewPage(_document));
        }

        private async void OnCancelClicked(object sender, EventArgs e) => await Navigation.PopAsync();
    }
}