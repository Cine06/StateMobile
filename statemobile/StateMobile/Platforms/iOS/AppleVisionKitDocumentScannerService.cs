#if IOS
using Foundation;
using Microsoft.Maui.ApplicationModel;
using StateMobile.Services;
using UIKit;
using VisionKit;

namespace StateMobile.Platforms.iOS;

public sealed class AppleVisionKitDocumentScannerService : IDocumentScannerService
{
    public Task<IReadOnlyList<byte[]>> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!VNDocumentCameraViewController.Supported)
        {
            throw new InvalidOperationException("Document scanning is not supported on this device.");
        }

        var tcs = new TaskCompletionSource<IReadOnlyList<byte[]>>();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var root = GetTopViewController();
            if (root == null)
            {
                tcs.TrySetException(new InvalidOperationException("No active iOS view controller is available."));
                return;
            }

            var scanner = new VNDocumentCameraViewController();
            var delegateImpl = new VisionScanDelegate(
                onCompleted: pages => tcs.TrySetResult(pages),
                onCanceled: () => tcs.TrySetResult(Array.Empty<byte[]>()),
                onFailed: error => tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription)));

            scanner.Delegate = delegateImpl;
            scanner.PresentationController!.Delegate = delegateImpl;
            root.PresentViewController(scanner, true, null);
        });

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    private static UIViewController? GetTopViewController()
    {
        var window = UIApplication.SharedApplication
            .ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);

        var root = window?.RootViewController;
        while (root?.PresentedViewController != null)
        {
            root = root.PresentedViewController;
        }

        return root;
    }

    private sealed class VisionScanDelegate(
        Action<IReadOnlyList<byte[]>> onCompleted,
        Action onCanceled,
        Action<NSError> onFailed)
        : VNDocumentCameraViewControllerDelegate, IUIAdaptivePresentationControllerDelegate
    {
        public override void DidFinish(VNDocumentCameraViewController controller, VNDocumentCameraScan scan)
        {
            var pages = new List<byte[]>();

            for (nuint i = 0; i < scan.PageCount; i++)
            {
                using var image = scan.GetImage(i);
                using var data = image.AsJPEG();
                if (data != null)
                {
                    pages.Add(data.ToArray());
                }
            }

            controller.DismissViewController(true, () => onCompleted(pages));
        }

        public override void DidCancel(VNDocumentCameraViewController controller)
        {
            controller.DismissViewController(true, onCanceled);
        }

        public override void DidFail(VNDocumentCameraViewController controller, NSError error)
        {
            controller.DismissViewController(true, () => onFailed(error));
        }

        public void DidDismiss(UIPresentationController presentationController)
        {
            onCanceled();
        }
    }
}
#endif
