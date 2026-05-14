using System;

namespace StateMobile.API.Models
{
    public class ProjectDiaryModel
    {
        public int Id { get; set; }
        public string ControlNo { get; set; } = string.Empty;
        public DateTime DiaryDate { get; set; } = DateTime.Now;
        public string DiaryDateFormatted { get; set; } = string.Empty;
        public int DiaryWeather { get; set; } // 0: Not Workable, 1: Workable
        public string DiaryWeatherRemarks { get; set; } = string.Empty;
        public string Manpower { get; set; } = "0";
        public string DiaryActivities { get; set; } = string.Empty;
        public string AuditUser { get; set; } = string.Empty;
        public string AuditDateFormatted { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
    }

    public class ProjectDiaryFileModel
    {
        public int Id { get; set; }
        public int DiaryID { get; set; }
        public string ControlNo { get; set; } = string.Empty;
        public string StreamID { get; set; } = string.Empty;
        public string DiaryDateFormatted { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileContentType { get; set; } = string.Empty;
        public string FileDescription { get; set; } = string.Empty;
        public string FileContentBase64 { get; set; } = string.Empty;
        public string AuditUser { get; set; } = string.Empty;
        public string AuditDateFormatted { get; set; } = string.Empty;
    }

    public class SaveDiaryRequest
    {
        public string ControlNo { get; set; } = string.Empty;
        public int DiaryEntryID { get; set; }
        public string DiaryDate { get; set; } = string.Empty;
        public int DiaryWeather { get; set; }
        public string DiaryWeatherRemarks { get; set; } = string.Empty;
        public string Manpower { get; set; } = "0";
        public string DiaryActivities { get; set; } = string.Empty;
        public string AuditUser { get; set; } = string.Empty;
    }

    public class SaveDiaryFileRequest
    {
        public string ControlNo { get; set; } = string.Empty;
        public int DiaryEntryID { get; set; }
        public string DiaryDate { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileContentType { get; set; } = string.Empty;
        public string FileContentBase64 { get; set; } = string.Empty;
        public string FileDescription { get; set; } = string.Empty;
        public string AuditUser { get; set; } = string.Empty;
    }
}
