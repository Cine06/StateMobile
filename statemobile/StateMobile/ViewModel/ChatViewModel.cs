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
        private ObservableCollection<ChatMessageModel> messages = new();

        [ObservableProperty]
        private ChatRoomModel selectedRoom;

        [ObservableProperty]
        private string newMessageText;

        [ObservableProperty]
        private bool isBusy;

        public ChatViewModel(IChatService chatService)
        {
            _chatService = chatService;
            // Makinig sa real-time messages
            ((SignalRChatService)_chatService).MessageReceived += OnMessageReceived;
        }

        public async Task InitializeAsync(ChatRoomModel room)
        {
            SelectedRoom = room;
            IsBusy = true;

            try
            {
                await _chatService.ConnectAsync();
                var history = await _chatService.GetMessagesAsync(room.RoomId);
                
                Messages.Clear();
                foreach (var msg in history)
                    Messages.Add(msg);

                await _chatService.MarkAsReadAsync(room.RoomId);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(NewMessageText)) return;

            var content = NewMessageText;
            NewMessageText = string.Empty; // Clear agad para sa magandang UX

            var success = await _chatService.SendMessageAsync(SelectedRoom.RoomId, content);
            if (!success)
            {
                // Ibalik ang text kung nag-fail
                NewMessageText = content;
                await App.Current.MainPage.DisplayAlert("Error", "Failed to send message.", "OK");
            }
        }

        private void OnMessageReceived(ChatMessageModel message)
        {
            // Siguraduhin na para sa kasalukuyang room ang message
            if (message.RoomId == SelectedRoom?.RoomId)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Messages.Add(message);
                });
            }
        }

        [RelayCommand]
        private async Task Back()
        {
            await _chatService.DisconnectAsync();
            await Shell.Current.GoToAsync("..");
        }
    }
}