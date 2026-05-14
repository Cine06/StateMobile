using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StateMobile.Services
{
    public interface IBadgeService
    {
        int UnreadNotificationCount { get; set; }
        int UnreadChatCount { get; set; }
        event EventHandler CountChanged;
        void SetCounts(int notifications, int chats);
        void IncrementNotification();
        void DecrementNotification();
        void IncrementChat();
        void DecrementChat();
    }

    public class BadgeService : IBadgeService, INotifyPropertyChanged
    {
        private int _unreadNotificationCount;
        public int UnreadNotificationCount
        {
            get => _unreadNotificationCount;
            set
            {
                if (_unreadNotificationCount != value)
                {
                    _unreadNotificationCount = Math.Max(0, value);
                    OnPropertyChanged();
                    CountChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private int _unreadChatCount;
        public int UnreadChatCount
        {
            get => _unreadChatCount;
            set
            {
                if (_unreadChatCount != value)
                {
                    _unreadChatCount = Math.Max(0, value);
                    OnPropertyChanged();
                    CountChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler? CountChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetCounts(int notifications, int chats)
        {
            UnreadNotificationCount = notifications;
            UnreadChatCount = chats;
        }

        public void IncrementNotification() => UnreadNotificationCount++;
        public void DecrementNotification() => UnreadNotificationCount--;

        public void IncrementChat() => UnreadChatCount++;
        public void DecrementChat() => UnreadChatCount--;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
