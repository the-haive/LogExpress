using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LogExpress.Views
{
    public class ConfigureTimestampView : Window
    {
        public ConfigureTimestampView()
        {
            this.InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
