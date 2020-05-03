using System.Collections.ObjectModel;
using LogExpress.Models;

namespace LogExpress.Services
{
    /// <summary>
    /// Manages the filters.
    /// Loads all filters from the config on start.
    /// CRUD on all filters, persisted in the config while running the application.
    /// </summary>
    public class Filters
    {
        public ObservableCollection<Filter> GetItems() => new ObservableCollection<Filter>
        {
            new Filter
            {
                Name = "StartCrawl",
                RegExp = "Starting Crawl"
            }
        };
    }
}
