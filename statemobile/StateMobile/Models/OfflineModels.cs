using SQLite;

namespace StateMobile.Models
{

    [Table("CachedProjects")]
    public class CachedProject
    {
        [PrimaryKey]
        public string CtrlNo { get; set; } = string.Empty;
        public int WorkType { get; set; }
        public string Particulars { get; set; } = string.Empty;
        public string ProjName { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public string AssignedEngineerCode { get; set; } = string.Empty;
        public string AssignedEngineersOICNames { get; set; } = string.Empty;
        public string AssignedEngineersOIC { get; set; } = string.Empty;
        public string GC { get; set; } = string.Empty;
        public decimal PercentageCompletion { get; set; }
        public string AwardDate { get; set; } = string.Empty;
        public string TargetEndDate { get; set; } = string.Empty;
        public string PrepDate { get; set; } = string.Empty;
        public string TargetStartDate { get; set; } = string.Empty;
        public string ActualStartDate { get; set; } = string.Empty;
        public string ActualDateCompletion { get; set; } = string.Empty;
        public string AssignedEngineers { get; set; } = string.Empty;
        public int ModelCode { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public string CoverPhotoUrl { get; set; } = string.Empty;
        public string EngineerThumbnailUrlsJson { get; set; } = string.Empty;
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;

        public ProjectModel ToProjectModel()
        {
            return new ProjectModel
            {
                CtrlNo = CtrlNo,
                WorkType = WorkType,
                Particulars = Particulars,
                ProjName = ProjName,
                StatusCode = StatusCode,
                StatusText = StatusText,
                AssignedEngineerCode = AssignedEngineerCode,
                AssignedEngineersOICNames = AssignedEngineersOICNames,
                AssignedEngineersOIC = AssignedEngineersOIC,
                GC = GC,
                PercentageCompletion = PercentageCompletion,
                AwardDate = ParseDate(AwardDate),
                TargetEndDate = ParseDate(TargetEndDate),
                PrepDate = ParseDate(PrepDate),
                TargetStartDate = ParseDate(TargetStartDate),
                ActualStartDate = ParseDate(ActualStartDate),
                ActualDateCompletion = ParseDate(ActualDateCompletion),
                AssignedEngineers = AssignedEngineers,
                ModelCode = ModelCode,
                ModelName = ModelName,
                CoverPhotoUrl = CoverPhotoUrl,
                EngineerThumbnailUrls = DeserializeList(EngineerThumbnailUrlsJson)
            };
        }

        public static CachedProject FromProjectModel(ProjectModel p)
        {
            return new CachedProject
            {
                CtrlNo = p.CtrlNo,
                WorkType = p.WorkType,
                Particulars = p.Particulars,
                ProjName = p.ProjName,
                StatusCode = p.StatusCode,
                StatusText = p.StatusText,
                AssignedEngineerCode = p.AssignedEngineerCode,
                AssignedEngineersOICNames = p.AssignedEngineersOICNames,
                AssignedEngineersOIC = p.AssignedEngineersOIC,
                GC = p.GC,
                PercentageCompletion = p.PercentageCompletion,
                AwardDate = p.AwardDate?.ToString("o") ?? "",
                TargetEndDate = p.TargetEndDate?.ToString("o") ?? "",
                PrepDate = p.PrepDate?.ToString("o") ?? "",
                TargetStartDate = p.TargetStartDate?.ToString("o") ?? "",
                ActualStartDate = p.ActualStartDate?.ToString("o") ?? "",
                ActualDateCompletion = p.ActualDateCompletion?.ToString("o") ?? "",
                AssignedEngineers = p.AssignedEngineers,
                ModelCode = p.ModelCode,
                ModelName = p.ModelName,
                CoverPhotoUrl = p.CoverPhotoUrl ?? "",
                EngineerThumbnailUrlsJson = SerializeList(p.EngineerThumbnailUrls),
                CachedAt = DateTime.UtcNow
            };
        }

        private static DateTime? ParseDate(string s) =>
            string.IsNullOrEmpty(s) ? null : DateTime.TryParse(s, out var d) ? d : null;

        private static string SerializeList(List<string> list) =>
            list == null || list.Count == 0 ? "[]" : System.Text.Json.JsonSerializer.Serialize(list);

        private static List<string> DeserializeList(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "[]") return new List<string>();
            try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }
    }

    // ─── Cached Status List ───
    [Table("CachedWorkStatuses")]
    public class CachedWorkStatus
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int StatusCode { get; set; }
        public string StatusText { get; set; } = string.Empty;

        public WorkStatusModel ToModel() => new WorkStatusModel { StatusCode = StatusCode, StatusText = StatusText };
        public static CachedWorkStatus FromModel(WorkStatusModel m) => new CachedWorkStatus { StatusCode = m.StatusCode, StatusText = m.StatusText };
    }

    // ─── Cached Engineer List ───
    [Table("CachedProjectEngineers")]
    public class CachedProjectEngineer
    {
        [PrimaryKey]
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public ProjectEngineerModel ToModel() => new ProjectEngineerModel { Code = Code, Name = Name };
        public static CachedProjectEngineer FromModel(ProjectEngineerModel m) => new CachedProjectEngineer { Code = m.Code, Name = m.Name };
    }

    // ─── Offline Sync Queue (for diary entries encoded while offline) ───
    [Table("OfflineSyncQueue")]
    public class OfflineSyncQueueItem
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>SaveDiary, DeleteDiary, SaveDiaryFile, DeleteDiaryFile</summary>
        public string OperationType { get; set; } = string.Empty;

        /// <summary>JSON payload of the request parameters</summary>
        public string PayloadJson { get; set; } = string.Empty;

        /// <summary>Control number of the project this belongs to</summary>
        public string ControlNo { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsSynced { get; set; }
        public string? SyncError { get; set; }

        /// <summary>
        /// Temporary local ID for diary entries created offline.
        /// Used to link file uploads to the correct entry after sync assigns a real server ID.
        /// </summary>
        public int LocalTempId { get; set; }
    }

    // ─── Offline Diary Entry (local-only, pending sync) ───
    [Table("OfflineDiaryEntries")]
    public class OfflineDiaryEntry : System.ComponentModel.INotifyPropertyChanged
    {
        [PrimaryKey, AutoIncrement]
        public int LocalId { get; set; }
        public string ControlNo { get; set; } = string.Empty;
        public string DiaryDate { get; set; } = string.Empty;
        public int DiaryWeather { get; set; }
        public string WeatherRemarks { get; set; } = string.Empty;
        public string Manpower { get; set; } = "0";
        public string Activities { get; set; } = string.Empty;
        public string AuditUser { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsSynced { get; set; }
        public int? ServerDiaryId { get; set; } // Populated after successful sync

        [Ignore]
        public string Particular { get; set; } = string.Empty;

        [Ignore]
        public List<string> LocalPhotoPaths { get; set; } = new();

        private bool _isSelected = true;
        [Ignore]
        public bool IsSelected 
        { 
            get => _isSelected; 
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } } 
        }

        private string? _debugInfo;
        [Ignore]
        public string? DebugInfo 
        { 
            get => _debugInfo; 
            set { if (_debugInfo != value) { _debugInfo = value; OnPropertyChanged(); } } 
        }

        private string? _previewPhotoPath;
        [Ignore]
        public string? PreviewPhotoPath
        {
            get => _previewPhotoPath;
            set { if (_previewPhotoPath != value) { _previewPhotoPath = value; OnPropertyChanged(); } }
        }

        private bool _hasPhotos;
        [Ignore]
        public bool HasPhotos
        {
            get => _hasPhotos;
            set { if (_hasPhotos != value) { _hasPhotos = value; OnPropertyChanged(); } }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    // ─── Offline Diary File (local-only, pending sync) ───
    [Table("OfflineDiaryFiles")]
    public class OfflineDiaryFile : System.ComponentModel.INotifyPropertyChanged
    {
        [PrimaryKey, AutoIncrement]
        public int LocalId { get; set; }
        public int ParentLocalDiaryId { get; set; } // Links to OfflineDiaryEntry.LocalId
        public string ControlNo { get; set; } = string.Empty;
        public string DiaryDate { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty; // Local file path on device
        public string Description { get; set; } = string.Empty;
        public string AuditUser { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsSynced { get; set; }

        [Ignore]
        public string Particular { get; set; } = string.Empty;

        private bool _isSelected = true;
        [Ignore]
        public bool IsSelected 
        { 
            get => _isSelected; 
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } } 
        }

        private ImageSource? _fileSource;
        [Ignore]
        public ImageSource? FileSource 
        { 
            get => _fileSource; 
            set { if (_fileSource != value) { _fileSource = value; OnPropertyChanged(); } } 
        }

        private string? _debugInfo;
        [Ignore]
        public string? DebugInfo 
        { 
            get => _debugInfo; 
            set { if (_debugInfo != value) { _debugInfo = value; OnPropertyChanged(); } } 
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    // ─── Cached User (for offline login) ───
    [Table("CachedUsers")]
    public class CachedUser
    {
        [PrimaryKey]
        public string Username { get; set; } = string.Empty; // Store lowercase for case-insensitive lookup
        public string PasswordHash { get; set; } = string.Empty;
        public string UserJson { get; set; } = string.Empty;
        public DateTime LastLogin { get; set; } = DateTime.UtcNow;

        public User ToUser()
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<User>(UserJson) ?? new User(); }
            catch { return new User(); }
        }

        public static string SerializeUser(User user) => 
            System.Text.Json.JsonSerializer.Serialize(user);
    }

    // ─── Cached Chat Room (for offline chat list) ───
    [Table("CachedChatRooms")]
    public class CachedChatRoom
    {
        [PrimaryKey]
        public string RoomID { get; set; } = string.Empty;
        public string RoomName { get; set; } = string.Empty;
        public bool IsGroup { get; set; }
        public bool IsDeleted { get; set; }
        public string RoomPhoto { get; set; } = string.Empty;
        public string OtherUserID { get; set; } = string.Empty;
        public string OtherUserPhoto { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public string LastSeen { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        public string LastMessageSenderId { get; set; } = string.Empty;
        public string LastMessageTime { get; set; } = string.Empty;
        public int UnreadCount { get; set; }
        public string OwnerUserID { get; set; } = string.Empty; // Which user's chat list this belongs to
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;

        public ChatRoomModel ToChatRoomModel()
        {
            return new ChatRoomModel
            {
                RoomID = Guid.TryParse(RoomID, out var id) ? id : Guid.Empty,
                RoomName = RoomName,
                IsGroup = IsGroup,
                IsDeleted = IsDeleted,
                RoomPhoto = RoomPhoto,
                OtherUserID = OtherUserID,
                OtherUserPhoto = OtherUserPhoto,
                IsOnline = IsOnline,
                LastSeen = ParseDate(LastSeen) ?? DateTime.MinValue,
                LastMessage = LastMessage,
                LastMessageSenderId = LastMessageSenderId,
                LastMessageTime = ParseDate(LastMessageTime) ?? DateTime.MinValue,
                UnreadCount = UnreadCount
            };
        }

        public static CachedChatRoom FromChatRoomModel(ChatRoomModel r, string ownerUserId)
        {
            return new CachedChatRoom
            {
                RoomID = r.RoomID.ToString(),
                RoomName = r.RoomName,
                IsGroup = r.IsGroup,
                IsDeleted = r.IsDeleted,
                RoomPhoto = r.RoomPhoto,
                OtherUserID = r.OtherUserID,
                OtherUserPhoto = r.OtherUserPhoto,
                IsOnline = r.IsOnline,
                LastSeen = r.LastSeen.ToString("o"),
                LastMessage = r.LastMessage,
                LastMessageSenderId = r.LastMessageSenderId ?? string.Empty,
                LastMessageTime = r.LastMessageTime.ToString("o"),
                UnreadCount = r.UnreadCount,
                OwnerUserID = ownerUserId,
                CachedAt = DateTime.UtcNow
            };
        }

        private static DateTime? ParseDate(string s) =>
            string.IsNullOrEmpty(s) ? null : DateTime.TryParse(s, out var d) ? d : null;
    }

    // ─── Cached Chat Message (for offline chat history) ───
    [Table("CachedChatMessages")]
    public class CachedChatMessage
    {
        [PrimaryKey]
        public long MessageID { get; set; }
        [Indexed]
        public string RoomID { get; set; } = string.Empty;
        public string SenderID { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public string ReactionsJson { get; set; } = "[]";
        public string SeenByJson { get; set; } = "[]";
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;

        public ChatMessageModel ToChatMessageModel()
        {
            var model = new ChatMessageModel
            {
                MessageID = MessageID,
                RoomID = Guid.TryParse(RoomID, out var id) ? id : Guid.Empty,
                SenderID = SenderID,
                MessageText = MessageText,
                Timestamp = DateTime.TryParse(Timestamp, out var ts) ? ts : DateTime.MinValue,
                IsRead = IsRead
            };

            try
            {
                if (!string.IsNullOrEmpty(ReactionsJson) && ReactionsJson != "[]")
                    model.Reactions = System.Text.Json.JsonSerializer.Deserialize<List<ReactionModel>>(ReactionsJson) ?? new();
            }
            catch { model.Reactions = new(); }

            try
            {
                if (!string.IsNullOrEmpty(SeenByJson) && SeenByJson != "[]")
                    model.SeenBy = System.Text.Json.JsonSerializer.Deserialize<List<ReadReceiptModel>>(SeenByJson) ?? new();
            }
            catch { model.SeenBy = new(); }

            return model;
        }

        public static CachedChatMessage FromChatMessageModel(ChatMessageModel m)
        {
            var messageText = m.MessageText ?? string.Empty;
            if (ChatMessage.TryExtractAttachmentPayload(messageText, out _, out _, out _, out var previewText))
            {
                messageText = previewText;
            }

            return new CachedChatMessage
            {
                MessageID = m.MessageID,
                RoomID = m.RoomID.ToString(),
                SenderID = m.SenderID,
                MessageText = messageText,
                Timestamp = m.Timestamp.ToString("o"),
                IsRead = m.IsRead,
                ReactionsJson = m.Reactions?.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(m.Reactions)
                    : "[]",
                SeenByJson = m.SeenBy?.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(m.SeenBy)
                    : "[]",
                CachedAt = DateTime.UtcNow
            };
        }
    }
}
