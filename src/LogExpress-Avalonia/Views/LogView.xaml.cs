using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LogExpress.Views
{
    public class LogView : UserControl
    {
        public LogView()
        {
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            //LogLevelMapImageControl = this.FindControl<Image>("LogLevelMap");
            LogLevelMap = this.FindControl<Image>("LogLevelMap");

            ListBoxControl = this.FindControl<ListBox>("Lines");
        }

        public Image LogLevelMap { get; set; }
        public ListBox ListBoxControl { get; private set; }
    }
}
