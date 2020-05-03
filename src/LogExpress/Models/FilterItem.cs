namespace LogExpress.Models
{
    public class FilterItem
    {
        public FilterItem(int key, string name, string toolTip = null)
        {
            Key = key;
            Name = name;
            ToolTip = toolTip;
        }

        public int Key { get; }
        public string Name { get; }
        public string ToolTip { get; }
    }
}
