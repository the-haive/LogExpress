using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

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
