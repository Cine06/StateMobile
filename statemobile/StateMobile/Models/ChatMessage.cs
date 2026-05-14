using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Maui;

namespace StateMobile.Models
{
    public class MessageReaction : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _reactionType = string.Empty;
        public string ReactionType
        {
            get => _reactionType;
            set { if (_reactionType != value) { _reactionType = value; OnPropertyChanged(); } }
        }

        public string UserID { get; set; } = string.Empty;
    }

    public class ChatMessage : INotifyPropertyChanged
    {
        public enum MessageStatus { Sending, Sent, Seen, Failed, Canceled }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private long _messageId;
        public long MessageID
        {
            get => _messageId;
            set { if (_messageId != value) { _messageId = value; OnPropertyChanged(); } }
        }

        private string _senderId = string.Empty;
        public string SenderID
        {
            get => _senderId;
            set { if (_senderId != value) { _senderId = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(IsStatusVisible)); OnPropertyChanged(nameof(HorizontalOptions)); OnPropertyChanged(nameof(BubbleColor)); OnPropertyChanged(nameof(BubbleTextColor)); } }
        }

        private string _messageText = string.Empty;
        public string MessageText
        {
            get => _messageText;
            set 
            { 
                var safeValue = value ?? string.Empty;
                if (_messageText != safeValue) 
                { 
                    _messageText = safeValue; 
                    _cachedDisplayMessageText = null; 
                    _cachedContentText = null; 
                    _imagePreview = null;
                    OnPropertyChanged(); 
                    
                 
                    if (_messageText.StartsWith("[REPLY:"))
                    {
                        try
                        {
                            var firstColon = _messageText.IndexOf(':');
                            var secondColon = _messageText.IndexOf(':', firstColon + 1);
                            var thirdColon = _messageText.IndexOf(':', secondColon + 1);
                            var closingBracket = _messageText.IndexOf(']');

                            if (firstColon > 0 && secondColon > firstColon && thirdColon > secondColon && closingBracket > thirdColon)
                            {
                                var idStr = _messageText.Substring(firstColon + 1, secondColon - firstColon - 1);
                                if (long.TryParse(idStr, out long id)) ParentMessageID = id;

                                ParentSenderName = _messageText.Substring(secondColon + 1, thirdColon - secondColon - 1);
                                ParentMessageText = _messageText.Substring(thirdColon + 1, closingBracket - thirdColon - 1);
                            }
                        }
                        catch { }
                    }

                    OnPropertyChanged(nameof(IsTextMessage)); 
                    OnPropertyChanged(nameof(IsFileMessage)); 
                    OnPropertyChanged(nameof(IsImageMessage)); 
                    OnPropertyChanged(nameof(IsReply));
                    OnPropertyChanged(nameof(IsForwarded));
                    OnPropertyChanged(nameof(ForwardedLabel));
                    OnPropertyChanged(nameof(DisplayFileName)); 
                    OnPropertyChanged(nameof(DisplayMessageText)); 
                    OnPropertyChanged(nameof(ImagePreview)); 
                    OnPropertyChanged(nameof(HasAttachmentData));
                    OnPropertyChanged(nameof(HasImagePreview));
                    OnPropertyChanged(nameof(ParentMessageID));
                    OnPropertyChanged(nameof(ParentMessageText));
                    OnPropertyChanged(nameof(ParentSenderName));
                } 
            }
        }

        public long? ParentMessageID { get; set; }
        public string? ParentMessageText { get; set; }
        public string? ParentSenderName { get; set; }
        public bool IsReply => ParentMessageID != null;

     
        public bool IsForwarded => MessageText?.StartsWith("[FWD]") == true;
        public string ForwardedLabel => SenderID == CurrentUserId
            ? "↗ You forwarded a message"
            : "↗ Forwarded message";

        public DateTime Timestamp { get; set; }

        private string _currentUserId = string.Empty;
        public string CurrentUserId
        {
            get => _currentUserId;
            set { if (_currentUserId != value) { _currentUserId = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsStatusVisible)); OnPropertyChanged(nameof(HorizontalOptions)); OnPropertyChanged(nameof(BubbleColor)); OnPropertyChanged(nameof(BubbleTextColor)); OnPropertyChanged(nameof(IsSent)); } }
        }

        private string _senderNickname = string.Empty;
        public string SenderNickname
        {
            get => _senderNickname;
            set { if (_senderNickname != value) { _senderNickname = value ?? string.Empty; OnPropertyChanged(); } }
        }

        private string _senderPhoto = string.Empty;
        public string SenderPhoto
        {
            get => _senderPhoto;
            set 
            { 
                var safeValue = value ?? string.Empty;
                if (_senderPhoto != safeValue) 
                { 
                    _senderPhoto = safeValue; 
                    _senderImageSource = null; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(SenderImageSource)); 
                } 
            }
        }

        private ImageSource? _senderImageSource;
        public ImageSource? SenderImageSource
        {
            get
            {
                if (_senderImageSource != null) return _senderImageSource;

                if (string.IsNullOrWhiteSpace(SenderPhoto)) 
                {
                    _senderImageSource = "user_avatar_placeholder.png";
                    return _senderImageSource;
                }

                try
                {
                    if (SenderPhoto.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        _senderImageSource = ImageSource.FromUri(new Uri(SenderPhoto));
                    else
                    {
                        var bytes = Convert.FromBase64String(SenderPhoto);
                        _senderImageSource = ImageSource.FromStream(() => new MemoryStream(bytes));
                    }
                }
                catch { _senderImageSource = "user_avatar_placeholder.png"; }
                
                return _senderImageSource;
            }
        }

        private bool _isGroup;
        public bool IsGroup
        {
            get => _isGroup;
            set { if (_isGroup != value) { _isGroup = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIncomingGroupMessage)); } }
        }

        public bool IsIncomingGroupMessage => IsGroup && !string.Equals(SenderID, CurrentUserId, StringComparison.OrdinalIgnoreCase) && SenderID != "SYSTEM";

        public Guid RoomID { get; set; }

        public ObservableCollection<MessageReaction> Reactions { get; } = new();
        public ObservableCollection<string> SeenByUsers { get; } = new();

        public bool HasReactions => Reactions?.Any() == true;
        
        private MessageStatus _status = MessageStatus.Sent;
        public MessageStatus Status
        {
            get => _status;
            set 
            { 
                if (_status != value) 
                { 
                    _status = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(StatusText)); 
                } 
            }
        }

        public string StatusText => Status switch
        {
            MessageStatus.Sending => "sending...",
            MessageStatus.Sent => "sent",
            MessageStatus.Seen => "seen",
            MessageStatus.Failed => "failed",
            MessageStatus.Canceled => "canceled",
            _ => ""
        };

        public bool IsStatusVisible => SenderID == CurrentUserId;

        public record GroupedReaction(string ReactionType, int Count);
        public IEnumerable<GroupedReaction> GroupedReactions => 
            Reactions?
                .GroupBy(r => r.ReactionType)
                .Select(g => new GroupedReaction(g.Key, g.Count())) ?? Enumerable.Empty<GroupedReaction>();

    
        public ChatMessage()
        {
            Reactions.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(GroupedReactions));
                OnPropertyChanged(nameof(HasReactions));

     
                if (SenderID == CurrentUserId && Reactions.Any(r => r.UserID != CurrentUserId))
                {
                    Status = MessageStatus.Seen;
                }
            };

            SeenByUsers.CollectionChanged += (s, e) => {
                if (SenderID == CurrentUserId && SeenByUsers.Any())
                {
                    Status = MessageStatus.Seen;
                }
            };
        }

        public string? FileUrl { get; set; }
        private string _fileName = string.Empty;
        public string? FileName 
        { 
            get => _fileName; 
            set { if (_fileName != value) { _fileName = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayFileName)); } }
        }
        public string? FileData { get; set; } 

        private string? _attachmentDataBase64;
        public string? AttachmentDataBase64
        {
            get => _attachmentDataBase64;
            set
            {
                if (_attachmentDataBase64 != value)
                {
                    _attachmentDataBase64 = value;
                    _imagePreview = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasAttachmentData));
                    OnPropertyChanged(nameof(HasImagePreview));
                    OnPropertyChanged(nameof(ImagePreview));
                }
            }
        }

        public bool HasAttachmentData => !string.IsNullOrWhiteSpace(AttachmentDataBase64);
        public bool HasImagePreview => IsImageMessage && ImagePreview != null;

        private string? _cachedContentText;
      
        public string ContentText
        {
            get
            {
                if (_cachedContentText != null) return _cachedContentText;

                if (MessageText == null) return "";
                string text = MessageText;

                bool changed = true;
                while (changed)
                {
                    changed = false;
            
                    if (text.StartsWith("[REPLY:"))
                    {
                        int firstColon = text.IndexOf(':');
                        int secondColon = text.IndexOf(':', firstColon + 1);
                        int thirdColon = text.IndexOf(':', secondColon + 1);
                        
                        if (thirdColon > 0)
                        {
                            int endBracket = text.IndexOf(']', thirdColon + 1);
                            if (endBracket >= 0)
                            {
                                text = text.Substring(endBracket + 1);
                                changed = true;
                                continue;
                            }
                        }
                        
                        int simpleEnd = text.IndexOf(']');
                        if (simpleEnd >= 0)
                        {
                            text = text.Substring(simpleEnd + 1);
                            changed = true;
                        }
                    }

             
                    if (text.StartsWith("[FWD]"))
                    {
                        text = text.Substring(5);
                        changed = true;
                    }
                }

                _cachedContentText = text;
                return text;
            }
        }

        public bool IsFileMessage => ContentText?.StartsWith("[FILE:") == true;
        public bool IsImageMessage => ContentText?.StartsWith("[IMG:") == true;
        public bool IsTextMessage => !IsFileMessage && !IsImageMessage;

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BorderColor));
                    OnPropertyChanged(nameof(BorderWidth));
                }
            }
        }
        public string DisplayFileName
        {
            get
            {
                var content = ContentText;
                if (IsFileMessage && content != null)
                {
                    var endBracket = content.IndexOf(']');
                    if (endBracket > 6)
                        return content.Substring(6, endBracket - 6);
                }
                if (IsImageMessage && content != null)
                {
                    var endBracket = content.IndexOf(']');
                    if (endBracket > 5)
                        return content.Substring(5, endBracket - 5);
                }

                return FileName ?? "File";
            }
        }

        private string? _cachedDisplayMessageText;
      
        public string DisplayMessageText
        {
            get
            {
                if (_cachedDisplayMessageText != null) return _cachedDisplayMessageText;

                if (MessageText == null) return "";
                string workingText = MessageText;
                
                bool changed = true;
                while (changed)
                {
                    changed = false;
                   
                    if (workingText.StartsWith("[REPLY:"))
                    {
                        int firstColon = workingText.IndexOf(':');
                        int secondColon = workingText.IndexOf(':', firstColon + 1);
                        int thirdColon = workingText.IndexOf(':', secondColon + 1);
                        
                        if (thirdColon > 0)
                        {
                            int endBracket = workingText.IndexOf(']', thirdColon + 1);
                            if (endBracket >= 0)
                            {
                                workingText = workingText.Substring(endBracket + 1);
                                changed = true;
                                continue;
                            }
                        }
                        
                        int simpleEnd = workingText.IndexOf(']');
                        if (simpleEnd >= 0)
                        {
                            workingText = workingText.Substring(simpleEnd + 1);
                            changed = true;
                        }
                    }

         
                    if (workingText.StartsWith("[FWD]"))
                    {
                        workingText = workingText.Substring(5);
                        changed = true;
                    }
                }

            
                if (workingText.StartsWith("[FILE:") || workingText.StartsWith("[IMG:"))
                {
                    var endBracket = workingText.IndexOf(']');
                    if (endBracket >= 0 && endBracket + 1 <= workingText.Length)
                        _cachedDisplayMessageText = workingText.Substring(endBracket + 1);
                    else
                        _cachedDisplayMessageText = "";
                }
                else
                {
                    _cachedDisplayMessageText = workingText;
                }
                
                return _cachedDisplayMessageText;
            }
        }

        private ImageSource? _imagePreview;
        public ImageSource? ImagePreview
        {
            get
            {
                if (_imagePreview != null) return _imagePreview;

                if (IsImageMessage)
                {
                    try
                    {
                        var base64 = !string.IsNullOrWhiteSpace(AttachmentDataBase64)
                            ? AttachmentDataBase64
                            : DisplayMessageText;
                        if (!string.IsNullOrEmpty(base64))
                        {
                            var bytes = Convert.FromBase64String(base64);
                            _imagePreview = ImageSource.FromStream(() => new MemoryStream(bytes));
                        }
                    }
                    catch { }
                }
                return _imagePreview;
            }
        }

        public static bool TryExtractAttachmentPayload(string? messageText, out string attachmentType, out string fileName, out string base64Data, out string previewText)
        {
            attachmentType = string.Empty;
            fileName = string.Empty;
            base64Data = string.Empty;
            previewText = messageText ?? string.Empty;

            if (string.IsNullOrWhiteSpace(messageText))
            {
                return false;
            }

            var workingText = StripSystemPrefixes(messageText);
            if (!(workingText.StartsWith("[FILE:") || workingText.StartsWith("[IMG:") || workingText.StartsWith("[AUDIO:")))
            {
                previewText = workingText;
                return false;
            }

            var closingBracket = workingText.IndexOf(']');
            if (closingBracket <= 0)
            {
                previewText = workingText;
                return false;
            }

            var header = workingText.Substring(1, closingBracket - 1);
            var colonIndex = header.IndexOf(':');
            if (colonIndex <= 0)
            {
                previewText = workingText;
                return false;
            }

            attachmentType = header.Substring(0, colonIndex);
            fileName = header.Substring(colonIndex + 1);
            base64Data = closingBracket + 1 < workingText.Length ? workingText.Substring(closingBracket + 1) : string.Empty;
            previewText = $"[{attachmentType}:{fileName}]";
            return true;
        }

        private static string StripSystemPrefixes(string text)
        {
            var workingText = text;
            var changed = true;

            while (changed)
            {
                changed = false;

                if (workingText.StartsWith("[REPLY:"))
                {
                    var firstColon = workingText.IndexOf(':');
                    var secondColon = workingText.IndexOf(':', firstColon + 1);
                    var thirdColon = workingText.IndexOf(':', secondColon + 1);

                    if (thirdColon > 0)
                    {
                        var endBracket = workingText.IndexOf(']', thirdColon + 1);
                        if (endBracket >= 0)
                        {
                            workingText = workingText.Substring(endBracket + 1);
                            changed = true;
                            continue;
                        }
                    }

                    var simpleEnd = workingText.IndexOf(']');
                    if (simpleEnd >= 0)
                    {
                        workingText = workingText.Substring(simpleEnd + 1);
                        changed = true;
                    }
                }

                if (workingText.StartsWith("[FWD]"))
                {
                    workingText = workingText.Substring(5);
                    changed = true;
                }
            }

            return workingText;
        }

        public LayoutOptions HorizontalOptions =>
            SenderID == "SYSTEM" ? LayoutOptions.Center :
            (string.Equals(SenderID, CurrentUserId, StringComparison.OrdinalIgnoreCase) ? LayoutOptions.End : LayoutOptions.Start);

        public Color BubbleColor =>
        Application.Current?.RequestedTheme == AppTheme.Dark
        ? (string.Equals(SenderID, CurrentUserId, StringComparison.OrdinalIgnoreCase) ? Color.FromArgb("#1E3A3A") : Color.FromArgb("#2A2A3E"))
        : (string.Equals(SenderID, CurrentUserId, StringComparison.OrdinalIgnoreCase) ? Color.FromArgb("#CFE9EA") : Color.FromArgb("#FFFFFF"));

        public Color BubbleTextColor =>
            Application.Current?.RequestedTheme == AppTheme.Dark
                ? Colors.White
                : Color.FromArgb("#1E3A3A");

        public Color BorderColor => 
            IsSelected ? Color.FromArgb("#3B82F6") : 
            (IsHighlighted ? Color.FromArgb("#F59E0B") : Colors.Transparent);

        public double BorderWidth => 
            (IsSelected || IsHighlighted) ? 2 : 0;

        public LayoutOptions ReactionHorizontalOptions =>
            string.Equals(SenderID, CurrentUserId, StringComparison.OrdinalIgnoreCase) ? LayoutOptions.Start : LayoutOptions.End;

        public Thickness ReactionMargin =>
            string.Equals(SenderID, CurrentUserId, StringComparison.OrdinalIgnoreCase) 
                ? new Thickness(-10, 0, 0, -15) 
                : new Thickness(0, 0, -10, -15);

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
                    OnPropertyChanged(nameof(SelectionIndicatorColor));
                    OnPropertyChanged(nameof(BorderColor));
                    OnPropertyChanged(nameof(BorderWidth));
                } 
            }
        }

        public bool IsSent => !string.IsNullOrEmpty(CurrentUserId) && string.Equals(SenderID, CurrentUserId, StringComparison.OrdinalIgnoreCase);
        public int ForwardColumn => IsSent ? 0 : 2;
        public int BubbleColumn => IsSent ? 1 : 1;
        public int AvatarColumn => 0; 
        
        public CornerRadius BubbleCornerRadius => 
            IsSent ? new CornerRadius(16, 16, 4, 16) : new CornerRadius(16, 16, 16, 4);

        public Color SelectionIndicatorColor => IsSelected ? Color.FromArgb("#253B82F6") : Colors.Transparent;
        
        public void NotifyReactionChanged()
        {
            OnPropertyChanged(nameof(GroupedReactions));
            OnPropertyChanged(nameof(HasReactions));
        }

        private bool _isDeleted;
        public bool IsDeleted
        {
            get => _isDeleted;
            set { if (_isDeleted != value) { _isDeleted = value; OnPropertyChanged(); } }
        }

        private bool _showDateHeader;
        public bool ShowDateHeader
        {
            get => _showDateHeader;
            set { if (_showDateHeader != value) { _showDateHeader = value; OnPropertyChanged(); } }
        }

        private string? _dateHeaderText;
        public string? DateHeaderText
        {
            get => _dateHeaderText;
            set { if (_dateHeaderText != value) { _dateHeaderText = value; OnPropertyChanged(); } }
        }
    }
}