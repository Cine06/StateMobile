using SkiaSharp;
using StateMobile.Models;

namespace StateMobile.Services
{
    public class PdfService : IPdfService
    {
        public async Task<byte[]> CreatePdfFromImagesAsync(List<byte[]> images)
        {
            return await Task.Run(() =>
            {
                using var stream = new SKDynamicMemoryWStream();
                using var document = SKDocument.CreatePdf(stream);

                float? targetPageWidth = null;
                float? targetPageHeight = null;

                foreach (var imgBytes in images)
                {
                    using var codec = SKCodec.Create(new MemoryStream(imgBytes));
                    if (codec == null) continue;

                    var origin = codec.EncodedOrigin;
                    using var bitmap = SKBitmap.Decode(imgBytes);
                    if (bitmap == null) continue;

                    using var oriented = OrientBitmap(bitmap, origin);
                    if (oriented == null) continue;

                    targetPageWidth ??= oriented.Width;
                    targetPageHeight ??= oriented.Height;

                    var pageWidth = targetPageWidth.Value;
                    var pageHeight = targetPageHeight.Value;
                    using var canvas = document.BeginPage(pageWidth, pageHeight);
                    if (canvas == null) continue;
                    canvas.Clear(SKColors.White);

                    var fitScale = Math.Min(pageWidth / oriented.Width, pageHeight / oriented.Height);
                    var drawWidth = oriented.Width * fitScale;
                    var drawHeight = oriented.Height * fitScale;
                    var drawX = (pageWidth - drawWidth) / 2f;
                    var drawY = (pageHeight - drawHeight) / 2f;

                    using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
                    canvas.DrawBitmap(oriented, new SKRect(drawX, drawY, drawX + drawWidth, drawY + drawHeight), paint);
                    document.EndPage();
                }

                document.Close();
                return stream.DetachAsData().ToArray();
            });
        }

        private static SKBitmap OrientBitmap(SKBitmap bitmap, SKEncodedOrigin origin)
        {
            if (origin == SKEncodedOrigin.TopLeft || origin == SKEncodedOrigin.Default)
            {
                return bitmap.Copy() ?? new SKBitmap(bitmap.Info);
            }

            int width = bitmap.Width;
            int height = bitmap.Height;
            bool swap = origin == SKEncodedOrigin.RightTop || origin == SKEncodedOrigin.LeftBottom ||
                        origin == SKEncodedOrigin.LeftTop || origin == SKEncodedOrigin.RightBottom;

            var rotatedInfo = new SKImageInfo(
                swap ? height : width,
                swap ? width : height,
                bitmap.Info.ColorType,
                bitmap.Info.AlphaType,
                bitmap.Info.ColorSpace);
            var rotated = new SKBitmap(rotatedInfo);

            using (var canvas = new SKCanvas(rotated))
            {
                canvas.Clear(SKColors.White);
                canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);

                switch (origin)
                {
                    case SKEncodedOrigin.TopRight:
                        canvas.Scale(-1, 1);
                        break;
                    case SKEncodedOrigin.BottomRight:
                        canvas.RotateDegrees(180);
                        break;
                    case SKEncodedOrigin.BottomLeft:
                        canvas.Scale(1, -1);
                        break;
                    case SKEncodedOrigin.LeftTop:
                        canvas.RotateDegrees(90);
                        canvas.Scale(-1, 1);
                        break;
                    case SKEncodedOrigin.RightTop:
                        canvas.RotateDegrees(90);
                        break;
                    case SKEncodedOrigin.RightBottom:
                        canvas.RotateDegrees(270);
                        canvas.Scale(-1, 1);
                        break;
                    case SKEncodedOrigin.LeftBottom:
                        canvas.RotateDegrees(270);
                        break;
                }

                canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
                canvas.DrawBitmap(bitmap, 0, 0);
            }

            return rotated;
        }

        public Task<byte[]> AddSignatureToPdfAsync(byte[] pdfBytes, byte[] signatureImage, int pageIndex, double x, double y, double scale)
        {
   
            return Task.FromResult(pdfBytes); 
        }
    }
}
