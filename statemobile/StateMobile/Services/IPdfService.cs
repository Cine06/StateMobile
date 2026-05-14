using StateMobile.Models;

namespace StateMobile.Services
{
    public interface IPdfService
    {
        Task<byte[]> CreatePdfFromImagesAsync(List<byte[]> images);
        Task<byte[]> AddSignatureToPdfAsync(byte[] pdfBytes, byte[] signatureImage, int pageIndex, double x, double y, double scale);
    }
}
