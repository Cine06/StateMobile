using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StateMobile.Models;
using StateMobile.Services;
using System.Collections.ObjectModel;

namespace StateMobile.ViewModel
{
    public partial class ChatViewModel : BasedViewModel
    {
        private readonly IChatService _chatService;
        
        [ObservableProperty]
        private ObservableCollection<ChatRoomModel> _chatRooms = new();

        [ObservableProperty]
        private ObservableCollection<ChatMessageModel> _messages = new();

        [ObservableProperty]
        private string _newMessageText;

        [ObservableProperty]
        private ChatRoomModel _selectedRoom;

        public ChatViewModel(IChatService chatService)
        {
            _chatService = chatService;
            _chatService.MessageReceived += OnMessageReceived;
        }

        [RelayCommand]
        public async Task LoadRooms()
        {
            IsBusy = true;
            var rooms = await _chatService.GetChatRoomsAsync();
            ChatRooms = new ObservableCollection<ChatRoomModel>(rooms);
            IsBusy = false;
        }

        [RelayCommand]
        public async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(NewMessageText) || SelectedRoom == null) return;

            var content = NewMessageText;
            NewMessageText = string.Empty; // Clear immediately for snappy feel

            var success = await _chatService.SendMessageAsync(SelectedRoom.Id, content);
            if (!success)
            {
                // Handle failure (e.g., show toast)
            }
        }

        private void OnMessageReceived(ChatMessageModel message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (SelectedRoom != null && message.RoomId == SelectedRoom.Id)
                {
                    Messages.Add(message);
                    _chatService.MarkAsReadAsync(SelectedRoom.Id);
                }
                else
                {
                    // Update unread count in the list
                    var room = ChatRooms.FirstOrDefault(r => r.Id == message.RoomId);
                    if (room != null)
                    {
                        room.UnreadCount++;
                        room.LastMessage = message.Content;
                        room.LastMessageTime = DateTime.Now.ToString("HH:mm");
                    }
                }
            });
        }
    }
}