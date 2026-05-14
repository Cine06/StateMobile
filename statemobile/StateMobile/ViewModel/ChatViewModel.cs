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
        private readonly string _currentUserId;

        [ObservableProperty] private string _messageText;
        [ObservableProperty] private ChatRoomModel _currentRoom;
        public ObservableCollection<ChatMessageModel> Messages { get; } = new();

        public ChatViewModel(IChatService chatService)
        {
            _chatService = chatService;
            _currentUserId = Preferences.Get("UserId", "");
        }

        [RelayCommand]
        private async Task SendImage(bool fromCamera)
        {
            FileResult photo = fromCamera 
                ? await MediaPicker.Default.CapturePhotoAsync() 
                : await MediaPicker.Default.PickPhotoAsync();

            if (photo == null) return;

            // 1. Compress Image to prevent "Payload Too Large" or DB errors
            using var stream = await photo.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            byte[] data = memoryStream.ToArray();

            // Simple compression logic (if > 1MB, you'd ideally resize here)
            var attachment = new AttachmentModel
            {
                FileName = photo.FileName,
                FileType = photo.ContentType,
                Data = data // In a real app, use a resizing library here
            };

            await SendMessageInternal(string.Empty, attachment);
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(MessageText)) return;
            var text = MessageText;
            MessageText = string.Empty;
            await SendMessageInternal(text, null);
        }

        private async Task SendMessageInternal(string text, AttachmentModel attachment)
        {
            var message = new ChatMessageModel
            {
                RoomID = CurrentRoom.RoomID,
                SenderID = _currentUserId,
                MessageText = text,
                Attachment = attachment,
                Timestamp = DateTime.Now
            };

            Messages.Add(message);
            message.IsSending = true;

            try 
            {
                // Use your API Service to POST to "api/chat/send"
                // If it fails, set message.IsFailed = true
                message.IsSending = false;
            }
            catch (Exception ex)
            {
                message.IsSending = false;
                message.IsFailed = true;
                Console.WriteLine($"[Chat] Send failed: {ex.Message}");
            }
        }
    }
}