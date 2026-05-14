using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StateMobile.Models
{
    public class PagePreviewItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public PagePreviewItem(int pageIndex, byte[] pageBytes)
        {
            PageIndex = pageIndex;
            PageBytes = pageBytes;
            Signatures = new ObservableCollection<SignaturePlacement>();
        }

        public int PageIndex { get; }

        private int _totalPages;
        public int TotalPages
        {
            get => _totalPages;
            set
            {
                if (_totalPages == value)
                {
                    return;
                }

                _totalPages = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PageIndicatorText));
            }
        }

        public string PageIndicatorText => $"Page {PageIndex + 1} of {Math.Max(1, TotalPages)}";

        public byte[] PageBytes { get; }

        public ObservableCollection<SignaturePlacement> Signatures { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}