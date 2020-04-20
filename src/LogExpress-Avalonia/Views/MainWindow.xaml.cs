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

            DarkThemeControl = this.FindControl<CheckBox>("DarkTheme");

            this.DataContext = new MainWindowViewModel(this);

        }

        public CheckBox DarkThemeControl { get; set; }
    }
}