using System.Text.Json.Serialization;

namespace StateMobile.Models
{
    public class ProjectDiaryPhotoModel : System.ComponentModel.INotifyPropertyChanged
    {
        public int Id { get; set; }
        public int DiaryID { get; set; }
        public string StreamID { get; set; } = string.Empty;
        public string PhotoUrl { get; set; }
        public string PhotoDescription { get; set; } = string.Empty;

        [JsonPropertyName("fileDescription")]
        public string FileDescription 
        { 
            get => PhotoDescription; 
            set => PhotoDescription = value ?? string.Empty; 
        }
        
        public string FileName { get; set; } = string.Empty;
        public string FileContentType { get; set; } = string.Empty;
        public string FileContentBase64 { get; set; } = string.Empty;
        public string AuditUser { get; set; } = string.Empty;
        public string AuditDateFormatted { get; set; } = string.Empty;
        public string DiaryDateFormatted { get; set; } = string.Empty;

        private bool _isMenuVisible;
        [JsonIgnore]
        public bool IsMenuVisible
        {
            get => _isMenuVisible;
            set
            {
                if (_isMenuVisible != value)
                {
                    _isMenuVisible = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsMenuVisible)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public class ProjectDiaryModel : System.ComponentModel.INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string ControlNo { get; set; } = string.Empty;
        public DateTime DiaryDate { get; set; } = DateTime.Now;
        public string DiaryDateFormatted { get; set; } = string.Empty;
        public int DiaryWeather { get; set; } 
        public string DiaryWeatherRemarks { get; set; } = string.Empty;
        public string Manpower { get; set; } = "0";
        public string DiaryActivities { get; set; } = string.Empty;
        public string AuditUser { get; set; } = string.Empty;
        public string AuditDateFormatted { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty; // For mobile entry (Main Photo if needed)
        public string PhotoDescription { get; set; } = string.Empty;
        public System.Collections.Generic.List<ProjectDiaryPhotoModel> Photos { get; set; } = new System.Collections.Generic.List<ProjectDiaryPhotoModel>();
        public bool IsOffline { get; set; }
        public string StatusColor => DiaryWeather == 1 ? "#498789" : "#E74C3C";
        public string StatusText => DiaryWeather == 1 ? "Workable" : "Not Workable";

        private bool _isMenuVisible;
        [JsonIgnore]
        public bool IsMenuVisible
        {
            get => _isMenuVisible;
            set
            {
                if (_isMenuVisible != value)
                {
                    _isMenuVisible = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsMenuVisible)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }
}
