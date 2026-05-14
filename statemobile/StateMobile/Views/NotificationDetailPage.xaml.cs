using Microsoft.Maui.Controls;
using StateMobile.Models;

namespace StateMobile.Views
{
    public partial class NotificationDetailPage : ContentPage
    {
        public NotificationDetailPage(NotificationModel notification)
        {
            InitializeComponent();
            BindingContext = notification;
        }
    }
}
