namespace StateMobile.Models
{
    public class ScannedPage
    {
        public string ImagePath { get; set; }
        public byte[] SignatureData { get; set; }
        public double SignatureX { get; set; }
        public double SignatureY { get; set; }
        public double SignatureScale { get; set; } = 1.0;
    }

    public class ScannedDocument
    {
        public string DocumentName { get; set; }
        public List<ScannedPage> Pages { get; set; } = new();
    }
}