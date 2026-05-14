using Microsoft.AspNetCore.SignalR.Client;
using StateMobile.Models;
using System;
using System.Threading.Tasks;

namespace StateMobile.Services
{
    public class SignalRChatService : IChatService
    {
        private HubConnection _hubConnection;
        private readonly string _hubUrl = AppSettings.ApiBaseUrl + "/chatHub";

        public event Action<ChatMessageModel> OnMessageReceived;
        public event Action<long, bool> OnMessageDeleted;
        public event Action<string, bool> OnUserStatusChanged;
        public event Action<string, bool> OnUserTyping;

        public async Task ConnectAsync(string userId)
        {
            if (_hubConnection?.State == HubConnectionState.Connected) return;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_hubUrl}?userId={userId}")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ChatMessageModel>("ReceiveMessage", (message) => OnMessageReceived?.Invoke(message));
            _hubConnection.On<long, bool>("MessageDeleted", (id, everyone) => OnMessageDeleted?.Invoke(id, everyone));
            _hubConnection.On<string, bool>("UserStatusChanged", (uid, online) => OnUserStatusChanged?.Invoke(uid, online));
            _hubConnection.On<string, bool>("UserTyping", (uid, typing) => OnUserTyping?.Invoke(uid, typing));

            await _hubConnection.StartAsync();
        }

        public async Task JoinRoomAsync(string roomId) => await _hubConnection.InvokeAsync("JoinRoom", roomId);
        public async Task LeaveRoomAsync(string roomId) => await _hubConnection.InvokeAsync("LeaveRoom", roomId);
        public async Task SendTypingAsync(string roomId, string userId, bool isTyping) => 
            await _hubConnection.InvokeAsync("Typing", roomId, userId, isTyping);

        public async Task DisconnectAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
            }
        }
    }
}