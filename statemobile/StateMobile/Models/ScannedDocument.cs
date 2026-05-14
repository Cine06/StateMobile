using Microsoft.Maui.Graphics;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StateMobile.Models
{
    public class ScannedDocument : INotifyPropertyChanged
    {
        private List<byte[]> _pages = new();
        public List<byte[]> Pages 
        { 
            get => _pages; 
            set { _pages = value; OnPropertyChanged(); } 
        }

        private byte[]? _signature;
        public byte[]? Signature 
        { 
            get => _signature; 
            set { _signature = value; OnPropertyChanged(); } 
        }

        private byte[]? _signaturePreview;
        public byte[]? SignaturePreview
        {
            get => _signaturePreview;
            set { _signaturePreview = value; OnPropertyChanged(); }
        }

        private int _signaturePageIndex = 0;
        public int SignaturePageIndex 
        { 
            get => _signaturePageIndex; 
            set { _signaturePageIndex = value; OnPropertyChanged(); } 
        }

        private float _signatureX = 50;
        public float SignatureX 
        { 
            get => _signatureX; 
            set { _signatureX = value; OnPropertyChanged(); } 
        }

        private float _signatureY = 50;
        public float SignatureY 
        { 
            get => _signatureY; 
            set { _signatureY = value; OnPropertyChanged(); } 
        }

        private float _signatureScale = 1.0f;
        public float SignatureScale 
        { 
            get => _signatureScale; 
            set { _signatureScale = value; OnPropertyChanged(); } 
        }

        private double _signatureWidth = 150;
        public double SignatureWidth
        {
            get => _signatureWidth;
            set { _signatureWidth = value; OnPropertyChanged(); }
        }

        private double _signatureHeight = 100;
        public double SignatureHeight
        {
            get => _signatureHeight;
            set { _signatureHeight = value; OnPropertyChanged(); }
        }

        private byte[]? _finalPdf;
        public byte[]? FinalPdf 
        { 
            get => _finalPdf; 
            set { _finalPdf = value; OnPropertyChanged(); } 
        }

        private string _fileName = $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        public string FileName 
        { 
            get => _fileName; 
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? _fileName : value.Trim();

                if (!normalized.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    normalized += ".pdf";
                }

                _fileName = normalized;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
