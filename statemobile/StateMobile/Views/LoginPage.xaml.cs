using StateMobile.ViewModel;

namespace StateMobile.Views
{
    public partial class LoginPage : ContentPage
    {
        private readonly LoginPageViewModel _viewModel;
        private bool _initializeInProgress;
        private DateTime _lastInitializeRequestUtc = DateTime.MinValue;

        public LoginPage(LoginPageViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Avoid duplicate init bursts caused by rapid page lifecycle callbacks.
            if (_initializeInProgress || (DateTime.UtcNow - _lastInitializeRequestUtc) < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _initializeInProgress = true;
            _lastInitializeRequestUtc = DateTime.UtcNow;

            // ✅ OPTIMIZED: Defer biometric check to avoid blocking UI rendering
            // This ensures the page is visible before we prompt for biometric
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await _viewModel.InitializeAsync();
                }
                finally
                {
                    _initializeInProgress = false;
                }
            });
        }
    }
}