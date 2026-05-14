using System.Collections.Generic;
using System;

namespace StateMobile.Models
{
    public class ChatMessageModel
    {
        public long MessageID { get; set; }
        public Guid RoomID { get; set; }
        public string SenderID { get; set; }
        public string MessageText { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsRead { get; set; }
        public string? AttachmentDataBase64 { get; set; }

        public string CurrentUserId { get; set; }
        public bool IsFromCurrentUser => SenderID == CurrentUserId;

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
        public bool RequestFailed { get; set; }
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
}