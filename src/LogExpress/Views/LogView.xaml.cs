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
            LinesCtrl = this.FindControl<ListBox>("Lines");
            FileFilterCtrl = this.FindControl<ComboBox>("FileFilter");
            LevelFilterCtrl = this.FindControl<ComboBox>("LevelFilter");
            SearchQueryCtrl = this.FindControl<TextBox>("SearchQuery");
        }

        public Image LogLevelMap { get; set; }
        public ListBox LinesCtrl { get; private set; }
        public ComboBox FileFilterCtrl { get; set; }
        public ComboBox LevelFilterCtrl { get; set; }
        public TextBox SearchQueryCtrl { get; set; }
    }
}
