using StateMobile.Models;

namespace StateMobile.Views
{
    public partial class GroupSettingsPage : ContentPage
    {
        public GroupSettingsPage(ChatRoomModel room)
        {
            InitializeComponent();
            settingsView.SetRoom(room);
            settingsView.OnClosed += async (s, e) => await Navigation.PopAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            settingsView.Cleanup();
        }
    }
}
