using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using StateMobile.Models;

namespace StateMobile.Services
{

    public class ChatService : IChatService, IDisposable
    {
        private HubConnection? _hubConnection;
        private readonly IUserSessionService _sessionService;

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public event Action<string, long, string, string>? OnMessageReceived;
        public event Action<string, bool>? OnUserStatusChanged;
        public event Action<Guid, string, string>? OnNewChatMessage;
        public event Action<string, long, string, string>? OnMessageReactionReceived;
        public event Action<string, long, string>? OnMessageReactionRemoved;
        public event Action<long, string>? OnMessageReadReceived;
        public event Action<Guid, string>? OnRoomReadReceived;
        public event Action<Guid, long>? OnMessageDeleted;
        public event Action<Guid>? OnChatUpdated;
        public event Action<Guid, string, string>? OnChatUpdatedWithRoom;
        public event Action<Guid, string, string>? OnRoomUpdated;
        public event Action<Guid, string, bool>? OnParticipantRoleUpdated;
        public event Action<Guid, string>? OnParticipantRemoved;
        public event Action<Guid, List<string>>? OnParticipantsAdded;
        public event Action<ChatRoomModel>? OnRoomAdded; 

        public ChatService(IUserSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        public async Task EnsureConnected()
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
            {
                await Connect("");
            }
        }

        public async Task Connect(string roomId)
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                System.Diagnostics.Debug.WriteLine("✅ Already connected to SignalR, joining room...");
                
                var userId = _sessionService.CurrentUser?.UserID ?? "unknown";
                if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(userId))
                {
                    try
                    {
                        await _hubConnection.InvokeAsync("JoinRoom", roomId);
                        System.Diagnostics.Debug.WriteLine($"✅ Joined room: {roomId}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to join room: {ex.Message}");
                        throw;
                    }
                }
                return;
            }

            try
            {
                var userId = _sessionService.CurrentUser?.UserID ?? "unknown";
                var connectionUrl = $"{AppSettings.ChatHubUrl}?userId={Uri.EscapeDataString(userId)}";

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(connectionUrl, options =>
                    {
                        options.HttpMessageHandlerFactory = (handler) =>
                        {
                            if (handler is HttpClientHandler clientHandler)
                            {
#if DEBUG
                                clientHandler.ServerCertificateCustomValidationCallback =
                                    (message, cert, chain, errors) => true;
#endif
                            }
                            return handler;
                        };
                    })
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10) })
                    .ConfigureLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Information);
                    })
                    .Build();

                _hubConnection.Reconnecting += (error) =>
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 SignalR reconnecting: {error?.Message}");
                    return Task.CompletedTask;
                };

                _hubConnection.Reconnected += (connectionId) =>
                {
                    System.Diagnostics.Debug.WriteLine($"✅ SignalR reconnected: {connectionId}");
                    return Task.CompletedTask;
                };

                _hubConnection.Closed += async (error) =>
                {
                    System.Diagnostics.Debug.WriteLine($"❌ SignalR connection closed: {error?.Message}");
                    await Task.Delay(5000);
                };

                _hubConnection.On<string, long, string, string>("ReceiveMessage", (roomId, messageId, senderId, message) =>
                {
                    OnMessageReceived?.Invoke(roomId, messageId, senderId, message);
                });
                _hubConnection.On<string, bool>("UserStatusChanged", OnUserStatusChangedHandler);
                _hubConnection.On<Guid, string, string>("NewChatMessage", OnNewChatMessageHandler);
                _hubConnection.On<string, long, string, string>("MessageReactionReceived", (roomId, messageId, senderId, reactionType) => 
                {
                    OnMessageReactionReceived?.Invoke(roomId, messageId, senderId, reactionType);
                });
                _hubConnection.On<string, long, string>("MessageReactionRemoved", (roomId, messageId, userId) =>
                {
                    OnMessageReactionRemoved?.Invoke(roomId, messageId, userId);
                });
                _hubConnection.On<long, string>("MessageReadReceived", (messageId, userId) => 
                {
                    OnMessageReadReceived?.Invoke(messageId, userId);
                });
                _hubConnection.On<string, string>("RoomReadReceived", (roomIdStr, userId) => 
                {
                    if (Guid.TryParse(roomIdStr, out var roomId))
                    {
                        OnRoomReadReceived?.Invoke(roomId, userId);
                    }
                });
                _hubConnection.On<Guid, long>("MessageDeleted", (roomId, messageId) =>
                {
                    System.Diagnostics.Debug.WriteLine($"🗑️ MessageDeleted in room {roomId}: {messageId}");
                    OnMessageDeleted?.Invoke(roomId, messageId);
                });

                _hubConnection.On<Guid>("ChatUpdated", (roomId) =>
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 ChatUpdated in room {roomId}");
                    OnChatUpdated?.Invoke(roomId);
                });

                _hubConnection.On<Guid, string, string>("ChatUpdated", (roomId, roomName, roomPhoto) =>
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 ChatUpdated with room payload in room {roomId}: {roomName}");
                    OnChatUpdatedWithRoom?.Invoke(roomId, roomName ?? string.Empty, roomPhoto ?? string.Empty);
                });

                _hubConnection.On<Guid, string, string>("RoomUpdated", (roomId, roomName, roomPhoto) =>
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 RoomUpdated in room {roomId}: {roomName}");
                    OnRoomUpdated?.Invoke(roomId, roomName ?? string.Empty, roomPhoto ?? string.Empty);
                });

                _hubConnection.On<Guid, string, bool>("ParticipantRoleUpdated", (roomId, userId, isAdmin) =>
                {
                    System.Diagnostics.Debug.WriteLine($"👥 ParticipantRoleUpdated in room {roomId}: User={userId}, IsAdmin={isAdmin}");
                    OnParticipantRoleUpdated?.Invoke(roomId, userId, isAdmin);
                });

                _hubConnection.On<Guid, string>("ParticipantRemoved", (roomId, userId) =>
                {
                    System.Diagnostics.Debug.WriteLine($"🚪 ParticipantRemoved in room {roomId}: User={userId}");
                    OnParticipantRemoved?.Invoke(roomId, userId);
                });

                _hubConnection.On<Guid, List<string>>("ParticipantsAdded", (roomId, userIds) =>
                {
                    System.Diagnostics.Debug.WriteLine($"📥 ParticipantsAdded in room {roomId}: {userIds.Count} users");
                    OnParticipantsAdded?.Invoke(roomId, userIds);
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _hubConnection.StartAsync(cts.Token);

                System.Diagnostics.Debug.WriteLine("✅ SignalR connected successfully");

                if (!string.IsNullOrEmpty(userId))
                {
                    await _hubConnection.InvokeAsync("JoinRoom", roomId);
                    System.Diagnostics.Debug.WriteLine($"✅ Joined room: {roomId}");
                }
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ SignalR connection timeout (10s)");
                throw new Exception("SignalR connection timeout. Real-time updates unavailable.");
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR HTTP error: {ex.Message}");
                throw new Exception($"Cannot reach SignalR server: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR connection failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task Disconnect()
        {
            if (_hubConnection != null)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("🔌 Disconnecting from SignalR...");
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                    _hubConnection = null;
                    System.Diagnostics.Debug.WriteLine("✅ Disconnected from SignalR");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error disconnecting: {ex.Message}");
                }
            }
        }

        public async Task SendMessage(Guid roomId, long messageId, string senderId, string messageText)
        {
            if (_hubConnection?.State != HubConnectionState.Connected)
            {
                System.Diagnostics.Debug.WriteLine("❌ Cannot send message: Not connected");
                throw new InvalidOperationException("Not connected to SignalR hub");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"📤 Sending message to room {roomId}, ID: {messageId}");
                await _hubConnection.InvokeAsync("SendMessageToRoom", roomId.ToString(), messageId, senderId, messageText);
                System.Diagnostics.Debug.WriteLine("✅ Message sent successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to send message: {ex.Message}");
                throw;
            }
        }

        public async Task SendReaction(Guid roomId, long messageId, string reactionType)
        {
            if (_hubConnection?.State != HubConnectionState.Connected) return;
            try
            {
                var senderId = _sessionService.CurrentUser?.UserID ?? "unknown";
                await _hubConnection.InvokeAsync("SendReaction", roomId.ToString(), messageId, senderId, reactionType);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to send reaction: {ex.Message}");
            }
        }

        public async Task SendRemoveReaction(Guid roomId, long messageId)
        {
            if (_hubConnection?.State != HubConnectionState.Connected) return;
            try
            {
                var userId = _sessionService.CurrentUser?.UserID ?? "unknown";
                await _hubConnection.InvokeAsync("SendRemoveReaction", roomId.ToString(), messageId, userId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to remove reaction: {ex.Message}");
            }
        }

        public async Task SendReadReceipt(Guid roomId, long messageId)
        {
            if (_hubConnection?.State != HubConnectionState.Connected) return;
            try
            {
                var userId = _sessionService.CurrentUser?.UserID ?? "unknown";
                await _hubConnection.InvokeAsync("SendReadReceipt", roomId.ToString(), messageId, userId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to send read receipt: {ex.Message}");
            }
        }

        public async Task SendRoomReadReceipt(Guid roomId)
        {
            if (_hubConnection?.State != HubConnectionState.Connected) return;
            try
            {
                var userId = _sessionService.CurrentUser?.UserID ?? "unknown";
                await _hubConnection.InvokeAsync("SendRoomReadReceipt", roomId.ToString(), userId);
                System.Diagnostics.Debug.WriteLine($"✅ Sent room read receipt for {roomId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to send room read receipt: {ex.Message}");
            }
        }

        public async Task NotifyMessageDeleted(Guid roomId, long messageId)
        {
            if (_hubConnection?.State != HubConnectionState.Connected)
            {
                OnMessageDeleted?.Invoke(roomId, messageId);
                return;
            }

            try
            {
                await _hubConnection.InvokeAsync("NotifyMessageDeleted", roomId.ToString(), messageId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to notify message deletion: {ex.Message}");
                OnMessageDeleted?.Invoke(roomId, messageId);
            }
        }

        public async Task JoinRoom(string roomId, string userId)
        {
            if (_hubConnection?.State != HubConnectionState.Connected)
            {
                System.Diagnostics.Debug.WriteLine("❌ Cannot join room: Not connected");
                throw new InvalidOperationException("Not connected to SignalR hub");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"🚪 Joining room {roomId} as {userId}");
                await _hubConnection.InvokeAsync("JoinRoom", roomId, userId);
                System.Diagnostics.Debug.WriteLine($"✅ Successfully joined room {roomId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to join room: {ex.Message}");
                throw;
            }
        }

        public async Task LeaveRoom(string roomId)
        {
            if (_hubConnection?.State != HubConnectionState.Connected) return;
            try
            {
                await _hubConnection.InvokeAsync("LeaveRoom", roomId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to leave room: {ex.Message}");
            }
        }

        public async Task JoinAllRooms(List<string> roomIds)
        {
            if (_hubConnection?.State != HubConnectionState.Connected) return;
            try
            {
                await _hubConnection.InvokeAsync("JoinAllRooms", roomIds);
                System.Diagnostics.Debug.WriteLine($"✅ Joined {roomIds.Count} rooms");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to join all rooms: {ex.Message}");
            }
        }

        private void OnUserStatusChangedHandler(string userId, bool isOnline)
        {
            System.Diagnostics.Debug.WriteLine($"👤 User status changed: {userId} - {(isOnline ? "Online" : "Offline")}");
            OnUserStatusChanged?.Invoke(userId, isOnline);
        }

        private void OnNewChatMessageHandler(Guid roomId, string senderId, string messageText)
        {
            System.Diagnostics.Debug.WriteLine($"💬 New message in room {roomId} from {senderId}: {messageText}");
            OnNewChatMessage?.Invoke(roomId, senderId, messageText);
        }

        public void NotifyRoomAdded(ChatRoomModel room)
        {
            if (room != null)
            {
                OnRoomAdded?.Invoke(room);
            }
        }

        public void Dispose()
        {
            Disconnect().GetAwaiter().GetResult();
        }
    }
}