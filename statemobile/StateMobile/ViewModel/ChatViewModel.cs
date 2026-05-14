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
        private readonly IDocumentScannerService _scannerService;
        private readonly string _currentUserId;

        [ObservableProperty]
        private string _messageText;

        [ObservableProperty]
        private ChatRoomModel _currentRoom;

        public ObservableCollection<ChatMessageModel> Messages { get; } = new();

        public ChatViewModel(IChatService chatService, IDocumentScannerService scannerService)
        {
            _chatService = chatService;
            _scannerService = scannerService;
            _currentUserId = Preferences.Get("UserId", "");

            _chatService.OnMessageReceived += (msg) => MainThread.BeginInvokeOnMainThread(() => Messages.Add(msg));
            _chatService.OnMessageDeleted += (id, everyone) => MainThread.BeginInvokeOnMainThread(() => {
                var msg = Messages.FirstOrDefault(m => m.MessageID == id);
                if (msg != null) Messages.Remove(msg);
            });
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(MessageText)) return;

            var message = new ChatMessageModel
            {
                RoomID = CurrentRoom.RoomID,
                SenderID = _currentUserId,
                MessageText = MessageText
            };

            // Call API to save and broadcast
            // (Implementation would use a HttpClient service)
            MessageText = string.Empty;
        }

        [RelayCommand]
        private async Task ScanDocument()
        {
            var result = await _scannerService.ScanDocumentAsync();
            if (result != null)
            {
                // Handle scanned document (e.g., upload and send as attachment)
                await Shell.Current.DisplayAlert("Success", "Document scanned and ready to send.", "OK");
            }
        }

        [RelayCommand]
        private async Task AttachFile()
        {
            var result = await FilePicker.PickAsync();
            if (result != null)
            {
                // Handle file attachment
            }
        }
    }
}