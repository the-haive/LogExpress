using System;

namespace LogExpress.Models
{
    public class FilterItem<T>
    {
        public FilterItem(T obj, int key, string name, string toolTip = null)
        {
            Object = obj;
            Key = key;
            Name = name;
            ToolTip = toolTip;
        }

        public T Object { get; }
        public int Key { get; }
        public string Name { get; }
        public string ToolTip { get; }
    }
}
