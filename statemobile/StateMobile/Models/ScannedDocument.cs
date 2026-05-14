using System.Collections.Generic;

namespace StateMobile.Models
{
    public class ScannedDocument
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public List<ScannedPage> Pages { get; set; } = new();
        public string Title { get; set; }
    }

    public class ScannedPage
    {
        public string ImagePath { get; set; }
        public SignaturePlacement Signature { get; set; }
    }

    public class SignaturePlacement
    {
        public byte[] SignatureData { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}