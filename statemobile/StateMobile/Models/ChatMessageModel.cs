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
        public bool IsDeleted { get; set; }
        
        // UI/Status Properties
        public bool IsMe => SenderID == Preferences.Get("UserId", "");
        public bool IsSending { get; set; }
        public bool IsFailed { get; set; }
        
        // Attachment Info
        public AttachmentModel Attachment { get; set; }
    }

    public class AttachmentModel
    {
        public string FileName { get; set; }
        public string FileType { get; set; }
        public byte[] Data { get; set; }
        public string LocalPath { get; set; }
    }
}