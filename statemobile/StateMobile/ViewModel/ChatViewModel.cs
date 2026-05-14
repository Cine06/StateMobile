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
        [ObservableProperty] private ChatRoomModel selectedRoom;

        public ChatViewModel(IChatService chatService, IDocumentScannerService scannerService)
        {
            _chatService = chatService;
            _scannerService = scannerService;
            
            if (_chatService is SignalRChatService signalR)
            {
                signalR.MessageReceived += (msg) => MainThread.BeginInvokeOnMainThread(() => Messages.Add(msg));
                signalR.MessageDeleted += (id, everyone) => HandleDeletion(id, everyone);
            }
        }

        [RelayCommand]
        private async Task PickAttachment()
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions {
                PickerTitle = "Select Attachment",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                await ((SignalRChatService)_chatService).SendAttachmentAsync(SelectedRoom.RoomId, result.FullPath, "image");
            }
        }

        [RelayCommand]
        private async Task ScanDocument()
        {
            var result = await _scannerService.ScanDocumentAsync();
            if (result != null && result.Any())
            {
                // I-upload ang unang scanned page bilang PDF/Image
                await ((SignalRChatService)_chatService).SendAttachmentAsync(SelectedRoom.RoomId, result.First().ImagePath, "pdf");
            }
        }

        [RelayCommand]
        private async Task DeleteMessage(ChatMessageModel message)
        {
            string action = await App.Current.MainPage.DisplayActionSheet("Delete Message?", "Cancel", null, "Delete for Me", "Delete for Everyone");
            
            if (action == "Delete for Me")
                await ((SignalRChatService)_chatService).DeleteMessageAsync(message.MessageID, false);
            else if (action == "Delete for Everyone")
                await ((SignalRChatService)_chatService).DeleteMessageAsync(message.MessageID, true);
        }

        private void HandleDeletion(long id, bool everyone)
        {
            MainThread.BeginInvokeOnMainThread(() => {
                var msg = Messages.FirstOrDefault(m => m.MessageID == id);
                if (msg != null)
                {
                    if (everyone) {
                        msg.MessageText = "This message was deleted.";
                        msg.IsDeletedForEveryone = true;
                        msg.AttachmentPath = null;
                    } else {
                        Messages.Remove(msg);
                    }
                }
            });
        }
    }
}