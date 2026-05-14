using Microsoft.AspNetCore.SignalR.Client;
using StateMobile.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StateMobile.Services
{
    public class SignalRChatService : IChatService
    {
        private HubConnection _hubConnection;
        private readonly string _hubUrl = AppSettings.ApiBaseUrl + "/chatHub";

        public event Action<ChatMessageModel> MessageReceived;

        public SignalRChatService()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl)
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                .Build();

            _hubConnection.On<ChatMessageModel>("ReceiveMessage", (message) =>
            {
                MessageReceived?.Invoke(message);
            });
        }

        public async Task ConnectAsync()
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                try
                {
                    await _hubConnection.StartAsync();
                }
                catch (Exception ex)
                {
                    // Log error for mobile debugging
                    System.Diagnostics.Debug.WriteLine($"SignalR Connection Error: {ex.Message}");
                }
            }
        }

        public async Task DisconnectAsync()
        {
            if (_hubConnection.State != HubConnectionState.Disconnected)
            {
                await _hubConnection.StopAsync();
            }
        }

        public async Task<bool> SendMessageAsync(int roomId, string content)
        {
            try
            {
                if (_hubConnection.State != HubConnectionState.Connected)
                    await ConnectAsync();

                await _hubConnection.InvokeAsync("SendMessage", roomId, content);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<IEnumerable<ChatRoomModel>> GetChatRoomsAsync()
        {
            // In a real mobile app, this would call a REST API first to get history
            // then SignalR handles live updates.
            using var client = new HttpClient();
            var response = await client.GetFromJsonAsync<List<ChatRoomModel>>($"{AppSettings.ApiBaseUrl}/api/chat/rooms");
            return response ?? new List<ChatRoomModel>();
        }

        public async Task<IEnumerable<ChatMessageModel>> GetMessagesAsync(int roomId)
        {
            using var client = new HttpClient();
            var response = await client.GetFromJsonAsync<List<ChatMessageModel>>($"{AppSettings.ApiBaseUrl}/api/chat/rooms/{roomId}/messages");
            return response ?? new List<ChatMessageModel>();
        }

        public async Task MarkAsReadAsync(int roomId)
        {
            using var client = new HttpClient();
            await client.PostAsync($"{AppSettings.ApiBaseUrl}/api/chat/rooms/{roomId}/read", null);
        }
    }
}