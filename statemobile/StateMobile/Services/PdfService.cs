using SkiaSharp;
using StateMobile.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace StateMobile.Services
{
    public class PdfService : IPdfService
    {
        // Standard A4 dimensions in points (72 DPI)
        private const float A4Width = 595.27f;
        private const float A4Height = 841.89f;

        public async Task<string> GeneratePdfAsync(ScannedDocument document)
        {
            var fileName = $"{document.Title ?? "Scanned_Doc"}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var outputPath = Path.Combine(FileSystem.CacheDirectory, fileName);

            using (var stream = new SKFileWStream(outputPath))
            using (var pdfDoc = SKDocument.CreatePdf(stream))
            {
                foreach (var page in document.Pages)
                {
                    using (var canvas = pdfDoc.BeginPage(A4Width, A4Height))
                    {
                        // 1. Draw the Scanned Image
                        if (File.Exists(page.ImagePath))
                        {
                            using (var bitmap = SKBitmap.Decode(page.ImagePath))
                            {
                                // Calculate scaling to fit A4 while maintaining aspect ratio
                                float scale = Math.Min(A4Width / bitmap.Width, A4Height / bitmap.Height);
                                float x = (A4Width - bitmap.Width * scale) / 2;
                                float y = (A4Height - bitmap.Height * scale) / 2;

                                var destRect = new SKRect(x, y, x + bitmap.Width * scale, y + bitmap.Height * scale);
                                canvas.DrawBitmap(bitmap, destRect);

                                // 2. Draw Signature if it exists for this specific page
                                if (page.Signature != null && page.Signature.SignatureData != null)
                                {
                                    using (var sigBitmap = SKBitmap.Decode(page.Signature.SignatureData))
                                    {
                                        // Map signature coordinates from preview space to PDF space
                                        // Assuming X/Y/W/H are relative to the page size
                                        float sigX = (float)page.Signature.X;
                                        float sigY = (float)page.Signature.Y;
                                        float sigW = (float)page.Signature.Width;
                                        float sigH = (float)page.Signature.Height;

                                        var sigRect = new SKRect(sigX, sigY, sigX + sigW, sigY + sigH);
                                        canvas.DrawBitmap(sigBitmap, sigRect);
                                    }
                                }
                            }
                        }
                        pdfDoc.EndPage();
                    }
                }
                pdfDoc.Close();
            }

            return outputPath;
        }
    }
}