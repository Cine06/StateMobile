using Microsoft.AspNetCore.SignalR.Client;
using StateMobile.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StateMobile.Services
{
    public class SignalRChatService : IChatService
    {
        private HubConnection _hubConnection;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "http://192.168.1.193:5103";

        public event Action<ChatMessageModel> MessageReceived;
        public event Action<long, bool> MessageDeleted; // MessageID, IsForEveryone

        public SignalRChatService()
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(_baseUrl) };
            SetupSignalR();
        }

        private void SetupSignalR()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/chatHub")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ChatMessageModel>("ReceiveMessage", (msg) => MessageReceived?.Invoke(msg));
            _hubConnection.On<long, bool>("MessageDeleted", (id, everyone) => MessageDeleted?.Invoke(id, everyone));
        }

        public async Task ConnectAsync() => await _hubConnection.StartAsync();
        public async Task DisconnectAsync() => await _hubConnection.StopAsync();

        public async Task<bool> SendMessageAsync(int roomId, string content)
        {
            try { await _hubConnection.InvokeAsync("SendMessage", roomId, content); return true; }
            catch { return false; }
        }

        // Professional Attachment Upload
        public async Task<bool> SendAttachmentAsync(Guid roomId, string filePath, string type)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                var fileContent = new StreamContent(File.OpenRead(filePath));
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(type == "image" ? "image/jpeg" : "application/pdf");
                
                content.Add(fileContent, "file", Path.GetFileName(filePath));
                content.Add(new StringContent(roomId.ToString()), "roomId");
                content.Add(new StringContent(type), "type");

                var response = await _httpClient.PostAsync("api/chat/upload", content);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task DeleteMessageAsync(long messageId, bool forEveryone)
        {
            await _httpClient.PostAsJsonAsync("api/chat/delete", new { MessageId = messageId, ForEveryone = forEveryone });
        }

        public async Task<IEnumerable<ChatMessageModel>> GetMessagesAsync(int roomId)
        {
            return await _httpClient.GetFromJsonAsync<List<ChatMessageModel>>($"api/chat/rooms/{roomId}/messages") ?? new();
        }

        public async Task MarkAsReadAsync(int roomId) => await _httpClient.PostAsync($"api/chat/rooms/{roomId}/read", null);
    }
}