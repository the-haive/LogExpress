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

            //LogLevelMapImageControl = this.FindControl<Image>("SeverityMap");
            SeverityMap = this.FindControl<Image>("SeverityMap");
            LinesCtrl = this.FindControl<ListBox>("Lines");
            FileFilterCtrl = this.FindControl<ComboBox>("FileFilter");
            SeverityFilterCtrl = this.FindControl<ComboBox>("SeverityFilter");
            SearchQueryCtrl = this.FindControl<TextBox>("SearchQuery");
        }

        public Image SeverityMap { get; set; }
        public ListBox LinesCtrl { get; private set; }
        public ComboBox FileFilterCtrl { get; set; }
        public ComboBox SeverityFilterCtrl { get; set; }
        public TextBox SearchQueryCtrl { get; set; }
    }
}
