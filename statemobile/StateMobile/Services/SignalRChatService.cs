using Microsoft.AspNetCore.SignalR.Client;
using StateMobile.Models;
using System.Net.Http.Json;

namespace StateMobile.Services
{
    public class SignalRChatService : IChatService
    {
        private HubConnection _hubConnection;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "http://192.168.1.193:5103"; // I-adjust base sa AppSettings

        public event Action<ChatMessageModel> MessageReceived;
        public event Action<string> ConnectionStatusChanged;

        public SignalRChatService()
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(_baseUrl) };
            
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/chatHub")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ChatMessageModel>("ReceiveMessage", (message) =>
            {
                MessageReceived?.Invoke(message);
            });

            _hubConnection.Reconnecting += (error) => {
                ConnectionStatusChanged?.Invoke("Reconnecting...");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += (connectionId) => {
                ConnectionStatusChanged?.Invoke("Connected");
                return Task.CompletedTask;
            };
        }

        public async Task ConnectAsync()
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                try { await _hubConnection.StartAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SignalR Start Error: {ex.Message}"); }
            }
        }

        public async Task DisconnectAsync() => await _hubConnection.StopAsync();

        public async Task<IEnumerable<ChatRoomModel>> GetChatRoomsAsync()
        {
            try { return await _httpClient.GetFromJsonAsync<List<ChatRoomModel>>("api/chat/rooms") ?? new(); }
            catch { return new List<ChatRoomModel>(); }
        }

        public async Task<IEnumerable<ChatMessageModel>> GetMessagesAsync(int roomId)
        {
            try { return await _httpClient.GetFromJsonAsync<List<ChatMessageModel>>($"api/chat/rooms/{roomId}/messages") ?? new(); }
            catch { return new List<ChatMessageModel>(); }
        }

        public async Task<bool> SendMessageAsync(int roomId, string content)
        {
            try
            {
                // Siguraduhing connected bago mag-send
                if (_hubConnection.State != HubConnectionState.Connected) await ConnectAsync();
                
                // Mas maganda kung via Hub para real-time agad sa lahat
                await _hubConnection.InvokeAsync("SendMessage", roomId, content);
                return true;
            }
            catch { return false; }
        }

        public async Task MarkAsReadAsync(int roomId)
        {
            await _httpClient.PostAsync($"api/chat/rooms/{roomId}/read", null);
        }
    }
}