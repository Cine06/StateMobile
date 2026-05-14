using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Storage;
using SkiaSharp;
using StateMobile.Models;
using StateMobile.Services;

namespace StateMobile.Views
{
    public partial class PdfPreviewPage : ContentPage, INotifyPropertyChanged
    {
        private readonly IPdfService _pdfService;
        private readonly bool _openSignatureOnLoad;
        private float _signatureThickness = 1.0f;
        private string _signatureColorHex = "#000000";
        private bool _signaturePromptShown;
        private bool _isSignatureToolsVisible;
        private SignaturePlacement? _activeSignaturePlacement;
        private int _selectedPageIndex;
        private double _previewCardWidth;
        private CancellationTokenSource? _signatureRenderCts;
        private int _signatureRenderVersion;
        private const int SignatureRenderDebounceMs = 40;
        private bool _isManipulatingSignature;
        private int _signatureToolsAnimationVersion;
        private AbsoluteLayout? _cachedBoundsLayout;
        private double _cachedBoundsWidth;
        private double _cachedBoundsHeight;
        private Grid? _cachedDragGrid;
        private byte[]? _cachedSignatureRawBytes;
        private SKBitmap? _cachedSignatureBitmap;
        private SignaturePlacement? _cachedPreviewPlacement;
        private string? _cachedPreviewColorHex;
        private float _cachedPreviewThickness = float.NaN;
        private byte[]? _cachedPreviewBytes;
    #if ANDROID
        private SignatureTouchInterceptionBlocker? _touchInterceptionBlocker;
    #endif
        private DateTime _lastManipulationTime;


        public ScannedDocument Document { get; }
        public ObservableCollection<PagePreviewItem> PreviewPages { get; }
        public bool IsSignatureAdded => PreviewPages.Any(page => page.Signatures.Count > 0);
        public bool IsSignatureToolsVisible => _activeSignaturePlacement != null && !_activeSignaturePlacement.IsLocked && _isSignatureToolsVisible;
        public int TotalPages => PreviewPages.Count;
        public double PreviewCardWidth
        {
            get => _previewCardWidth;
            private set
            {
                if (Math.Abs(_previewCardWidth - value) < 0.5)
                {
                    return;
                }

                _previewCardWidth = value;
                OnPropertyChanged();
            }
        }
        public int SelectedPageIndex
        {
            get => _selectedPageIndex;
            private set
            {
                if (_selectedPageIndex == value)
                {
                    return;
                }

                _selectedPageIndex = value;
                Document.SignaturePageIndex = value;
                UpdateSelectedPageStates();
                OnPropertyChanged();
            }
        }

        public PdfPreviewPage(IPdfService pdfService, ScannedDocument document, bool openSignatureOnLoad = false)
        {
            InitializeComponent();
            _pdfService = pdfService;
            _openSignatureOnLoad = openSignatureOnLoad;
            Document = document;
            PreviewPages = new ObservableCollection<PagePreviewItem>(
                Document.Pages.Select((pageBytes, pageIndex) => new PagePreviewItem(pageIndex, pageBytes)));
            foreach (var page in PreviewPages)
            {
                page.TotalPages = PreviewPages.Count;
            }
            SelectedPageIndex = PreviewPages.Count > 0
                ? Math.Clamp(Document.SignaturePageIndex, 0, PreviewPages.Count - 1)
                : 0;
            UpdateSelectedPageStates();
            BindingContext = this;
            UpdatePreviewCardWidth();
            pagesCollectionView.HandlerChanged += OnPagesCollectionViewHandlerChanged;
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            UpdatePreviewCardWidth(width);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            if (!_openSignatureOnLoad || _signaturePromptShown)
            {
                return;
            }

            _signaturePromptShown = true;
            OnAddSignatureClicked(this, EventArgs.Empty);
        }

        protected override void OnDisappearing()
        {
            CancelPendingSignatureRender();
            SetCollectionScrollEnabled(true);
            base.OnDisappearing();
        }

        private void OnPagesCollectionViewScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            // We update the SelectedPageIndex in real-time as the user scrolls
            // so that "Add Signature" always targets the visible page.
            UpdateSelectedPageIndexFromScroll();
        }

        private void UpdateSelectedPageIndexFromScroll()
        {
            if (PreviewPages == null || PreviewPages.Count <= 1) return;

#if ANDROID
            if (pagesCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
            {
                // Find the child view exactly at the center of the RecyclerView.
                // This is the most accurate way to detect which page the user is focused on.
                var centerX = recyclerView.Width / 2;
                var centerY = recyclerView.Height / 2;
                var centerChild = recyclerView.FindChildViewUnder(centerX, centerY);

                if (centerChild != null)
                {
                    // Ensure the recyclerView is still attached and valid before querying the adapter position.
                    var pos = recyclerView.GetChildAdapterPosition(centerChild);
                    if (pos >= 0 && pos < (PreviewPages?.Count ?? 0) && pos != SelectedPageIndex)
                    {
                        SelectedPageIndex = pos;
                    }
                }
            }
#elif IOS || MACCATALYST
            if (pagesCollectionView.Handler?.PlatformView is UIKit.UICollectionView collectionView)
            {
                // Find the item at the center point of the collection view's bounds.
                var bounds = collectionView.Bounds;
                var centerPoint = new CoreGraphics.CGPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                var indexPath = collectionView.IndexPathForItemAtPoint(centerPoint);
                
                if (indexPath != null)
                {
                    var pos = (int)indexPath.Item;
                    if (pos >= 0 && pos < (PreviewPages?.Count ?? 0) && pos != SelectedPageIndex)
                    {
                        SelectedPageIndex = pos;
                    }
                }
                else
                {
                    // Fallback to previous logic if center point check fails
                    var visiblePaths = collectionView.IndexPathsForVisibleItems;
                    if (visiblePaths != null && visiblePaths.Length > 0)
                    {
                        var indices = visiblePaths.Select(p => (int)p.Item).OrderBy(i => i).ToList();
                        var mid = indices[indices.Count / 2];
                        if (mid != SelectedPageIndex)
                        {
                            SelectedPageIndex = mid;
                        }
                    }
                }
            }
#endif
        }

        private void OnPagesCollectionViewHandlerChanged(object? sender, EventArgs e)
        {
            ApplyCollectionScrollState();
        }

        private void UpdatePreviewCardWidth(double? pageWidth = null)
        {
            var availableWidth = pageWidth ?? Width;
            if (availableWidth <= 0)
            {
                return;
            }

            PreviewCardWidth = Math.Max(availableWidth - 32, 320);
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private void OnAddSignatureClicked(object sender, EventArgs e)
        {
            // Ensure we are targeting the page the user is actually looking at.
            UpdateSelectedPageIndexFromScroll();

            var overlay = this.FindByName<Grid>("signatureOverlay");
            if (overlay != null)
            {
                overlay.IsVisible = true;
            }
        }

        private async void OnRenamePdfTapped(object sender, TappedEventArgs e)
        {
            await PromptRenamePdfAsync();
        }

        private async void OnRenamePdfClicked(object sender, EventArgs e)
        {
            await PromptRenamePdfAsync();
        }

        private async Task PromptRenamePdfAsync()
        {
            var currentName = System.IO.Path.GetFileNameWithoutExtension(Document.FileName);
            if (string.IsNullOrWhiteSpace(currentName))
            {
                currentName = "Scan";
            }

            var newName = await DisplayPromptAsync(
                "Rename PDF",
                "Enter a new file name",
                initialValue: currentName,
                maxLength: 80,
                keyboard: Keyboard.Default);

            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            newName = newName.Trim();
            foreach (var invalidChar in System.IO.Path.GetInvalidFileNameChars())
            {
                newName = newName.Replace(invalidChar.ToString(), string.Empty);
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            if (!newName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                newName += ".pdf";
            }

            Document.FileName = newName;

            var fileNameLabel = this.FindByName<Label>("lblFileName");
            if (fileNameLabel != null)
            {
                fileNameLabel.Text = Document.FileName;
            }
        }

        private async void OnSignatureCompleted(object sender, byte[] signatureBytes)
        {
            var overlay = this.FindByName<Grid>("signatureOverlay");
            if (overlay != null)
            {
                overlay.IsVisible = false;
            }

            if (signatureBytes == null || signatureBytes.Length == 0)
            {
                return;
            }

            _signatureColorHex = "#000000";

            Document.Signature = signatureBytes;
            Document.SignaturePreview = signatureBytes;
            var initialWidth = 170f;
            var initialHeight = 100f;
            using (var signatureBitmap = SKBitmap.Decode(signatureBytes))
            {
                if (signatureBitmap != null && signatureBitmap.Width > 0 && signatureBitmap.Height > 0)
                {
                    var aspect = signatureBitmap.Width / (float)signatureBitmap.Height;
                    initialHeight = Math.Clamp(initialWidth / Math.Max(0.01f, aspect), 70f, 220f);
                }
            }

            var placement = new SignaturePlacement
            {
                PageIndex = SelectedPageIndex,
                X = 30,
                Y = 40,
                Width = initialWidth,
                Height = initialHeight,
                RawBytes = signatureBytes,
                PreviewBytes = signatureBytes,
                IsLocked = false
            };

            var targetPage = GetSelectedPage();
            if (targetPage != null)
            {
                targetPage.Signatures.Add(placement);
            }

            SelectSignaturePlacement(placement, showTools: true);
            OnPropertyChanged(nameof(IsSignatureAdded));

            _ = RefreshSignaturePreviewAsync(showLoader: false, debounce: false);
        }

        private void OnSignatureTapped(object sender, TappedEventArgs e)
        {
            var placement = ResolvePlacementFromSender(sender);
            if (placement == null || placement.IsLocked)
            {
                return;
            }

            SelectSignaturePlacement(placement, showTools: true);
        }

        private double _startX, _startY;
        private void OnPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            var placement = ResolvePlacementFromSender(sender);
            if (!_isSignatureToolsVisible || placement == null || placement.IsLocked || !ReferenceEquals(_activeSignaturePlacement, placement))
            {
                return;
            }

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    BeginSignatureManipulation();
                    CacheBoundsLayout(sender);
                    _startX = placement.X;
                    _startY = placement.Y;
                    
                    // Dragging uses TranslationX/Y (render transforms), so we can safely 
                    // suppress layout to gain significant performance and smoothness.
                    SetRecyclerViewSuppressLayout(true);
                    break;
                case GestureStatus.Running:
                    if (_cachedDragGrid == null)
                    {
                        break;
                    }
                    // Set TranslationX/Y directly on the Grid — zero binding overhead.
                    var translateX = e.TotalX;
                    var translateY = e.TotalY;
                    if (TryGetCachedBounds(out var boundsWidth, out var boundsHeight))
                    {
                        var maxX = Math.Max(0.0, boundsWidth - placement.Width);
                        var maxY = Math.Max(0.0, boundsHeight - placement.Height);
                        translateX = Math.Clamp(_startX + translateX, 0.0, maxX) - _startX;
                        translateY = Math.Clamp(_startY + translateY, 0.0, maxY) - _startY;
                    }

                    // Only update if there is a meaningful change to reduce UI thread traffic.
                    if (Math.Abs(_cachedDragGrid.TranslationX - translateX) > 0.1 || 
                        Math.Abs(_cachedDragGrid.TranslationY - translateY) > 0.1)
                    {
                        _cachedDragGrid.TranslationX = translateX;
                        _cachedDragGrid.TranslationY = translateY;
                    }
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    // Commit the visual offset into the model position.
                    if (_cachedDragGrid != null)
                    {
                        var finalX = (float)(_startX + _cachedDragGrid.TranslationX);
                        var finalY = (float)(_startY + _cachedDragGrid.TranslationY);
                        
                        placement.BatchMove(finalX, finalY);
                        
                        SetRecyclerViewSuppressLayout(false);
                        EndSignatureManipulation();
                        
                        _cachedDragGrid.TranslationX = 0;
                        _cachedDragGrid.TranslationY = 0;
                    }
                    ClearCachedBoundsLayout();
                    break;
            }
        }

        private double _widthStart, _heightStart;
        private double _resizeAspectStart;
        private const double FineResizeFactor = 0.55;
        private const double FastResizeFactor = 1.7;
        private const double FineDragThreshold = 42.0;

        private void OnResizeHandlePan(object sender, PanUpdatedEventArgs e)
        {
            var placement = ResolvePlacementFromSender(sender);
            if (!_isSignatureToolsVisible || placement == null || placement.IsLocked || !ReferenceEquals(_activeSignaturePlacement, placement))
            {
                return;
            }

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    BeginSignatureManipulation();
                    CacheBoundsLayout(sender);
                    _widthStart = placement.Width;
                    _heightStart = placement.Height;
                    _resizeAspectStart = _heightStart <= 0 ? 1.0 : (_widthStart / _heightStart);
                    break;
                case GestureStatus.Running:
                    if (_cachedDragGrid == null)
                    {
                        break;
                    }

                    var totalX = e.TotalX;
                    var direction = Math.Sign(totalX);
                    var absDrag = Math.Abs(totalX);

                    // Small drags are intentionally fine-grained, while larger drags ramp up speed.
                    var blendedDrag = absDrag <= FineDragThreshold
                        ? absDrag * FineResizeFactor
                        : (FineDragThreshold * FineResizeFactor) + ((absDrag - FineDragThreshold) * FastResizeFactor);

                    var effectiveDeltaX = direction * blendedDrag;
                    var widthCandidate = Math.Clamp(_widthStart + effectiveDeltaX, 70, 900);

                    double newWidth, newHeight;

                    if (_resizeAspectStart <= 0.01)
                    {
                        newWidth = widthCandidate;
                        newHeight = Math.Clamp(_heightStart + e.TotalY, 36, 700);
                    }
                    else
                    {
                        newHeight = Math.Clamp(widthCandidate / _resizeAspectStart, 36, 700);
                        newWidth = Math.Clamp(newHeight * _resizeAspectStart, 70, 900);

                        if (TryGetCachedBounds(out var boundsWidth, out var boundsHeight))
                        {
                            var maxWidth = Math.Max(70.0, boundsWidth - placement.X);
                            var maxHeight = Math.Max(36.0, boundsHeight - placement.Y);

                            newWidth = Math.Clamp(newWidth, 70, maxWidth);
                            newHeight = Math.Clamp(newHeight, 36, maxHeight);
                        }
                    }

                    // Update LayoutBounds directly on the Grid for maximum performance.
                    // This is significantly faster than WidthRequest/HeightRequest in an AbsoluteLayout
                    // as it avoids the full measure-to-arrange layout cycle for the entire page.
                    if (Math.Abs(_cachedDragGrid.Width - newWidth) > 0.5 || 
                        Math.Abs(_cachedDragGrid.Height - newHeight) > 0.5)
                    {
                        AbsoluteLayout.SetLayoutBounds(_cachedDragGrid, new Rect(placement.X, placement.Y, newWidth, newHeight));
                    }
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    // Commit the visual size into the model.
                    if (_cachedDragGrid != null)
                    {
                        var bounds = AbsoluteLayout.GetLayoutBounds(_cachedDragGrid);
                        placement.BatchResize(bounds.Width, bounds.Height);
                        
                        EndSignatureManipulation();
                        
                        // No need to reset WidthRequest/-1 here as we used SetLayoutBounds directly.
                    }
                    ClearCachedBoundsLayout();
                    break;
            }
        }

        private void OnRemoveSignatureTapped(object sender, TappedEventArgs e)
        {
            var placement = ResolvePlacementFromSender(sender);
            if (placement == null || placement.IsLocked)
            {
                return;
            }

            CancelPendingSignatureRender();
            var page = PreviewPages.FirstOrDefault(item => item.PageIndex == placement.PageIndex);
            page?.Signatures.Remove(placement);
            ClearSignatureSelection();
            Document.Signature = null;
            Document.SignaturePreview = null;
            EndSignatureManipulation();
            OnPropertyChanged(nameof(IsSignatureAdded));
            _ = UpdateSignatureToolsVisibilityAsync();
        }

        private SignaturePlacement? ResolvePlacementFromSender(object sender)
        {
            if (sender is BindableObject bindable && bindable.BindingContext is SignaturePlacement placement)
            {
                return placement;
            }

            return _activeSignaturePlacement;
        }

        /// <summary>
        /// Cache the AbsoluteLayout ancestor and parent Grid on gesture start.
        /// </summary>
        private void CacheBoundsLayout(object sender)
        {
            if (sender is Element element)
            {
                // The sender (Border) is a child of the positioned Grid.
                _cachedDragGrid = element.Parent as Grid;

                _cachedBoundsLayout = FindAncestor<AbsoluteLayout>(element);
                if (_cachedBoundsLayout != null && _cachedBoundsLayout.Width > 0 && _cachedBoundsLayout.Height > 0)
                {
                    _cachedBoundsWidth = _cachedBoundsLayout.Width;
                    _cachedBoundsHeight = _cachedBoundsLayout.Height;
                }
                else
                {
                    _cachedBoundsLayout = null;
                    _cachedBoundsWidth = 0;
                    _cachedBoundsHeight = 0;
                }
            }
        }

        private void ClearCachedBoundsLayout()
        {
            _cachedBoundsLayout = null;
            _cachedBoundsWidth = 0;
            _cachedBoundsHeight = 0;
            _cachedDragGrid = null;
        }

        private bool TryGetCachedBounds(out double boundsWidth, out double boundsHeight)
        {
            if (_cachedBoundsLayout != null && _cachedBoundsWidth > 0 && _cachedBoundsHeight > 0)
            {
                boundsWidth = _cachedBoundsWidth;
                boundsHeight = _cachedBoundsHeight;
                return true;
            }

            boundsWidth = 0;
            boundsHeight = 0;
            return false;
        }

        private static T? FindAncestor<T>(Element? element) where T : Element
        {
            while (element != null)
            {
                if (element is T match)
                {
                    return match;
                }

                element = element.Parent;
            }

            return null;
        }

        private void OnSignatureEditOkClicked(object sender, EventArgs e)
        {
            if (_activeSignaturePlacement != null)
            {
                _activeSignaturePlacement.IsLocked = true;
            }

            ClearSignatureSelection();
            EndSignatureManipulation();
        }

        private void OnSignatureThicknessChanged(object sender, ValueChangedEventArgs e)
        {
            if (_activeSignaturePlacement?.RawBytes == null)
            {
                return;
            }

            _signatureThickness = (float)e.NewValue;

            var scaleLabel = this.FindByName<Label>("lblSignatureScale");
            if (scaleLabel != null)
            {
                scaleLabel.Text = $"{_signatureThickness:0.0}x";
            }

            _ = RefreshSignaturePreviewAsync(showLoader: false, debounce: true);
        }

        private void OnSignatureColorClicked(object sender, EventArgs e)
        {
            if (_activeSignaturePlacement?.RawBytes == null)
            {
                return;
            }

            var color = sender is Button button && button.CommandParameter is string hex && !string.IsNullOrWhiteSpace(hex)
                ? hex
                : "#000000";

            _signatureColorHex = color;

            _ = RefreshSignaturePreviewAsync(showLoader: false, debounce: false);
        }

        private void OnPageTapped(object sender, TappedEventArgs e)
        {
            // Ignore ghost taps that occasionally occur right after a pan gesture completes.
            // This prevents the selection from being cleared unintentionally.
            if ((DateTime.UtcNow - _lastManipulationTime).TotalMilliseconds < 300)
            {
                return;
            }

            if (_activeSignaturePlacement != null && !_activeSignaturePlacement.IsLocked)
            {
                ClearSignatureSelection();
                EndSignatureManipulation();
            }

            var pageItem = ResolvePageItemFromSender(sender);
            if (pageItem != null)
            {
                SelectedPageIndex = pageItem.PageIndex;
            }
        }

        private PagePreviewItem? ResolvePageItemFromSender(object sender)
        {
            if (sender is BindableObject bindable && bindable.BindingContext is PagePreviewItem pageItem)
            {
                return pageItem;
            }

            if (sender is Element element)
            {
                var border = FindAncestor<Border>(element);
                if (border?.BindingContext is PagePreviewItem ancestorPageItem)
                {
                    return ancestorPageItem;
                }
            }

            return null;
        }

        private void SelectSignaturePlacement(SignaturePlacement placement, bool showTools)
        {
            if (_activeSignaturePlacement != null && !ReferenceEquals(_activeSignaturePlacement, placement))
            {
                _activeSignaturePlacement.IsEditing = false;
            }

            _activeSignaturePlacement = placement;
            _activeSignaturePlacement.IsEditing = true;
            _isSignatureToolsVisible = showTools && !_activeSignaturePlacement.IsLocked;
            OnPropertyChanged(nameof(IsSignatureToolsVisible));
            _ = UpdateSignatureToolsVisibilityAsync();
        }

        private void ClearSignatureSelection()
        {
            if (_activeSignaturePlacement != null)
            {
                _activeSignaturePlacement.IsEditing = false;
            }

            _activeSignaturePlacement = null;
            _isSignatureToolsVisible = false;
            OnPropertyChanged(nameof(IsSignatureToolsVisible));
            _ = UpdateSignatureToolsVisibilityAsync();
        }

        private PagePreviewItem? GetSelectedPage() =>
            PreviewPages.FirstOrDefault(page => page.PageIndex == SelectedPageIndex);

        private void UpdateSelectedPageStates()
        {
            foreach (var page in PreviewPages)
            {
                page.IsSelected = page.PageIndex == SelectedPageIndex;
            }
        }

        private async Task RefreshSignaturePreviewAsync(bool showLoader, bool debounce)
        {
            var placement = _activeSignaturePlacement;
            if (placement?.RawBytes == null)
            {
                return;
            }

            CancelPendingSignatureRender();
            var cts = new CancellationTokenSource();
            _signatureRenderCts = cts;
            var token = cts.Token;
            var renderVersion = ++_signatureRenderVersion;

            try
            {
                if (debounce)
                {
                    await Task.Delay(SignatureRenderDebounceMs, token);
                }

                if (showLoader && loadingOverlay != null)
                {
                    loadingOverlay.IsVisible = true;
                }

                var signatureBytes = placement.RawBytes;
                if (signatureBytes == null)
                {
                    return;
                }

                var color = _signatureColorHex;
                var thickness = QuantizeSignatureThickness(_signatureThickness);

                if (TryGetCachedSignaturePreview(placement, color, thickness, out var cachedPreview))
                {
                    placement.PreviewBytes = cachedPreview;
                    Document.SignaturePreview = cachedPreview;
                    OnPropertyChanged(nameof(IsSignatureAdded));
                    _ = UpdateSignatureToolsVisibilityAsync();
                    return;
                }

                if (!TryEnsureSignatureBitmapCache(signatureBytes, out var sourceBitmap))
                {
                    return;
                }

                var bitmapCopy = sourceBitmap.Copy();
                var preview = await Task.Run(() =>
                {
                    try { return RenderSignaturePreview(bitmapCopy, color, thickness); }
                    finally { bitmapCopy.Dispose(); }
                }, token);

                if (token.IsCancellationRequested || renderVersion != _signatureRenderVersion || _activeSignaturePlacement != placement)
                {
                    return;
                }

                placement.PreviewBytes = preview;
                Document.SignaturePreview = preview;
                CacheSignaturePreview(placement, color, thickness, preview);
                OnPropertyChanged(nameof(IsSignatureAdded));
                _ = UpdateSignatureToolsVisibilityAsync();
            }
            catch (OperationCanceledException)
            {
                // Another signature edit arrived; only the latest render should apply.
            }
            finally
            {
                if (showLoader && loadingOverlay != null && ReferenceEquals(_signatureRenderCts, cts))
                {
                    loadingOverlay.IsVisible = false;
                }
            }
        }

        private void CancelPendingSignatureRender()
        {
            _signatureRenderCts?.Cancel();
            _signatureRenderCts?.Dispose();
            _signatureRenderCts = null;
            _signatureRenderVersion++;
        }

        private static float QuantizeSignatureThickness(float value)
        {
            return (float)Math.Round(value * 20f) / 20f;
        }

        private bool TryEnsureSignatureBitmapCache(byte[] signatureBytes, out SKBitmap cachedBitmap)
        {
            if (ReferenceEquals(_cachedSignatureRawBytes, signatureBytes)
                && _cachedSignatureBitmap != null
                && !_cachedSignatureBitmap.IsNull)
            {
                cachedBitmap = _cachedSignatureBitmap;
                return true;
            }

            _cachedSignatureBitmap?.Dispose();
            _cachedSignatureBitmap = null;

            var bitmap = SKBitmap.Decode(signatureBytes);
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                bitmap?.Dispose();
                cachedBitmap = null!;
                return false;
            }

            _cachedSignatureRawBytes = signatureBytes;
            _cachedSignatureBitmap = bitmap;
            cachedBitmap = bitmap;
            return true;
        }

        private bool TryGetCachedSignaturePreview(SignaturePlacement placement, string colorHex, float thickness, out byte[] preview)
        {
            if (ReferenceEquals(_cachedPreviewPlacement, placement)
                && string.Equals(_cachedPreviewColorHex, colorHex, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(_cachedPreviewThickness - thickness) < 0.001f
                && _cachedPreviewBytes != null)
            {
                preview = _cachedPreviewBytes;
                return true;
            }

            preview = Array.Empty<byte>();
            return false;
        }

        private void CacheSignaturePreview(SignaturePlacement placement, string colorHex, float thickness, byte[] preview)
        {
            _cachedPreviewPlacement = placement;
            _cachedPreviewColorHex = colorHex;
            _cachedPreviewThickness = thickness;
            _cachedPreviewBytes = preview;
        }

        private async Task UpdateSignatureToolsVisibilityAsync()
        {
            if (signatureTools != null)
            {
                var animationVersion = ++_signatureToolsAnimationVersion;
                var shouldShow = IsSignatureToolsVisible;
                ApplyCollectionScrollState();

                if (shouldShow)
                {
                    signatureTools.IsVisible = true;
                    signatureTools.Opacity = 0;
                    signatureTools.TranslationY = 10;
                    signatureTools.Scale = 0.98;

                    await Task.WhenAll(
                        signatureTools.FadeTo(1, 160, Easing.CubicOut),
                        signatureTools.TranslateTo(0, 0, 160, Easing.CubicOut),
                        signatureTools.ScaleTo(1, 160, Easing.CubicOut));
                }
                else if (signatureTools.IsVisible)
                {
                    await Task.WhenAll(
                        signatureTools.FadeTo(0, 120, Easing.CubicIn),
                        signatureTools.TranslateTo(0, 10, 120, Easing.CubicIn),
                        signatureTools.ScaleTo(0.98, 120, Easing.CubicIn));

                    if (animationVersion == _signatureToolsAnimationVersion)
                    {
                        signatureTools.IsVisible = false;
                    }
                }
            }

            ApplyCollectionScrollState();
        }

        private void ApplyCollectionScrollState()
        {
            SetCollectionScrollEnabled(!(_isSignatureToolsVisible || _isManipulatingSignature));
        }

        private void BeginSignatureManipulation()
        {
            if (_isManipulatingSignature)
            {
                return;
            }

            _isManipulatingSignature = true;
        }

        private void EndSignatureManipulation()
        {
            _lastManipulationTime = DateTime.UtcNow;
            if (!_isManipulatingSignature)
            {
                return;
            }

            _isManipulatingSignature = false;
            ApplyCollectionScrollState();
        }

        private void SetCollectionScrollEnabled(bool isEnabled)
        {
#if ANDROID
            if (pagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
            {
                recyclerView.NestedScrollingEnabled = isEnabled;
                
                // Ensure neither the recycler nor its parents intercept our signature touches.
                ((Android.Views.ViewGroup)recyclerView).RequestDisallowInterceptTouchEvent(!isEnabled);
                recyclerView.Parent?.RequestDisallowInterceptTouchEvent(!isEnabled);

                if (isEnabled)
                {
                    if (_touchInterceptionBlocker != null)
                    {
                        recyclerView.RemoveOnItemTouchListener(_touchInterceptionBlocker);
                        _touchInterceptionBlocker = null;
                    }
                }
                else
                {
                    recyclerView.StopScroll();
                    if (_touchInterceptionBlocker == null)
                    {
                        _touchInterceptionBlocker = new SignatureTouchInterceptionBlocker();
                        recyclerView.AddOnItemTouchListener(_touchInterceptionBlocker);
                    }
                }
            }
#elif IOS || MACCATALYST
            if (pagesCollectionView?.Handler?.PlatformView is UIKit.UICollectionView collectionView)
            {
                collectionView.ScrollEnabled = isEnabled;
            }
#endif
        }

        private void SetRecyclerViewSuppressLayout(bool suppress)
        {
#if ANDROID
            if (pagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
            {
                recyclerView.SuppressLayout(suppress);
            }
#endif
        }

#if ANDROID
        private sealed class SignatureTouchInterceptionBlocker : Java.Lang.Object, AndroidX.RecyclerView.Widget.RecyclerView.IOnItemTouchListener
        {
            public bool OnInterceptTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
            {
                // When a touch starts anywhere in the RecyclerView while this listener is active,
                // we tell the recycler and its parents NOT to intercept move events.
                // This prevents the list from scrolling when the user is trying to drag/resize a signature.
                if (e.ActionMasked == Android.Views.MotionEventActions.Down)
                {
                    rv.StopScroll(); // Force stop any ongoing fling/scroll.
                    ((Android.Views.ViewGroup)rv).RequestDisallowInterceptTouchEvent(true);
                    rv.Parent?.RequestDisallowInterceptTouchEvent(true);
                }
                return false; // Return false so the touch is still dispatched to the children.
            }

            public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept) { }

            public void OnTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e) { }
        }
#endif



        private async Task<byte[]> BuildFinalPdfAsync()
        {
            // ✅ Ensure we have a valid reference width. If the UI hasn't laid out yet (NaN or 0), 
            // we default to the screen width to prevent downstream calculation errors (like NaN scaling).
            var currentWidth = Width;
            if (double.IsNaN(currentWidth) || currentWidth <= 0)
            {
                if (Application.Current?.MainPage != null)
                    currentWidth = Application.Current.MainPage.Width;
                else
                    currentWidth = (double)DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
            }

            var referenceWidth = (float)(PreviewCardWidth > 0 ? PreviewCardWidth : Math.Max(currentWidth - 32, 320));
            if (float.IsNaN(referenceWidth) || referenceWidth <= 0) referenceWidth = 320f;
            
            var doc = Document;
            if (doc == null)
            {
                throw new InvalidOperationException("Scanned document is missing. Cannot generate PDF.");
            }

            var pages = doc.Pages?.ToList();
            if (pages == null || pages.Count == 0)
            {
                throw new InvalidOperationException("No pages found in the document to generate a PDF.");
            }

            var previews = PreviewPages;
            if (previews == null)
            {
                throw new InvalidOperationException("Preview items are missing. Cannot map signatures.");
            }

            for (var pageIndex = 0; pageIndex < previews.Count && pageIndex < pages.Count; pageIndex++)
            {
                var page = previews[pageIndex];
                if (page == null) continue;
                
                // We process all pages with a target width of 1400px for a professional balance of quality and file size.
                // This ensures the document stays under server payload limits while remaining crystal clear.
                pages[pageIndex] = await ApplySignaturesToImage(pages[pageIndex], page.Signatures.ToList(), referenceWidth, 1400f);
            }

            var pdfBytes = await _pdfService.CreatePdfFromImagesAsync(pages);
            if (pdfBytes != null)
            {
                doc.FinalPdf = pdfBytes;
            }
            return pdfBytes ?? Array.Empty<byte>();
        }

        private async Task<string> SavePdfAsync(byte[] pdfBytes)
        {
            var doc = Document;
            if (doc == null) return string.Empty;

            var baseName = System.IO.Path.GetFileNameWithoutExtension(doc.FileName);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}";
            }

            var fileName = $"{baseName}.pdf";
            await using var stream = new MemoryStream(pdfBytes);
            var result = await FileSaver.Default.SaveAsync(fileName, stream);

            if (!result.IsSuccessful)
            {
                throw result.Exception ?? new InvalidOperationException("The PDF save was canceled or could not be completed.");
            }

            return result.FilePath;
        }

        private async Task<byte[]> BuildAndSavePdfAsync()
        {
            var pdfBytes = await BuildFinalPdfAsync();
            await SavePdfAsync(pdfBytes);
            return pdfBytes;
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var loader = this.FindByName<Grid>("loadingOverlay");
            if (loader != null)
            {
                loader.IsVisible = true;
                if (lblLoadingText != null) lblLoadingText.Text = "Saving PDF...";
            }

            try
            {
                var pdfBytes = await BuildFinalPdfAsync();
                var filePath = await SavePdfAsync(pdfBytes);
                await DisplayAlertAsync("Saved", $"PDF saved to:\n{filePath}", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"Could not save PDF: {ex.Message}", "OK");
            }
            finally
            {
                if (loader != null)
                {
                    loader.IsVisible = false;
                    if (lblLoadingText != null) lblLoadingText.Text = "Generating PDF...";
                }
            }
        }

        private async void OnSaveAndSendClicked(object sender, EventArgs e)
        {
            var loader = this.FindByName<Grid>("loadingOverlay");
            if (loader != null)
            {
                loader.IsVisible = true;
                if (lblLoadingText != null) lblLoadingText.Text = "Saving and sending PDF...";
            }

            try
            {
                var pdfBytes = await BuildAndSavePdfAsync();
                WeakReferenceMessenger.Default.Send(new PdfSharePayload(pdfBytes, Document?.FileName ?? "Scan.pdf"));
                if (Navigation != null) await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"Could not save and send PDF: {ex.Message}", "OK");
            }
            finally
            {
                if (loader != null)
                {
                    loader.IsVisible = false;
                    if (lblLoadingText != null) lblLoadingText.Text = "Generating PDF...";
                }
            }
        }

        private async void OnSendClicked(object sender, EventArgs e)
        {
            var loader = this.FindByName<Grid>("loadingOverlay");
            if (loader != null)
            {
                loader.IsVisible = true;
                if (lblLoadingText != null) lblLoadingText.Text = "Sending PDF...";
            }

            try
            {
                var pdfBytes = await BuildFinalPdfAsync();
                WeakReferenceMessenger.Default.Send(new PdfSharePayload(pdfBytes, Document?.FileName ?? "Scan.pdf"));
                if (Navigation != null)
                {
                    await Navigation.PopModalAsync();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"Could not generate or send PDF: {ex.Message}", "OK");
            }
            finally
            {
                if (loader != null)
                {
                    loader.IsVisible = false;
                    if (lblLoadingText != null) lblLoadingText.Text = "Generating PDF...";
                }
            }
        }

        private async Task<byte[]> ApplySignaturesToImage(byte[] imageBytes, IReadOnlyList<SignaturePlacement> placements, float previewWidth, float targetWidth = 1400f)
        {
            return await Task.Run(() =>
            {
                if (imageBytes == null || imageBytes.Length == 0) return Array.Empty<byte>();
                if (placements == null) return imageBytes;

                try
                {
                    using var originalBmp = SKBitmap.Decode(imageBytes);
                if (originalBmp == null) return imageBytes;

                using var codec = SKCodec.Create(new MemoryStream(imageBytes));
                var origin = codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
                using var correctlyOrientedBmp = OrientBitmap(originalBmp, origin);
                var bmp = correctlyOrientedBmp;
                if (bmp == null) return imageBytes;

                // 1. Normalize dimensions to the target width while preserving aspect ratio.
                // 1400px is excellent for readability while keeping the resulting PDF manageable for mobile networks.
                var finalWidth = targetWidth;
                var finalHeight = finalWidth * bmp.Height / Math.Max(1f, bmp.Width);

                // 2. Use Rgb888x (no alpha) for the base page to ensure absolute color consistency in PDF viewers.
                var info = new SKImageInfo((int)finalWidth, (int)finalHeight, SKColorType.Rgba8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb());
                using var surface = SKSurface.Create(info);
                if (surface == null) return imageBytes; // Safety: could happen if dimensions are too large or OOM.

                var canvas = surface.Canvas;
                if (canvas == null) return imageBytes;
                
                // Start with a clean white base
                canvas.DrawColor(SKColors.White);

                using var cleanPaint = new SKPaint 
                { 
                    FilterQuality = SKFilterQuality.High,
                    IsAntialias = true
                };
                
                // Draw the original image scaled to the normalized size.
                canvas.DrawBitmap(bmp, new SKRect(0, 0, finalWidth, finalHeight), cleanPaint);

                var previewHeight = previewWidth * finalHeight / Math.Max(1f, finalWidth);

                foreach (var placement in placements)
                {
                    if (placement == null) continue;
                    if (placement.PreviewBytes == null || placement.Width <= 0 || placement.Height <= 0)
                    {
                        continue;
                    }

                    using var sigBmp = SKBitmap.Decode(placement.PreviewBytes);
                    if (sigBmp == null)
                    {
                        continue;
                    }

                    // Scale the signature relative to the actual bitmap pixels.
                    var scaleX = finalWidth / previewWidth;
                    var scaleY = finalHeight / previewHeight;
                    var sigWidth = (float)placement.Width * scaleX;
                    var sigHeight = (float)placement.Height * scaleY;
                    var finalX = (float)placement.X * scaleX;
                    var finalY = (float)placement.Y * scaleY;

                    canvas.DrawBitmap(sigBmp, new SKRect(finalX, finalY, finalX + sigWidth, finalY + sigHeight), cleanPaint);
                }

                using var image = surface.Snapshot();
                if (image == null) return imageBytes;

                // ✅ Use 90% Jpeg quality. This is the industry standard for professional documents
                // because it maintains visual fidelity while significantly reducing the binary size for SignalR transfer.
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
                if (data == null) return imageBytes;

                return data.ToArray();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ ApplySignaturesToImage failure: {ex.Message}");
                    return imageBytes;
                }
            });
        }

        private static byte[] RenderSignaturePreview(SKBitmap sourceBitmap, string hexColor, float thicknessScale)
        {
            var isDefaultColor = string.Equals(hexColor, "#000000", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hexColor, "#FF000000", StringComparison.OrdinalIgnoreCase);

            if (isDefaultColor && Math.Abs(thicknessScale - 1.0f) < 0.01f)
            {
                using var originalImage = SKImage.FromBitmap(sourceBitmap);
                using var originalData = originalImage.Encode(SKEncodedImageFormat.Png, 90);
                return originalData.ToArray();
            }

            var width = sourceBitmap.Width;
            var height = sourceBitmap.Height;
            var scale = Math.Clamp(thicknessScale, 0.5f, 3.0f);
            var signedRadius = (scale - 1.0f) * 3.0f;
            var radius = (int)Math.Round(Math.Abs(signedRadius));

            var padding = Math.Max(4, radius + 2);
            var outputInfo = new SKImageInfo(
                width + (padding * 2),
                height + (padding * 2),
                SKColorType.Rgba8888,
                SKAlphaType.Premul);

            using var surface = SKSurface.Create(outputInfo);
            if (surface == null) return Array.Empty<byte>();

            var canvas = surface.Canvas;
            if (canvas == null) return Array.Empty<byte>();
            canvas.Clear(SKColors.Transparent);

            var targetColor = SKColor.Parse(hexColor);

            // Color matrix replaces RGB with target color while preserving alpha.
            // Single GPU-accelerated operation instead of pixel-by-pixel loop.
            var r = targetColor.Red / 255f;
            var g = targetColor.Green / 255f;
            var b = targetColor.Blue / 255f;

            using var paint = new SKPaint();
            paint.IsAntialias = true;
            paint.FilterQuality = SKFilterQuality.Medium;

            paint.ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
            {
                0, 0, 0, 0, r,
                0, 0, 0, 0, g,
                0, 0, 0, 0, b,
                0, 0, 0, 1, 0
            });

            // Morphology (dilate/erode) for thickness via built-in image filter.
            if (radius > 0)
            {
                paint.ImageFilter = signedRadius > 0
                    ? SKImageFilter.CreateDilate(radius, radius)
                    : SKImageFilter.CreateErode(radius, radius);
            }

            canvas.DrawBitmap(sourceBitmap, padding, padding, paint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }


        private static SKBitmap OrientBitmap(SKBitmap bitmap, SKEncodedOrigin origin)
        {
            if (origin == SKEncodedOrigin.TopLeft || origin == SKEncodedOrigin.Default) 
                return bitmap.Copy() ?? new SKBitmap(bitmap.Info); // Always return a copy to avoid double disposal

            int width = bitmap.Width;
            int height = bitmap.Height;
            bool swap = origin == SKEncodedOrigin.RightTop || origin == SKEncodedOrigin.LeftBottom ||
                        origin == SKEncodedOrigin.LeftTop || origin == SKEncodedOrigin.RightBottom;

            var rotatedInfo = new SKImageInfo(swap ? height : width, swap ? width : height, bitmap.Info.ColorType, bitmap.Info.AlphaType, bitmap.Info.ColorSpace);
            var rotated = new SKBitmap(rotatedInfo);

            using (var canvas = new SKCanvas(rotated))
            {
                canvas.Clear(SKColors.White);
                canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);

                switch (origin)
                {
                    case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); break;
                    case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180); break;
                    case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); break;
                    case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                    case SKEncodedOrigin.RightTop: canvas.RotateDegrees(90); break;
                    case SKEncodedOrigin.RightBottom: canvas.RotateDegrees(270); canvas.Scale(-1, 1); break;
                    case SKEncodedOrigin.LeftBottom: canvas.RotateDegrees(270); break;
                }

                canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
                canvas.DrawBitmap(bitmap, 0, 0);
            }

            return rotated;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected new void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
