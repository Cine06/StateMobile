using StateMobile.Models;
using StateMobile.Services;
using System.Collections.ObjectModel;
using SkiaSharp;

namespace StateMobile.Views
{
    public partial class CameraScanPage : ContentPage
    {
        private readonly IPdfService _pdfService;
        private readonly IDocumentScannerService _documentScannerService;
        private readonly ScannedDocument _document = new();
        private readonly ObservableCollection<ImageSource> _thumbnails = new();
        private bool _autoLaunchPending = true;
        private bool _scanInProgress;
        private bool _suppressNextAutoScan;
        private bool _isNavigatingToPreview;

        public CameraScanPage(IPdfService pdfService, IDocumentScannerService documentScannerService)
        {
            InitializeComponent();
            _pdfService = pdfService;
            _documentScannerService = documentScannerService;
            thumbnailCollection.ItemsSource = _thumbnails;
            btnSign.IsEnabled = false;
            btnDone.IsEnabled = false;
            btnDone.Text = "Done (0)";
            lblPlaceholder.IsVisible = true;
            lblPlaceholder.Text = "Tap capture to scan your first page";
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Let the page render one frame before auto-launching ML Kit.
            await Task.Yield();

            if (_suppressNextAutoScan)
            {
                _suppressNextAutoScan = false;
                return;
            }

            if (!_autoLaunchPending)
            {
                return;
            }

            _autoLaunchPending = false;
            await StartScanFlowAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _scanInProgress = false;
        }

        private async void OnRetakeClicked(object sender, EventArgs e)
        {
            if (_document.Pages.Count == 0)
            {
                return;
            }

            var lastIndex = _document.Pages.Count - 1;
            _document.Pages.RemoveAt(lastIndex);
            _thumbnails.RemoveAt(lastIndex);
            UpdateDoneButton();

            if (_document.Pages.Count == 0)
            {
                lblPlaceholder.IsVisible = true;
                lblPlaceholder.Text = "Tap capture to scan your first page";
                return;
            }

            lblPlaceholder.IsVisible = false;
        }

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            await StartScanFlowAsync();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            if (_document.Pages.Count == 0)
            {
                return;
            }
            await OpenPreviewAsync(openSignatureOnLoad: false);
        }

        private async void OnSignClicked(object sender, EventArgs e)
        {
            if (_document.Pages.Count == 0)
            {
                return;
            }

            await OpenPreviewAsync(openSignatureOnLoad: true);
        }

        private async Task StartScanFlowAsync()
        {
            if (_scanInProgress)
            {
                return;
            }

            _scanInProgress = true;
            loadingOverlay.IsVisible = true;
            lblLoadingText.Text = "Scanning document...";
            lblPlaceholder.Text = "Scanning document...";
            var shouldOpenPreview = false;

            try
            {
                var pages = await _documentScannerService.ScanAsync(CancellationToken.None);
                if (pages.Count == 0)
                {
                    return;
                }

                foreach (var page in pages)
                {
                    await AddScannedPageAsync(page);
                }

                lblPlaceholder.IsVisible = false;
                lblPlaceholder.Text = "Tap capture to scan another page or finish with Done.";
                shouldOpenPreview = true;
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Scanner Error", ex.Message, "OK");
            }
            finally
            {
                loadingOverlay.IsVisible = false;
                _scanInProgress = false;
            }

            if (shouldOpenPreview)
            {
                await OpenPreviewAsync(openSignatureOnLoad: false);
            }
        }

        private async Task OpenPreviewAsync(bool openSignatureOnLoad)
        {
            if (_isNavigatingToPreview)
            {
                return;
            }

            _isNavigatingToPreview = true;
            loadingOverlay.IsVisible = true;
            lblLoadingText.Text = openSignatureOnLoad ? "Opening signature..." : "Preparing preview...";

            try
            {
                _suppressNextAutoScan = true;
                var previewPage = new PdfPreviewPage(_pdfService, _document, openSignatureOnLoad);
                await Navigation.PushAsync(previewPage);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"Could not open preview: {ex.Message}", "OK");
            }
            finally
            {
                loadingOverlay.IsVisible = false;
                _isNavigatingToPreview = false;
            }
        }

        private async Task AddScannedPageAsync(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }

            // Process each page once in background: trim and thumbnail generation.
            var processed = await Task.Run(() => ProcessScannedPage(bytes));

            _document.Pages.Add(processed.PageBytes);
            _thumbnails.Add(processed.Thumbnail);
            UpdateDoneButton();
            lblPlaceholder.IsVisible = false;
        }

        private static (byte[] PageBytes, ImageSource Thumbnail) ProcessScannedPage(byte[] originalBytes)
        {
            var trimmedBytes = TrimUniformBorders(originalBytes);
            var thumbnailBytes = CreateThumbnailBytes(trimmedBytes, maxLongestSide: 340);
            var thumbnail = ImageSource.FromStream(() => new MemoryStream(thumbnailBytes));
            return (trimmedBytes, thumbnail);
        }

        private static byte[] CreateThumbnailBytes(byte[] imageBytes, int maxLongestSide)
        {
            try
            {
                using var bitmap = SKBitmap.Decode(imageBytes);
                if (bitmap == null)
                {
                    return imageBytes;
                }

                var longestSide = Math.Max(bitmap.Width, bitmap.Height);
                if (longestSide <= maxLongestSide)
                {
                    return imageBytes;
                }

                var scale = maxLongestSide / (float)longestSide;
                var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                var info = new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType, bitmap.ColorSpace);
                using var resized = bitmap.Resize(info, SKFilterQuality.Medium);
                if (resized == null)
                {
                    return imageBytes;
                }

                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
                return data?.ToArray() ?? imageBytes;
            }
            catch
            {
                return imageBytes;
            }
        }

        private static byte[] TrimUniformBorders(byte[] imageBytes)
        {
            try
            {
                using var bitmap = SKBitmap.Decode(imageBytes);
                if (bitmap == null || bitmap.Width < 200 || bitmap.Height < 200)
                {
                    return imageBytes;
                }

                var borderColor = AverageCornerColor(bitmap);
                const int tolerance = 28;
                const float threshold = 0.97f;

                var maxTrimX = (int)(bitmap.Width * 0.08f);
                var maxTrimY = (int)(bitmap.Height * 0.08f);

                int top = 0;
                while (top < maxTrimY && RowMatchesBorder(bitmap, top, borderColor, tolerance, threshold))
                {
                    top++;
                }

                int bottom = bitmap.Height - 1;
                while ((bitmap.Height - 1 - bottom) < maxTrimY && bottom > top && RowMatchesBorder(bitmap, bottom, borderColor, tolerance, threshold))
                {
                    bottom--;
                }

                int left = 0;
                while (left < maxTrimX && left < bitmap.Width - 1 && ColumnMatchesBorder(bitmap, left, borderColor, tolerance, threshold))
                {
                    left++;
                }

                int right = bitmap.Width - 1;
                while ((bitmap.Width - 1 - right) < maxTrimX && right > left && ColumnMatchesBorder(bitmap, right, borderColor, tolerance, threshold))
                {
                    right--;
                }

                var croppedWidth = right - left + 1;
                var croppedHeight = bottom - top + 1;
                if (croppedWidth < 120 || croppedHeight < 120)
                {
                    return imageBytes;
                }

                if (left == 0 && top == 0 && right == bitmap.Width - 1 && bottom == bitmap.Height - 1)
                {
                    return imageBytes;
                }

                using var cropped = new SKBitmap(croppedWidth, croppedHeight, bitmap.ColorType, bitmap.AlphaType);
                using (var canvas = new SKCanvas(cropped))
                {
                    var src = new SKRect(left, top, right + 1, bottom + 1);
                    var dst = new SKRect(0, 0, croppedWidth, croppedHeight);
                    canvas.DrawBitmap(bitmap, src, dst);
                }

                using var image = SKImage.FromBitmap(cropped);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray() ?? imageBytes;
            }
            catch
            {
                return imageBytes;
            }
        }

        private static SKColor AverageCornerColor(SKBitmap bitmap)
        {
            var c1 = bitmap.GetPixel(0, 0);
            var c2 = bitmap.GetPixel(bitmap.Width - 1, 0);
            var c3 = bitmap.GetPixel(0, bitmap.Height - 1);
            var c4 = bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1);

            byte r = (byte)((c1.Red + c2.Red + c3.Red + c4.Red) / 4);
            byte g = (byte)((c1.Green + c2.Green + c3.Green + c4.Green) / 4);
            byte b = (byte)((c1.Blue + c2.Blue + c3.Blue + c4.Blue) / 4);
            return new SKColor(r, g, b);
        }

        private static bool RowMatchesBorder(SKBitmap bitmap, int y, SKColor borderColor, int tolerance, float threshold)
        {
            int matches = 0;
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (IsClose(bitmap.GetPixel(x, y), borderColor, tolerance))
                {
                    matches++;
                }
            }

            return matches >= bitmap.Width * threshold;
        }

        private static bool ColumnMatchesBorder(SKBitmap bitmap, int x, SKColor borderColor, int tolerance, float threshold)
        {
            int matches = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                if (IsClose(bitmap.GetPixel(x, y), borderColor, tolerance))
                {
                    matches++;
                }
            }

            return matches >= bitmap.Height * threshold;
        }

        private static bool IsClose(SKColor a, SKColor b, int tolerance)
        {
            return Math.Abs(a.Red - b.Red) <= tolerance
                && Math.Abs(a.Green - b.Green) <= tolerance
                && Math.Abs(a.Blue - b.Blue) <= tolerance;
        }

        private void UpdateDoneButton()
        {
            btnSign.IsEnabled = _document.Pages.Count > 0;
            btnDone.IsEnabled = _document.Pages.Count > 0;
            btnDone.Text = $"Done ({_document.Pages.Count})";
        }
    }
}
