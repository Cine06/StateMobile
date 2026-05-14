using StateMobile.Models;

namespace StateMobile.Services
{
    public interface IChatService
    {
        Task<IEnumerable<ChatRoomModel>> GetChatRoomsAsync();
        Task<IEnumerable<ChatMessageModel>> GetMessagesAsync(int roomId);
        Task<bool> SendMessageAsync(int roomId, string content);
        Task MarkAsReadAsync(int roomId);
        Task ConnectAsync();
        Task DisconnectAsync();
        event Action<ChatMessageModel> MessageReceived;
    }
}