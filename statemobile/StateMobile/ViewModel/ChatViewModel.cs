using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StateMobile.Models;
using System.Collections.ObjectModel;

namespace StateMobile.ViewModel
{
    public partial class ChatViewModel : BasedViewModel
    {
        // ... existing properties ...

        [RelayCommand]
        private async Task SendMessage(ChatMessageModel message = null)
        {
            var msgToSend = message ?? new ChatMessageModel 
            { 
                RoomID = CurrentRoom.RoomID, 
                SenderID = _currentUserId, 
                MessageText = MessageText,
                IsFailed = false 
            };

            if (message == null) Messages.Add(msgToSend);
            msgToSend.IsSending = true;
            msgToSend.IsFailed = false;

            try 
            {
                // Simulate API Call
                // var result = await _apiService.PostAsync("api/chat/send", msgToSend);
                msgToSend.IsSending = false;
            }
            catch (Exception)
            {
                msgToSend.IsSending = false;
                msgToSend.IsFailed = true;
                await Shell.Current.DisplayAlert("Error", "Failed to send message. Tap to retry.", "OK");
            }
        }

        [RelayCommand]
        private async Task RetryMessage(ChatMessageModel message)
        {
            await SendMessage(message);
        }

        [RelayCommand]
        private async Task DeleteMessage(ChatMessageModel message)
        {
            var action = await Shell.Current.DisplayActionSheet("Delete Message", "Cancel", null, "Delete for Me", "Delete for Everyone");
            if (action == "Cancel") return;

            bool forEveryone = action == "Delete for Everyone";
            // Call API to delete
            // await _apiService.PostAsync("api/chat/delete", new { MessageID = message.MessageID, ForEveryone = forEveryone });
        }
    }
}