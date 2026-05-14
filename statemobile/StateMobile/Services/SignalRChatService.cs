using Microsoft.AspNetCore.SignalR.Client;
using StateMobile.Models;

namespace StateMobile.Services
{
    public class SignalRChatService : IChatService
    {
        private HubConnection? _hubConnection;
        private readonly IUserSessionService _sessionService;
        private readonly IBadgeService _badgeService;
        private readonly HashSet<string> _joinedRooms = new();
        private string? _currentRoomId;

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

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public SignalRChatService(IUserSessionService sessionService, IBadgeService badgeService, OfflineDatabase? offlineDb = null)
        {
            _sessionService = sessionService;
            _badgeService = badgeService;
        }

        /// <summary>
        /// Ensures the SignalR connection is established without joining any room.
        /// Call this to keep the connection alive for receiving global events.
        /// </summary>
        public async Task EnsureConnected()
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
                return;

            await BuildAndStartConnection();
        }

        public async Task Connect(string roomId)
        {
            try
            {
                if (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected)
                {
                    await BuildAndStartConnection();
                }

                // Join the room if not already joined
                if (!_joinedRooms.Contains(roomId))
                {
                    await _hubConnection.InvokeAsync("JoinRoom", roomId);
                    _joinedRooms.Add(roomId);
                    System.Diagnostics.Debug.WriteLine($"✅ Joined room: {roomId} (total: {_joinedRooms.Count})");
                }
                _currentRoomId = roomId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR Connection Error: {ex.Message}");
                throw;
            }
        }

        private async Task BuildAndStartConnection()
        {
            var userId = _sessionService.CurrentUser?.UserID ?? "guest";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{AppSettings.ChatHubUrl}?userId={userId}") 
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10) })
                .Build();


            _hubConnection.HandshakeTimeout = TimeSpan.FromSeconds(30);
            _hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(15);
            _hubConnection.ServerTimeout = TimeSpan.FromMinutes(2);

            _hubConnection.On<string, long, string, string>("ReceiveMessage", (receivedRoomId, messageId, senderId, message) =>
            {
                // ✅ Increment global badge if sender is not current user AND we are not in the room
                if (senderId != _sessionService.CurrentUser?.UserID && _currentRoomId != receivedRoomId)
                {
                    _badgeService.IncrementChat();
                    System.Diagnostics.Debug.WriteLine($"🔔 Global badge incremented via ReceiveMessage from {senderId}");
                }
                
                OnMessageReceived?.Invoke(receivedRoomId, messageId, senderId, message);
            });

            _hubConnection.On<string, bool>("UserStatusChanged", (userId, isOnline) =>
            {
                System.Diagnostics.Debug.WriteLine($"🟢 Status update: {userId} is {(isOnline ? "online" : "offline")}");
                OnUserStatusChanged?.Invoke(userId, isOnline);
            });

            _hubConnection.On<Guid, string, string>("NewChatMessage", (roomId, senderId, message) =>
            {
                System.Diagnostics.Debug.WriteLine($"📩 New message in room {roomId} from {senderId}: {message}");

                if (senderId != _sessionService.CurrentUser?.UserID && _currentRoomId != roomId.ToString())
                {
                    _badgeService.IncrementChat();
                }

                OnNewChatMessage?.Invoke(roomId, senderId, message);
            });

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
                    System.Diagnostics.Debug.WriteLine($"📬 RoomReadReceived: room={roomIdStr}, user={userId}");
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

            _hubConnection.Reconnected += async (connectionId) =>
            {
                System.Diagnostics.Debug.WriteLine($"🔄 SignalR reconnected, re-joining {_joinedRooms.Count} rooms");
                
                if (_joinedRooms.Count > 0)
                {
                    try
                    {
                        await _hubConnection.InvokeAsync("JoinAllRooms", _joinedRooms.ToList());
                        System.Diagnostics.Debug.WriteLine($"✅ Re-joined all {_joinedRooms.Count} rooms after reconnect");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to re-join rooms: {ex.Message}");
                    }
                }
            };
            
            _hubConnection.Closed += async (ex) =>
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ SignalR disconnected: {ex?.Message}");
            };

            await _hubConnection.StartAsync();
            System.Diagnostics.Debug.WriteLine("✅ SignalR Connected");
        }

        public async Task SendMessage(Guid roomId, long messageId, string senderId, string message)
        {
            try
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.InvokeAsync("SendMessageToRoom", roomId.ToString(), messageId, senderId, message);
                }

                // ✅ Trigger locally so sender's views (like ChatListPage) update immediately
                OnMessageReceived?.Invoke(roomId.ToString(), messageId, senderId, message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Send error: {ex.Message}");
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
                
                // Trigger locally so UI updates immediately
                OnRoomReadReceived?.Invoke(roomId, userId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to send room read receipt: {ex.Message}");
            }
        }

        public async Task NotifyMessageDeleted(Guid roomId, long messageId)
        {
            try
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.InvokeAsync("NotifyMessageDeleted", roomId.ToString(), messageId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to notify message deletion: {ex.Message}");
            }
            finally
            {
                // Always trigger locally so other views (like ChatListPage) can refresh
                OnMessageDeleted?.Invoke(roomId, messageId);
            }
        }

        public async Task JoinRoom(string roomId, string userId)
        {
            try
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    if (!_joinedRooms.Contains(roomId))
                    {
                        await _hubConnection.InvokeAsync("JoinRoom", roomId);
                        _joinedRooms.Add(roomId);
                    }
                    _currentRoomId = roomId;
                }
                else
                {
                    throw new InvalidOperationException("Hub connection is not in a connected state.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Join room error: {ex.Message}");
                throw;
            }
        }

        public async Task JoinAllRooms(List<string> roomIds)
        {
            if (_hubConnection?.State != HubConnectionState.Connected) return;
            try
            {
                var newRooms = roomIds.Where(id => !_joinedRooms.Contains(id)).ToList();
                if (newRooms.Count > 0)
                {
                    await _hubConnection.InvokeAsync("JoinAllRooms", newRooms);
                    foreach (var id in newRooms)
                    {
                        _joinedRooms.Add(id);
                    }
                    System.Diagnostics.Debug.WriteLine($"✅ Joined {newRooms.Count} new rooms (total: {_joinedRooms.Count})");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ JoinAllRooms error: {ex.Message}");
            }
        }

        public async Task LeaveRoom(string roomId)
        {
            try
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.InvokeAsync("LeaveRoom", roomId);
                    _joinedRooms.Remove(roomId);
                    System.Diagnostics.Debug.WriteLine($"✅ Left room group: {roomId} (remaining: {_joinedRooms.Count})");
                    if (_currentRoomId == roomId)
                    {
                        _currentRoomId = null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Leave room error: {ex.Message}");
            }
        }

        public async Task Disconnect()
        {
            try
            {
                if (_hubConnection != null)
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                    _hubConnection = null;
                    _joinedRooms.Clear();
                    _currentRoomId = null;
                    System.Diagnostics.Debug.WriteLine("✅ SignalR disconnected, cleared all room memberships");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Disconnect error: {ex.Message}");
            }
        }
        
        public void NotifyRoomAdded(ChatRoomModel room)
        {
            if (room != null)
            {
                OnRoomAdded?.Invoke(room);
            }
        }
    }
}