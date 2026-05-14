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

        [ObservableProperty] private ObservableCollection<ChatMessageModel> messages = new();
        [ObservableProperty] private string newMessageText;

        public ChatViewModel(IChatService chatService, IDocumentScannerService scannerService)
        {
            _chatService = chatService;
            _scannerService = scannerService;
            
            if (_chatService is SignalRChatService signalR)
            {
                signalR.MessageReceived += (msg) => MainThread.BeginInvokeOnMainThread(() => Messages.Add(msg));
            }
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(NewMessageText)) return;

            var msg = new ChatMessageModel { 
                MessageText = NewMessageText, 
                IsMine = true, 
                Timestamp = DateTime.Now,
                IsSending = true 
            };
            Messages.Add(msg);
            
            var text = NewMessageText;
            NewMessageText = string.Empty;

            var success = await _chatService.SendMessageAsync(SelectedRoom.RoomId, text);
            if (!success) {
                msg.IsFailed = true;
                msg.IsSending = false;
            }
        }

        [RelayCommand]
        private async Task PickAttachment()
        {
            try {
                var result = await FilePicker.Default.PickAsync(new PickOptions { FileTypes = FilePickerFileType.Images });
                if (result != null) {
                    // Professional Image + Text support
                    var success = await ((SignalRChatService)_chatService).SendAttachmentAsync(
                        SelectedRoom.RoomId, result.FullPath, "image", NewMessageText);
                    
                    if (success) NewMessageText = string.Empty;
                    else await App.Current.MainPage.DisplayAlert("Error", "Failed to send image.", "OK");
                }
            } catch { /* Handle error */ }
        }

        [RelayCommand]
        private async Task RetrySend(ChatMessageModel msg)
        {
            msg.IsFailed = false;
            msg.IsSending = true;
            var success = await _chatService.SendMessageAsync(SelectedRoom.RoomId, msg.MessageText);
            if (!success) msg.IsFailed = true;
        }
    }
}