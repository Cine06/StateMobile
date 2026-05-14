#if ANDROID
using Android.App;
using Android.Content;
using Google.MLKit.Vision.Documentscanner;
using Microsoft.Maui.ApplicationModel;
using StateMobile.Services;

namespace StateMobile.Platforms.Android;

public sealed class AndroidMlKitDocumentScannerService : IDocumentScannerService
{
    private const int ScanRequestCode = 7361;
    private static readonly GmsDocumentScannerOptions ScannerOptions = new GmsDocumentScannerOptions.Builder()
        .SetScannerMode(GmsDocumentScannerOptions.ScannerModeBaseWithFilter)
        .SetGalleryImportAllowed(true)
        .SetResultFormats(GmsDocumentScannerOptions.ResultFormatJpeg)
        .Build();

    public async Task<IReadOnlyList<byte[]>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var activity = MainActivity.Current ?? Platform.CurrentActivity as Activity;
        if (activity == null)
        {
            throw new InvalidOperationException("No active Android activity is available.");
        }

        var scanner = GmsDocumentScanning.GetClient(ScannerOptions);
        var intentSenderObj = await AwaitTaskAsync(scanner.GetStartScanIntent(activity), cancellationToken);

        if (intentSenderObj is not IntentSender intentSender)
        {
            throw new InvalidOperationException("Unable to create ML Kit scanner intent.");
        }

        var resultTask = new TaskCompletionSource<(Result ResultCode, Intent? Data)>();
        EventHandler<ActivityResultEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (args.RequestCode != ScanRequestCode)
            {
                return;
            }

            MainActivity.ActivityResultReceived -= handler;
            resultTask.TrySetResult((args.ResultCode, args.Data));
        };

        MainActivity.ActivityResultReceived += handler;

        try
        {
            activity.StartIntentSenderForResult(intentSender, ScanRequestCode, null, 0, 0, 0);
            var (resultCode, data) = await resultTask.Task.WaitAsync(cancellationToken);

            if (resultCode != Result.Ok || data == null)
            {
                return Array.Empty<byte[]>();
            }

            var scanResult = GmsDocumentScanningResult.FromActivityResultIntent(data);
            var pages = scanResult?.Pages;
            if (pages == null || pages.Count == 0)
            {
                return Array.Empty<byte[]>();
            }

            var contentResolver = activity.ContentResolver;
            var output = new List<byte[]>(pages.Count);

            foreach (var page in pages)
            {
                var uri = page.ImageUri;
                if (uri == null)
                {
                    continue;
                }

                await using var stream = contentResolver?.OpenInputStream(uri);
                if (stream == null)
                {
                    continue;
                }

                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken);
                output.Add(memory.ToArray());
            }

            return output;
        }
        catch (IntentSender.SendIntentException ex)
        {
            throw new InvalidOperationException("Unable to launch ML Kit scanner.", ex);
        }
        finally
        {
            MainActivity.ActivityResultReceived -= handler;
        }
    }

    private static Task<Java.Lang.Object?> AwaitTaskAsync(global::Android.Gms.Tasks.Task task, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<Java.Lang.Object?>();

        task
            .AddOnSuccessListener(new SuccessListener(completion))
            .AddOnFailureListener(new FailureListener(completion))
            .AddOnCanceledListener(new CanceledListener(completion));

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        }

        return completion.Task;
    }

    private sealed class SuccessListener(TaskCompletionSource<Java.Lang.Object?> completion)
        : Java.Lang.Object, global::Android.Gms.Tasks.IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result)
        {
            completion.TrySetResult(result);
        }
    }

    private sealed class FailureListener(TaskCompletionSource<Java.Lang.Object?> completion)
        : Java.Lang.Object, global::Android.Gms.Tasks.IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception e)
        {
            completion.TrySetException(new InvalidOperationException(e.Message, e));
        }
    }

    private sealed class CanceledListener(TaskCompletionSource<Java.Lang.Object?> completion)
        : Java.Lang.Object, global::Android.Gms.Tasks.IOnCanceledListener
    {
        public void OnCanceled()
        {
            completion.TrySetCanceled();
        }
    }
}
#endif
