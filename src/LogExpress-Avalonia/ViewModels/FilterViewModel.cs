using System.Collections.ObjectModel;
using LogExpress.Models;
using LogExpress.Services;

namespace LogExpress.ViewModels
{
    public class FilterViewModel : ViewModelBase
    {
        public FilterViewModel()
        {
            var filters = new Filters();
            Items = filters.GetItems();
        }

        public ObservableCollection<Filter> Items { get; }

        public void ExecuteFilter()
        {
        }

        public void ToggleTail()
        {
        }

        public void SaveFilter()
        {
        }
        public void LoadFilter()
        {
        }
    }
}
