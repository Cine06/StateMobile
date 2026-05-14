using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace StateMobile.ViewModel
{
    public partial class BasedViewModel : ObservableObject
    {
        [ObservableProperty]
        public bool _isBusy;
        [ObservableProperty]
        public string _title;

    }
}
