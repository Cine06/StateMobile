using StateMobile.Services;

namespace StateMobile.Views
{
    public partial class HomePage : BasePage
    {
        private bool _didLogFirstRender;

        public HomePage(IUserSessionService sessionService) : base(sessionService)
        {
            InitializeComponent();
            BindingContext = this;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            if (_didLogFirstRender)
            {
                return;
            }

            _didLogFirstRender = true;
            StartupTimingLogger.Mark("home.rendered");
            StartupTimingLogger.PrintSummaryIfAvailable();
        }

        private async void OnProjectProfileClicked(object sender, EventArgs e)
        {
            var page = Handler?.MauiContext?.Services.GetService<ProjectProfilePage>();
            if (page != null)
            {
                await Navigation.PushAsync(page);
            }
        }
    }
}