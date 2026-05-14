using StateMobile.Models;
using StateMobile.Services;

namespace StateMobile.Views
{
    public partial class PdfPreviewPage : ContentPage
    {
        private ScannedPage? _selectedPage;

        public PdfPreviewPage(ScannedDocument document)
        {
            InitializeComponent();
            BindingContext = new { Document = document };
        }

        private async void OnAddSignatureClicked(object sender, EventArgs e)
        {
            // Get the page from the button's binding context
            if (sender is Button btn && btn.BindingContext is ScannedPage page)
            {
                _selectedPage = page;
                
                // Open Signature Pad (Assuming SignaturePadView exists)
                var signaturePad = new SignaturePadView();
                signaturePad.OnSignatureSaved += (data) => 
                {
                    _selectedPage.Signature = new PageSignature 
                    { 
                        SignatureData = data,
                        X = 50, // Default position
                        Y = 50
                    };
                    
                    // Refresh UI
                    OnPropertyChanged(nameof(BindingContext));
                };

                await Navigation.PushModalAsync(signaturePad);
            }
        }
    }
}