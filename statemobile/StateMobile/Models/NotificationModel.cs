using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.Maui;

namespace StateMobile.Models
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
        private DateTime? _dateRead;
        public DateTime? DateRead
        {
            get => _dateRead;
            set
            {
                if (_dateRead != value)
                {
                    _dateRead = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsUnread));
                    OnPropertyChanged(nameof(UnreadFontWeight));
                    OnPropertyChanged(nameof(UnreadDotVisible));
                    OnPropertyChanged(nameof(UnreadDotColor));
                    OnPropertyChanged(nameof(ReadStatusText));
                }
            }
        }

        private int _done;
        public int Done
        {
            get => _done;
            set
            {
                if (_done != value)
                {
                    _done = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsUnread));
                    OnPropertyChanged(nameof(UnreadDotVisible));
                    OnPropertyChanged(nameof(UnreadDotColor));
                    OnPropertyChanged(nameof(ReadStatusText));
                }
            }
        }

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

        public string Title => ModuleName ?? $"Module {ModuleCode}";
        public string Initial => !string.IsNullOrEmpty(Title) ? Title[0].ToString().ToUpper() : "?";
        public string IconColor => GetIconColor();
        public string StarChar => IsStarred ? "★" : "☆";
        public string SenderSubtitle => RequestBy;
        public bool SenderSubtitleVisible => !string.IsNullOrEmpty(SenderSubtitle);
        public FontAttributes UnreadFontWeight => IsUnread ? FontAttributes.Bold : FontAttributes.None;


        public bool UnreadDotVisible => IsUnread;
        public string UnreadDotColor => IsUnread ? "#1A73E8" : "Transparent";
        public string ReadStatusText => IsUnread ? "Unread" : $"Read {DateRead?.ToString("MMM d, h:mm tt")}";

        private bool _isStarred;
        public bool IsStarred
        {
            get => _isStarred;
            set
            {
                if (_isStarred != value)
                {
                    _isStarred = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StarChar));
                }
            }
        }

        public bool IsUnread => DateRead == null;
        public string TimeAgo => CalculateTimeAgo();
        public string MessagePreview => Message?.Length > 60 ? Message.Substring(0, 57) + "..." : Message;

        private bool _isExpanded;
        
        [JsonIgnore]
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayMessage));
                    OnPropertyChanged(nameof(MessageMaxLines));
                    OnPropertyChanged(nameof(MessageLineBreakMode));
                }
            }
        }

        [JsonIgnore]
        public string DisplayMessage => IsExpanded ? Message : MessagePreview;
        
        [JsonIgnore]
        public int MessageMaxLines => IsExpanded ? 100 : 1;
        
        [JsonIgnore]
        public LineBreakMode MessageLineBreakMode => IsExpanded ? LineBreakMode.WordWrap : LineBreakMode.TailTruncation;


        private string GetIconColor()
        {
            if (string.IsNullOrEmpty(ModuleName)) return "#9AA0A6"; // Gray


            string[] colors = { "#4285F4", "#DB4437", "#F4B400", "#0F9D58" };
   
            int hash = 0;
            foreach (char c in ModuleName)
            {
                hash = hash * 31 + c;
            }
            return colors[Math.Abs(hash) % colors.Length];
        }

        private string CalculateTimeAgo()
        {
            var timeSpan = DateTime.Now - Date;

            if (timeSpan.TotalMinutes < 1) return "Just now";
            if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes}m";
            if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours}h";


            return Date.ToString("d MMM");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

 