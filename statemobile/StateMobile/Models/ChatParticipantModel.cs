using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StateMobile.Models
{
    public partial class ChatParticipantModel : ObservableObject
    {
        [ObservableProperty]
        private string userID = string.Empty;

        [ObservableProperty]
        private string fullName = string.Empty;

        [ObservableProperty]
        private string photo = string.Empty;

        [ObservableProperty]
        private bool isOnline;

        [ObservableProperty]
        private bool isAdmin;

        public string DisplayName => FullName ?? string.Empty;
        public bool ShowAdminTag => IsAdmin;
    }
}
