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
        
        // New Properties
        public string AttachmentPath { get; set; }
        public string AttachmentType { get; set; }
        public bool IsDeletedForEveryone { get; set; }
        public bool IsMine { get; set; } // Helper for UI
        
        public bool HasAttachment => !string.IsNullOrEmpty(AttachmentPath);
        public bool IsImage => AttachmentType?.ToLower() == "image";
    }
}