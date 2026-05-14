namespace StateMobile.Services;

public sealed class FallbackDocumentScannerService : IDocumentScannerService
{
    public async Task<IReadOnlyList<byte[]>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var photo = await MediaPicker.Default.CapturePhotoAsync();
        if (photo == null)
        {
            return Array.Empty<byte[]>();
        }

        await using var stream = await photo.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        return new[] { memory.ToArray() };
    }
}
