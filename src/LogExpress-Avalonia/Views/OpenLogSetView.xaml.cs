using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml;
using LogExpress.ViewModels;

namespace LogExpress.Views
{
    public class OpenLogSetView : Window
    {
        public OpenLogSetView()
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
