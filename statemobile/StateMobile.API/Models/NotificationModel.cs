using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StateMobile.API.Models
{
    public class NotificationModel : INotifyPropertyChanged
    {
        public long Code { get; set; }
        public DateTime Date { get; set; }
        public string AISNo { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string RequestBy { get; set; } = string.Empty;
        public string ForApproval { get; set; } = string.Empty;
        public string WidgetName { get; set; } = string.Empty;
        public int ModuleCode { get; set; }
        public string InternetURL { get; set; } = string.Empty;
        public string LocalURL { get; set; } = string.Empty;
        public string URL { get; set; } = string.Empty;
        public string ControlNo { get; set; } = string.Empty;
        public DateTime? DateRead { get; set; }
        public int Done { get; set; }

        // --- UI Helper Properties (for API testing/preview) ---
        public string Title => ModuleName ?? $"Module {ModuleCode}";
        public bool IsUnread => DateRead == null;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}