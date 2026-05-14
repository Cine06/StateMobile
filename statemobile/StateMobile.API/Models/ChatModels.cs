namespace StateMobile.API.Models;

public class ChatRoomModel
{
    public Guid RoomID { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
    public string LastMessageSenderId { get; set; } = string.Empty;
    public DateTime LastMessageTime { get; set; }
    public string OtherUserID { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsDeleted { get; set; }
    public int UnreadCount { get; set; }
    public string OtherUserPhoto { get; set; } = string.Empty;
    public bool IsGroup { get; set; }
    public string RoomPhoto { get; set; } = string.Empty;
}

public class ReactionModel
{
    public long ReactionID { get; set; }
    public long MessageID { get; set; }
    public string UserID { get; set; } = string.Empty;
    public string ReactionType { get; set; } = string.Empty;
}

public class ReadReceiptModel
{
    public long MessageID { get; set; }
    public string UserID { get; set; } = string.Empty;
    public DateTime ReadAt { get; set; }
}

public class ChatMessageModel
{
    public long MessageID { get; set; }
    public Guid RoomID { get; set; }
    public string SenderID { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsRead { get; set; }
    public List<ReactionModel> Reactions { get; set; } = new();
    public List<ReadReceiptModel> SeenBy { get; set; } = new();
}

public class ChatMessageContentResponse
{
    public long MessageID { get; set; }
    public string MessageText { get; set; } = string.Empty;
}

public class ChatMessagesPageResult
{
    public List<ChatMessageModel> Messages { get; set; } = new();
    public bool HasMoreOlderMessages { get; set; }
    public long? OldestMessageId { get; set; }
    public DateTime? OldestTimestamp { get; set; }
}

public class SendMessageRequest
{
    public Guid RoomId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
}

public class CreateChatRoomRequest
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public string TargetFullName { get; set; } = string.Empty;
    public List<string>? TargetUserIds { get; set; }
    public bool IsGroup { get; set; }
}

public class UserStatusModel
{
    public string UserID { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChatParticipantModel
{
    public string UserID { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Photo { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public bool IsAdmin { get; set; }
}

public class UpdateChatRoomRequest
{
    public Guid RoomID { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string RoomPhoto { get; set; } = string.Empty;
    public string ActorID { get; set; } = string.Empty;
}



public class UpdateParticipantRoleRequest
{
    public Guid RoomID { get; set; }
    public string UserID { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string ActorID { get; set; } = string.Empty;
}