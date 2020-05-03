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
            FileFilterCtrl = this.FindControl<ComboBox>("FileFilter");
            LevelFilterCtrl = this.FindControl<ComboBox>("LevelFilter");
            YearFilterCtrl = this.FindControl<ComboBox>("YearFilter");
            MonthFilterCtrl = this.FindControl<ComboBox>("MonthFilter");
        }

        public Image LogLevelMap { get; set; }
        public ListBox ListBoxControl { get; private set; }
        public ComboBox FileFilterCtrl { get; set; }
        public ComboBox LevelFilterCtrl { get; set; }
        public ComboBox YearFilterCtrl { get; set; }
        public ComboBox MonthFilterCtrl { get; set; }
    }
}
