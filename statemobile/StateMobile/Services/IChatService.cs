using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StateMobile.Models;

namespace StateMobile.Services
{
    public interface IChatService
    {
        bool IsConnected { get; }

        Task EnsureConnected();
        Task Connect(string roomId);
        Task SendMessage(Guid roomId, long messageId, string senderId, string message);
        Task SendReaction(Guid roomId, long messageId, string reactionType);
        Task SendRemoveReaction(Guid roomId, long messageId);
        Task SendReadReceipt(Guid roomId, long messageId);
        Task SendRoomReadReceipt(Guid roomId);
        Task NotifyMessageDeleted(Guid roomId, long messageId);
        Task JoinRoom(string roomId, string userId);
        Task JoinAllRooms(List<string> roomIds);
        Task LeaveRoom(string roomId);
        Task Disconnect();

        void NotifyRoomAdded(ChatRoomModel room);

        event Action<string, long, string, string>? OnMessageReceived;
        event Action<string, bool>? OnUserStatusChanged;
        event Action<Guid, string, string>? OnNewChatMessage;
        event Action<string, long, string, string>? OnMessageReactionReceived;
        event Action<string, long, string>? OnMessageReactionRemoved;
        event Action<long, string>? OnMessageReadReceived;
        event Action<Guid, string>? OnRoomReadReceived;
        event Action<Guid, long>? OnMessageDeleted;
        event Action<Guid>? OnChatUpdated;
        event Action<Guid, string, string>? OnChatUpdatedWithRoom;
        event Action<Guid, string, string>? OnRoomUpdated;
        event Action<Guid, string, bool>? OnParticipantRoleUpdated;
        event Action<Guid, string>? OnParticipantRemoved;
        event Action<Guid, List<string>>? OnParticipantsAdded;
        event Action<ChatRoomModel>? OnRoomAdded;
    }
}
