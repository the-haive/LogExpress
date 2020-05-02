using System;
using ByteSizeLib;
using JetBrains.Annotations;

namespace LogExpress.Models
{
    public class LogFileFilter
    {
        public LogFileFilter(ScopedFile scopedFile)
        {
            Name = scopedFile.Name;
            CreationTime = scopedFile.CreationTime.ToLongDateString();
            DirectoryName = scopedFile.DirectoryName;
            ScopedFile = scopedFile;
        }

        public LogFileFilter(string name)
        {
            Name = name;
        }

        [UsedImplicitly] public ScopedFile ScopedFile { get; private set; }
        [UsedImplicitly] public string CreationTime { get; private set; }

        [UsedImplicitly] public string DirectoryName { get; private set; }

        [UsedImplicitly] public string Name { get; private set; }

        [UsedImplicitly] public string Size { get; private set; }

        [UsedImplicitly]
        public string ToolTip => CreationTime == null
            ? string.Empty
            : $"Created: {CreationTime} {Environment.NewLine}Size: {Size} {Environment.NewLine}Folder: {DirectoryName}";
    }
}
