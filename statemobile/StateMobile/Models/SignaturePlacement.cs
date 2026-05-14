using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Graphics;

namespace StateMobile.Models
{
    public class SignaturePlacement : INotifyPropertyChanged
    {
        private float _x;
        private float _y;
        private double _width;
        private double _height;
        private byte[]? _rawBytes;
        private byte[]? _previewBytes;
        private bool _isLocked;
        private bool _isEditing;

        public int PageIndex { get; set; }

        public float X
        {
            get => _x;
            set
            {
                if (Math.Abs(_x - value) < 0.01f)
                {
                    return;
                }

                _x = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Bounds));
            }
        }

        public float Y
        {
            get => _y;
            set
            {
                if (Math.Abs(_y - value) < 0.01f)
                {
                    return;
                }

                _y = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Bounds));
            }
        }

       
        public void BatchMove(float newX, float newY)
        {
            var xChanged = Math.Abs(_x - newX) >= 0.01f;
            var yChanged = Math.Abs(_y - newY) >= 0.01f;

            if (!xChanged && !yChanged)
            {
                return;
            }

            _x = newX;
            _y = newY;

            OnPropertyChanged(nameof(X));
            OnPropertyChanged(nameof(Y));
            OnPropertyChanged(nameof(Bounds));
        }

      
        public void BatchResize(double newWidth, double newHeight)
        {
            var wChanged = Math.Abs(_width - newWidth) >= 0.01;
            var hChanged = Math.Abs(_height - newHeight) >= 0.01;

            if (!wChanged && !hChanged)
            {
                return;
            }

            if (wChanged)
            {
                _width = newWidth;
                OnPropertyChanged(nameof(Width));
            }

            if (hChanged)
            {
                _height = newHeight;
                OnPropertyChanged(nameof(Height));
            }

            OnPropertyChanged(nameof(Bounds));
        }

        public double Width
        {
            get => _width;
            set
            {
                if (Math.Abs(_width - value) < 0.01)
                {
                    return;
                }

                _width = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Bounds));
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                if (Math.Abs(_height - value) < 0.01)
                {
                    return;
                }

                _height = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Bounds));
            }
        }

        public byte[]? RawBytes
        {
            get => _rawBytes;
            set
            {
                _rawBytes = value;
                OnPropertyChanged();
            }
        }

        public byte[]? PreviewBytes
        {
            get => _previewBytes;
            set
            {
                _previewBytes = value;
                OnPropertyChanged();
            }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value)
                {
                    return;
                }

                _isLocked = value;
                OnPropertyChanged();
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value)
                {
                    return;
                }

                _isEditing = value;
                OnPropertyChanged();
            }
        }

        public Rect Bounds => new(X, Y, Width, Height);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}