using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using LogExpress.Models;

namespace LogExpress.ViewModels
{
    public class TodoListViewModel : ViewModelBase
    {
        public TodoListViewModel(IEnumerable<TodoItem> items)
        {
            Items = new ObservableCollection<TodoItem>(items);
        }

        public ObservableCollection<TodoItem> Items { get; }
    }
}
