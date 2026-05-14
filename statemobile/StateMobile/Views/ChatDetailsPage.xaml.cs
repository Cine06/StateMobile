using StateMobile.ViewModel;
using StateMobile.Models;

namespace StateMobile.Views
{
    public partial class ChatDetailsPage : ContentPage
    {
        public ChatDetailsPage(ChatViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
            
            // Auto-scroll to bottom when new messages arrive
            viewModel.Messages.CollectionChanged += (s, e) => 
            {
                if (e.NewItems != null && MessagesList.ItemsSource != null)
                {
                    var lastItem = viewModel.Messages.LastOrDefault();
                    if (lastItem != null)
                    {
                        MessagesList.ScrollTo(lastItem, animate: true);
                    }
                }
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (BindingContext is ChatViewModel vm && vm.SelectedRoom != null)
            {
                await vm.LoadMessagesCommand.ExecuteAsync(null);
                // Scroll to bottom on initial load
                if (vm.Messages.Count > 0)
                {
                    MessagesList.ScrollTo(vm.Messages.Last(), animate: false);
                }
            }
        }
    }
}