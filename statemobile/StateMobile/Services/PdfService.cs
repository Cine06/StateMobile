using SkiaSharp;
using StateMobile.Models;

namespace StateMobile.Services
{
    public class PdfService : IPdfService
    {
        private const float A4Width = 595.27f;
        private const float A4Height = 841.89f;

        public async Task<string> GenerateA4PdfAsync(ScannedDocument doc)
        {
            var outputPath = Path.Combine(FileSystem.CacheDirectory, $"{doc.DocumentName}.pdf");
            
            using (var stream = File.Create(outputPath))
            using (var document = SKDocument.CreatePdf(stream))
            using (var canvas = document.BeginPage(A4Width, A4Height))
            {
                foreach (var page in doc.Pages)
                {
                    // 1. Draw Scanned Image (Fit to A4)
                    using (var bitmap = SKBitmap.Decode(page.ImagePath))
                    {
                        canvas.DrawBitmap(bitmap, new SKRect(0, 0, A4Width, A4Height));
                    }

                    // 2. Draw Signature if exists
                    if (page.SignatureData != null)
                    {
                        using (var sigBitmap = SKBitmap.Decode(page.SignatureData))
                        {
                            var sigWidth = 150f * (float)page.SignatureScale;
                            var sigHeight = 75f * (float)page.SignatureScale;
                            var rect = new SKRect((float)page.SignatureX, (float)page.SignatureY, 
                                               (float)page.SignatureX + sigWidth, (float)page.SignatureY + sigHeight);
                            canvas.DrawBitmap(sigBitmap, rect);
                        }
                    }
                    document.EndPage();
                }
                document.Close();
            }
            return outputPath;
        }
    }
}