using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogExpress.ViewModels;

namespace LogExpress.Views
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            ThemeControl = this.FindControl<CheckBox>("Theme");
            LogView = this.FindControl<LogView>("LogPanel");

            this.DataContext = new MainWindowViewModel(this);

        }

        public CheckBox ThemeControl { get; set; }
        public LogView LogView { get; set; }
    }
}