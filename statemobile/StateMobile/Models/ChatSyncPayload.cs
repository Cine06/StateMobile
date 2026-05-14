using System;

namespace StateMobile.Models
{
    public sealed class ChatSyncPayload
    {
        public Guid RoomId { get; }
        public string SenderId { get; }
        public string MessageText { get; }
        public DateTime Timestamp { get; }

        public ChatSyncPayload(Guid roomId, string senderId, string messageText, DateTime timestamp)
        {
            RoomId = roomId;
            SenderId = senderId;
            MessageText = messageText;
            Timestamp = timestamp;
        }
    }
}
