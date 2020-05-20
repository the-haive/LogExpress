using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;

namespace LogExpress.Views
{
    public class ConfigureSeverityView : Window
    {
        public ConfigureSeverityView()
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
