namespace StateMobile.Services;

public interface IDocumentScannerService
{
    Task<IReadOnlyList<byte[]>> ScanAsync(CancellationToken cancellationToken = default);
}
