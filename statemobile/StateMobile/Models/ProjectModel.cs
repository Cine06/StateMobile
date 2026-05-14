namespace StateMobile.Models
{
    public class ProjectModel : System.ComponentModel.INotifyPropertyChanged
    {
        public int WorkType { get; set; }
        public string CtrlNo { get; set; } = string.Empty;
        public string Particulars { get; set; } = string.Empty;
        public string ProjName { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public string AssignedEngineerCode { get; set; } = string.Empty;
        public string AssignedEngineersOICNames { get; set; } = string.Empty;
        public string AssignedEngineersOIC { get; set; } = string.Empty; 
        public string GC { get; set; } = string.Empty;
        public decimal PercentageCompletion { get; set; }
        public DateTime? AwardDate { get; set; }
        public DateTime? TargetEndDate { get; set; }
        public DateTime? PrepDate { get; set; }
        public DateTime? TargetStartDate { get; set; }
        public DateTime? ActualStartDate { get; set; }
        public DateTime? ActualDateCompletion { get; set; }
        public string AssignedEngineers { get; set; } = string.Empty;

        private bool _hasOfflineEntries;
        public bool HasOfflineEntries
        {
            get => _hasOfflineEntries;
            set
            {
                if (_hasOfflineEntries != value)
                {
                    _hasOfflineEntries = value;
                    OnPropertyChanged(nameof(HasOfflineEntries));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

        public int ModelCode { get; set; }
        public string ModelName { get; set; } = string.Empty;

        public string AwardDateFormatted => AwardDate.HasValue ? AwardDate.Value.ToString("MM/dd/yyyy") : "---";
        public string TargetEndDateFormatted => TargetEndDate.HasValue ? TargetEndDate.Value.ToString("MM/dd/yyyy") : "---";
        public string TargetStartDateFormatted => TargetStartDate.HasValue ? TargetStartDate.Value.ToString("MM/dd/yyyy") : "---";
        public string ActualStartDateFormatted => ActualStartDate.HasValue ? ActualStartDate.Value.ToString("MM/dd/yyyy") : "---";
        public string ActualDateCompletionFormatted => ActualDateCompletion.HasValue ? ActualDateCompletion.Value.ToString("MM/dd/yyyy") : "---";
        public string ProgressPercentText => $"{PercentageCompletion:N2}%";

        public string? CoverPhotoUrl { get; set; }
        public List<string> EngineerThumbnailUrls { get; set; } = new List<string>();
        public List<string> EngineerCodes => string.IsNullOrEmpty(AssignedEngineersOIC) 
            ? new List<string>() 
            : AssignedEngineersOIC.Split(',').Select(c => c.Trim()).ToList();
    }

    public class WorkStatusModel
    {
        public int StatusCode { get; set; }
        public string StatusText { get; set; } = string.Empty;
    }

    public class ProjectEngineerModel
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class HouseModelFilterModel
    {
        public int Code { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
