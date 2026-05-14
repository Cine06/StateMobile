using System.ComponentModel;

namespace StateMobile.Models
{
    public class User : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string? UserID { get; set; }
        public string? AISNo { get; set; } 
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? DepartmentName { get; set; }
        public string? Photo { get; set; }
        public string? Nickname { get; set; }
        public string? Mobile { get; set; }

        public string FullName
        {
            get
            {
                var parts = new[] { FirstName, LastName };
                return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
            }
        }

        public string DisplayName => FullName;
        public string DisplaySubtitle => DepartmentName ?? string.Empty;
        public Color? StatusColor => null; 

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
