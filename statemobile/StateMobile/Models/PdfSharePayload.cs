namespace StateMobile.Models
{
    public sealed class PdfSharePayload
    {
        public PdfSharePayload(byte[] pdfBytes, string fileName)
        {
            PdfBytes = pdfBytes;
            FileName = fileName;
        }

        public byte[] PdfBytes { get; }
        public string FileName { get; }
    }
}