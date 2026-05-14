using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace StateMobile.Views
{
    public partial class SignaturePadView : ContentView
    {
        private readonly List<SKPath> _paths = new();
        private SKPath? _currentPath;
        private SKPaint _paint;
        private SKColor _currentColor = SKColors.Black;
        private float _currentStrokeWidth = 5f;

        // Performance optimization: cache the rendered paths
        private SKBitmap? _cachedPathsBitmap;
        private SKImageInfo _cachedBitmapInfo;
        private bool _needsCacheRefresh = true;

        // Touch throttling
        private long _lastTouchTick = 0;
        private const long TOUCH_THROTTLE_MS = 16; // ~60fps

        private byte[]? _attachedSignatureBytes;
        private bool _isDrawMode = true;

        public event EventHandler<byte[]>? SignatureCompleted;

        public SignaturePadView()
        {
            InitializeComponent();
            InitializePaint();
        }

        private void InitializePaint()
        {
            _paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = _currentColor,
                StrokeWidth = _currentStrokeWidth,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
        }

        public void SetStrokeColor(SKColor color)
        {
            _currentColor = color;
            _paint.Color = color;
        }

        public void SetStrokeWidth(float width)
        {
            _currentStrokeWidth = Math.Max(1f, Math.Min(50f, width));
            _paint.StrokeWidth = _currentStrokeWidth;
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            SignatureCompleted?.Invoke(this, Array.Empty<byte>());
        }

        private void OnDrawModeClicked(object sender, EventArgs e)
        {
            _isDrawMode = true;
            canvasBorder.IsVisible = true;
            attachBorder.IsVisible = false;
            
            btnDrawMode.BackgroundColor = Color.FromArgb("#2563EB");
            btnDrawMode.TextColor = Colors.White;
            btnAttachMode.BackgroundColor = Colors.Transparent;
            btnAttachMode.TextColor = Color.FromArgb("#6B7280");
            btnAttachMode.BorderWidth = 1;
        }

        private void OnAttachModeClicked(object sender, EventArgs e)
        {
            _isDrawMode = false;
            canvasBorder.IsVisible = false;
            attachBorder.IsVisible = true;
            
            btnAttachMode.BackgroundColor = Color.FromArgb("#2563EB");
            btnAttachMode.TextColor = Colors.White;
            btnAttachMode.BorderWidth = 0;
            btnDrawMode.BackgroundColor = Colors.Transparent;
            btnDrawMode.TextColor = Color.FromArgb("#6B7280");
            btnDrawMode.BorderWidth = 1;
        }

        private async void OnPickImageClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Select Signature Image",
                    FileTypes = FilePickerFileType.Images
                });

                if (result != null)
                {
                    using var stream = await result.OpenReadAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    _attachedSignatureBytes = memoryStream.ToArray();

                    imgSignaturePreview.Source = ImageSource.FromStream(() => new MemoryStream(_attachedSignatureBytes));
                    layoutAttachPlaceholder.IsVisible = false;
                    importPreviewContainer.IsVisible = true;
                    btnChangeImage.IsVisible = true;
                }
            }
            catch (Exception)
            {
                // Silently fail or log
            }
        }

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            
            canvas.Clear(SKColors.Transparent);

            // Check if we need to refresh the cache
            if (_needsCacheRefresh || _cachedBitmapInfo.Width != info.Width || _cachedBitmapInfo.Height != info.Height)
            {
                RefreshPathsCache(info);
            }

            // Draw cached bitmap (all previous paths)
            if (_cachedPathsBitmap != null)
            {
                canvas.DrawBitmap(_cachedPathsBitmap, 0, 0);
            }

            // Draw current path only (minimal redraw)
            if (_currentPath != null && !_currentPath.IsEmpty)
            {
                canvas.DrawPath(_currentPath, _paint);
            }
        }

        private void RefreshPathsCache(SKImageInfo info)
        {
            _cachedBitmapInfo = info;
            _cachedPathsBitmap?.Dispose();
            _cachedPathsBitmap = new SKBitmap(info);

            if (_paths.Count > 0)
            {
                using var canvas = new SKCanvas(_cachedPathsBitmap);
                canvas.Clear(SKColors.Transparent);

                foreach (var path in _paths)
                {
                    canvas.DrawPath(path, _paint);
                }
            }

            _needsCacheRefresh = false;
        }

        private void OnCanvasViewTouch(object sender, SKTouchEventArgs e)
        {
            var currentTick = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    _currentPath = new SKPath();
                    _currentPath.MoveTo(e.Location);
                    _lastTouchTick = currentTick;
                    ((SKCanvasView)sender).InvalidateSurface();
                    break;

                case SKTouchAction.Moved:
                    // Throttle touch events to ~60fps to reduce redraws
                    if (e.InContact && _currentPath != null && currentTick - _lastTouchTick >= TOUCH_THROTTLE_MS)
                    {
                        _currentPath.LineTo(e.Location);
                        _lastTouchTick = currentTick;
                        ((SKCanvasView)sender).InvalidateSurface();
                    }
                    break;

                case SKTouchAction.Released:
                    if (_currentPath != null && !_currentPath.IsEmpty)
                    {
                        _paths.Add(_currentPath);
                        _currentPath = null;
                        _needsCacheRefresh = true; // Mark cache for update
                    }
                    ((SKCanvasView)sender).InvalidateSurface();
                    break;
            }

            e.Handled = true;
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            if (_isDrawMode)
            {
                _paths.Clear();
                _currentPath = null;
                _cachedPathsBitmap?.Dispose();
                _cachedPathsBitmap = null;
                _needsCacheRefresh = false;
                canvasView.InvalidateSurface();
            }
            else
            {
                _attachedSignatureBytes = null;
                imgSignaturePreview.Source = null;
                layoutAttachPlaceholder.IsVisible = true;
                importPreviewContainer.IsVisible = false;
                btnChangeImage.IsVisible = false;
            }
        }

        private void OnDoneClicked(object sender, EventArgs e)
        {
            if (_isDrawMode)
            {
                if (_paths.Count == 0)
                {
                    SignatureCompleted?.Invoke(this, Array.Empty<byte>());
                    return;
                }

                // Export only the drawn signature bounds so "Done" stays responsive.
                if (!TryGetSignatureBounds(_paths, out var bounds))
                {
                    SignatureCompleted?.Invoke(this, Array.Empty<byte>());
                    return;
                }

                const int padding = 12;
                var width = Math.Max(1, (int)Math.Ceiling(bounds.Width) + (padding * 2));
                var height = Math.Max(1, (int)Math.Ceiling(bounds.Height) + (padding * 2));
                var info = new SKImageInfo(width, height);
                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Translate(-bounds.Left + padding, -bounds.Top + padding);

                foreach (var path in _paths)
                {
                    canvas.DrawPath(path, _paint);
                }

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var signatureBytes = data.ToArray();

                SignatureCompleted?.Invoke(this, signatureBytes);
            }
            else
            {
                SignatureCompleted?.Invoke(this, _attachedSignatureBytes ?? Array.Empty<byte>());
            }
        }

        private static bool TryGetSignatureBounds(IEnumerable<SKPath> paths, out SKRect bounds)
        {
            var hasBounds = false;
            bounds = SKRect.Empty;

            foreach (var path in paths)
            {
                if (path.IsEmpty)
                {
                    continue;
                }

                var pathBounds = path.Bounds;
                if (!hasBounds)
                {
                    bounds = pathBounds;
                    hasBounds = true;
                    continue;
                }

                bounds = SKRect.Union(bounds, pathBounds);
            }

            return hasBounds;
        }



        private void Cleanup()
        {
            _paint?.Dispose();
            _cachedPathsBitmap?.Dispose();
            foreach (var path in _paths)
            {
                path?.Dispose();
            }
            _paths.Clear();
            _currentPath?.Dispose();
        }
    }
}
