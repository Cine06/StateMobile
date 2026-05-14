using StateMobile.Models;
using StateMobile.ViewModels;
using System.Diagnostics;

namespace StateMobile.Views;

public partial class NotificationPage : ContentPage
{
    public NotificationPage(NotificationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel; 
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var vm = BindingContext as NotificationViewModel;
            if (vm != null)
            {
                await vm.LoadNotificationsAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ NotificationPage OnAppearing Error: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
    }
}