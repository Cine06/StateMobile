using StateMobile.Services;

namespace StateMobile.Views
{
    public partial class ChangePasswordPage : BasePage
    {
        private string _currentPassword = "";
        public string CurrentPassword
        {
            get => _currentPassword;
            set { _currentPassword = value; OnPropertyChanged(); }
        }

        private string _newPassword = "";
        public string NewPassword
        {
            get => _newPassword;
            set { _newPassword = value; OnPropertyChanged(); }
        }

        private string _confirmPassword = "";
        public string ConfirmPassword
        {
            get => _confirmPassword;
            set { _confirmPassword = value; OnPropertyChanged(); }
        }

        private readonly IDatabaseService? _databaseService;

        public ChangePasswordPage(IUserSessionService sessionService, IDatabaseService databaseService) : base(sessionService)
        {
            InitializeComponent();
            _databaseService = databaseService;
            BindingContext = this;
        }

        private async void OnUpdatePasswordClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentPassword) ||
                string.IsNullOrWhiteSpace(NewPassword) ||
                string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", "Please fill in all fields.", "OK");
                });
                return;
            }

            if (NewPassword != ConfirmPassword)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", "New passwords do not match.", "OK");
                });
                return;
            }

            if (NewPassword.Length < 8)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", "Password must be at least 8 characters long.", "OK");
                });
                return;
            }

            if (UserID == null || _databaseService == null)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", "User session or database service is not available.", "OK");
                });
                return;
            }

            var result = await _databaseService.ChangePasswordAsync(UserID, CurrentPassword, NewPassword);

            if (result.Success)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Success", "Password updated successfully!", "OK");
                    await Navigation.PopAsync();
                });
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", result.Message, "OK");
                });
            }
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
