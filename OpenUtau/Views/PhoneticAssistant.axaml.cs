using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class PhoneticAssistant : AppWindowBase {
        PhoneticAssistantViewModel viewModel;
        public PhoneticAssistant() {
            InitializeComponent();
            DataContext = viewModel = new PhoneticAssistantViewModel();
        }

        public void OnCopy(object sender, RoutedEventArgs e) {
            TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(viewModel.Phonemes);
        }
    }
}
