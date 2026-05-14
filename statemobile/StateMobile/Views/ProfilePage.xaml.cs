using StateMobile.Services;

namespace StateMobile.Views
{
    public partial class ProfilePage : BasePage
    {
        private readonly IDatabaseService? _databaseService;

        public ProfilePage(IUserSessionService sessionService, IDatabaseService databaseService) : base(sessionService)
        {
            InitializeComponent();
            _databaseService = databaseService;
            BindingContext = this;
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged(nameof(IsEditing));
                    OnPropertyChanged(nameof(IsNotEditing));
                }
            }
        }

        public bool IsNotEditing => !IsEditing;

        private string? _originalNickname;
        private string? _originalMobile;

        private void OnEditClicked(object sender, EventArgs e)
        {
            _originalNickname = Nickname;
            _originalMobile = Mobile;
            IsEditing = true;
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            Nickname = _originalNickname ?? string.Empty;
            Mobile = _originalMobile ?? string.Empty;
            IsEditing = false;
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var aisNo = UserAISNo;
            if (string.IsNullOrEmpty(aisNo))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", "User not found.", "OK");
                });
                return;
            }

            if (_databaseService == null)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", "Database service is not available.", "OK");
                });
                return;
            }

            var result = await _databaseService.UpdateProfileInfoAsync(aisNo, Nickname, Mobile);
            if (result.Success)
            {
                var sessionService = Handler?.MauiContext?.Services.GetService<IUserSessionService>();
                if (sessionService?.CurrentUser != null)
                {
                    sessionService.CurrentUser.Nickname = Nickname;
                    sessionService.CurrentUser.Mobile = Mobile;
                    await sessionService.SetUserAsync(sessionService.CurrentUser);
                }

                IsEditing = false;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Success", "Profile updated successfully.", "OK");
                });
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Update Failed", result.Message, "OK");
                });
            }
        }
    }
}
