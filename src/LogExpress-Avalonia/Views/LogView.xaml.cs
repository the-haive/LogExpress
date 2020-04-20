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
/*            
            ListBoxControl = this.FindControl<ListBox>("Lines");
            ListBoxVirtualizingStackPanel = ListBoxControl.FindVisualChildOfType<VirtualizingStackPanel>();
*/
        }

        //public VirtualizingStackPanel ListBoxVirtualizingStackPanel { get; set; }

        //public ListBox ListBoxControl { get; private set; }
    }
}
