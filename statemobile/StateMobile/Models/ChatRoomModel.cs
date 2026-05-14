using Microsoft.Maui.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.IO;
namespace StateMobile.Models
{
    public class ChatRoomModel : BindableObject
    {
        public Guid RoomID { get; set; }

        private string _roomName = string.Empty;
        public string RoomName
        {
            get => _roomName;
            set
            {
                if (_roomName != value)
                {
                    _roomName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }
        private string _lastMessage = string.Empty;
        public string LastMessage
        {
            get => _lastMessage;
            set
            {
                if (_lastMessage != value)
                {
                    _lastMessage = value;
                    _cachedSubtitle = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplaySubtitle));
                }
            }
        }
        private string _lastMessageSenderId = string.Empty;
        public string LastMessageSenderId
        {
            get => _lastMessageSenderId;
            set
            {
                if (_lastMessageSenderId != value)
                {
                    _lastMessageSenderId = value;
                    _cachedSubtitle = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplaySubtitle));
                }
            }
        }

        private bool _isLastMessageFromCurrentUser;
        public bool IsLastMessageFromCurrentUser
        {
            get => _isLastMessageFromCurrentUser;
            set
            {
                if (_isLastMessageFromCurrentUser != value)
                {
                    _isLastMessageFromCurrentUser = value;
                    _cachedSubtitle = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplaySubtitle));
                }
            }
        }

        private DateTime _lastMessageTime;
        public DateTime LastMessageTime
        {
            get => _lastMessageTime;
            set
            {
                if (_lastMessageTime != value)
                {
                    _lastMessageTime = value;
                    OnPropertyChanged();
                }
            }
        }

        public string OtherUserID { get; set; } = string.Empty;
        public List<string> ParticipantIds { get; set; } = new();
        public Dictionary<string, string> ParticipantNames { get; set; } = new();

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline != value)
                {
                    _isOnline = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        private HashSet<string> _onlineParticipantIds = new();
        public void UpdateParticipantStatus(string userId, bool isOnline)
        {
            if (isOnline)
            {
                if (!_onlineParticipantIds.Contains(userId))
                    _onlineParticipantIds.Add(userId);
            }
            else
            {
                _onlineParticipantIds.Remove(userId);
            }

            if (IsGroup)
            {
                IsOnline = _onlineParticipantIds.Count > 0;
            }
            else
            {
                if (userId == OtherUserID)
                {
                    IsOnline = isOnline;
                }
            }
        }
        public DateTime LastSeen { get; set; }
        public bool IsDeleted { get; set; }
        private string _otherUserPhoto = string.Empty;
        public string OtherUserPhoto
        {
            get => _otherUserPhoto;
            set
            {
                if (_otherUserPhoto != value)
                {
                    _otherUserPhoto = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayPhoto));
                    OnPropertyChanged(nameof(DisplayImageSource));
                }
            }
        }

        public bool IsGroup { get; set; }
        private string _roomPhoto = string.Empty;
        public string RoomPhoto
        {
            get => _roomPhoto;
            set
            {
                if (_roomPhoto != value)
                {
                    _roomPhoto = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayPhoto));
                    OnPropertyChanged(nameof(DisplayImageSource));
                }
            }
        }
        public string DisplayPhoto => IsGroup ? RoomPhoto : OtherUserPhoto;

        public ImageSource? DisplayImageSource
        {
            get
            {
                var photoToShow = DisplayPhoto;
                if (string.IsNullOrWhiteSpace(photoToShow))
                    return null;

                try
                {
                    if (photoToShow.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return ImageSource.FromUri(new Uri(photoToShow));
                    }
                    else
                    {
                        var bytes = Convert.FromBase64String(photoToShow);
                        return ImageSource.FromStream(() => new MemoryStream(bytes));
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private int _unreadCount;
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (_unreadCount != value)
                {
                    _unreadCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasUnread));
                    OnPropertyChanged(nameof(NameFontAttribute));
                    OnPropertyChanged(nameof(NameTextColor));
                    OnPropertyChanged(nameof(MessageFontAttribute));
                    OnPropertyChanged(nameof(MessageTextColor));
                }
            }
        }

        public bool HasUnread => UnreadCount > 0;
        public FontAttributes NameFontAttribute => HasUnread ? FontAttributes.Bold : FontAttributes.None;
        public Color NameTextColor => Application.Current?.RequestedTheme == AppTheme.Dark 
            ? (HasUnread ? Color.FromArgb("#498789") : Color.FromArgb("#FFFFFF"))
            : (HasUnread ? Color.FromArgb("#000000") : Color.FromArgb("#1E3A3A"));

        public FontAttributes MessageFontAttribute => HasUnread ? FontAttributes.Bold : FontAttributes.None;
        public Color MessageTextColor => Application.Current?.RequestedTheme == AppTheme.Dark
            ? (HasUnread ? Color.FromArgb("#A4C2C2") : Color.FromArgb("#888899"))
            : (HasUnread ? Color.FromArgb("#1E3A3A") : Color.FromArgb("#6B7280"));

        public string StatusText => IsOnline ? "Online" : GetLastSeenText();
        public Color StatusColor => IsOnline ? Color.FromArgb("#10B981") : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#888899") : Color.FromArgb("#6B7280"));

        public string DisplayName => !string.IsNullOrWhiteSpace(RoomName) ? RoomName : "Unknown";
        private string? _cachedSubtitle;
        public string DisplaySubtitle
        {
            get
            {
                if (_cachedSubtitle != null) return _cachedSubtitle;
                if (string.IsNullOrEmpty(LastMessage)) return string.Empty;
                
                string contentText = LastMessage;
                bool isForwarded = false;
                bool changed = true;
                
                while (changed)
                {
                    changed = false;
                    
                    if (contentText.StartsWith("[FWD]"))
                    {
                        isForwarded = true;
                        contentText = contentText.Substring(5).TrimStart();
                        changed = true;
                    }
                    
                    if (contentText.StartsWith("[REPLY:"))
                    {
                        int firstColon = contentText.IndexOf(':');
                        int secondColon = contentText.IndexOf(':', firstColon + 1);
                        int thirdColon = contentText.IndexOf(':', secondColon + 1);
                        
                        if (thirdColon > 0)
                        {
                            int endBracket = contentText.IndexOf(']', thirdColon + 1);
                            if (endBracket >= 0)
                            {
                                contentText = contentText.Substring(endBracket + 1);
                                changed = true;
                            }
                        }
                        else
                        {
                            int simpleEnd = contentText.IndexOf(']');
                            if (simpleEnd >= 0)
                            {
                                contentText = contentText.Substring(simpleEnd + 1);
                                changed = true;
                            }
                        }
                    }
                }

                if (isForwarded)
                {
                    if (!IsLastMessageFromCurrentUser)
                    {
                        _cachedSubtitle = "Forwarded a message";
                        return _cachedSubtitle;
                    }
                }

                string displayMsg = contentText;
                if (contentText.StartsWith("[FILE:"))
                {
                    var endBracket = contentText.IndexOf(']');
                    if (endBracket > 6)
                        displayMsg = "📎 " + contentText.Substring(6, endBracket - 6);
                    else
                        displayMsg = "📎 File";
                }
                else if (contentText.StartsWith("[IMG:"))
                {
                    var endBracket = contentText.IndexOf(']');
                    if (endBracket > 5)
                        displayMsg = "📷 " + contentText.Substring(5, endBracket - 5);
                    else
                        displayMsg = "📷 Photo";
                }

                if (isForwarded && IsLastMessageFromCurrentUser)
                {
                    displayMsg = "You: " + displayMsg;
                }

                _cachedSubtitle = displayMsg;
                return _cachedSubtitle;
            }
        }

        private string GetLastSeenText()
        {
            if (LastSeen == DateTime.MinValue) return "Offline";

            var timeAgo = DateTime.Now - LastSeen;

            if (timeAgo.TotalMinutes < 1) return "Just now";
            if (timeAgo.TotalMinutes < 60) return $"{(int)timeAgo.TotalMinutes}m ago";
            if (timeAgo.TotalHours < 24) return $"{(int)timeAgo.TotalHours}h ago";
            if (timeAgo.TotalDays < 7) return $"{(int)timeAgo.TotalDays}d ago";

            return LastSeen.ToString("MMM d");
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
    }
}