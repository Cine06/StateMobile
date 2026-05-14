namespace StateMobile.API.Models
{
    public class ProjectModel
    {
        public int WorkType { get; set; }
        public string CtrlNo { get; set; } = string.Empty;
        public string Particulars { get; set; } = string.Empty;
        public string ProjName { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public string AssignedEngineerCode { get; set; } = string.Empty;
        public string AssignedEngineersOICNames { get; set; } = string.Empty;
        public string AssignedEngineersOIC { get; set; } = string.Empty; // Added
        public string GC { get; set; } = string.Empty;
        public decimal PercentageCompletion { get; set; }
        public DateTime? AwardDate { get; set; }
        public DateTime? TargetEndDate { get; set; }
        public DateTime? PrepDate { get; set; }
        public DateTime? TargetStartDate { get; set; }
        public DateTime? ActualStartDate { get; set; }
        public DateTime? ActualDateCompletion { get; set; }
        public string AssignedEngineers { get; set; } = string.Empty;
        public int ModelCode { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public string? CoverPhotoUrl { get; set; }
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
