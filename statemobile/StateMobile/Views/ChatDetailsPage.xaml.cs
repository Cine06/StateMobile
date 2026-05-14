using StateMobile.Models;
using StateMobile.Services;

namespace StateMobile.Views
{
    public partial class ChatDetailsPage : BasePage
    {
        private readonly IUserSessionService _sessionService;
        private ChatRoomModel? _room;

        public ChatDetailsPage(IUserSessionService sessionService) : base(sessionService)
        {
            InitializeComponent();
            _sessionService = sessionService;
        }

        public void SetRoom(ChatRoomModel room)
        {
            _room = room;
            detailsView.SetRoom(room);
        }

        private async void OnDetailsBackTapped(object sender, EventArgs e)
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                // ✅ Cleanup resources before navigating away
                detailsView.Cleanup();
                await Navigation.PopAsync();
            }
            finally
            {
                _isNavigating = false;
            }
        }

        private async void OnSettingsTapped(object sender, EventArgs e)
        {
            if (_room == null || _isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                await Navigation.PushAsync(new GroupSettingsPage(_room));
            }
            finally
            {
                _isNavigating = false;
            }
        }
    }
}