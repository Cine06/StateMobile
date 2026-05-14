using System.Collections.ObjectModel;

namespace StateMobile.Models
{
    public class ScannedDocument
    {
        public string? Title { get; set; }
        public ObservableCollection<ScannedPage> Pages { get; set; } = new();
    }

    public class ScannedPage
    {
        public int PageNumber { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public PageSignature? Signature { get; set; }
    }

    public class PageSignature
    {
        public byte[]? SignatureData { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 150;
        public double Height { get; set; } = 75;
    }
}