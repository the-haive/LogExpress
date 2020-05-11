using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LogExpress.Views
{
    public class AboutView : Window
    {
        private Button _aboutCtrl;

        public AboutView()
        {
            this.InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _aboutCtrl = this.FindControl<Button>("AboutOk");
            _aboutCtrl.Click += OnAboutCtrlOnClick;
        }

        private void OnAboutCtrlOnClick(object sender, RoutedEventArgs args)
        {
            _aboutCtrl.Click -= OnAboutCtrlOnClick;
            Close();
        }
    }
}
