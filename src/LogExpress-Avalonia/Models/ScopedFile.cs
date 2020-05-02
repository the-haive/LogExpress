using System;
using System.IO;
using ByteSizeLib;

namespace LogExpress.Models
{
    public class ScopedFile
    {
        public ScopedFile(string file, string basePath)
        {
            BasePath = basePath;
            var fi = new FileInfo(file);
            CreationTime = fi.CreationTime;
            Name = fi.Name;
            FullName = fi.FullName;
            Length = fi.Length;
            DirectoryName = fi.DirectoryName;
            LinesListCreationTime = (ulong) (CreationTime.Ticks / LineItem.TicksPerSec);
        }

        public string BasePath { get; set; }
        public DateTime CreationTime { get; }
        public string Name{ get; }
        public string FullName{ get; }
        public string RelativeFullName => $"...{FullName.Remove(0, BasePath.Length)}";
        public string DirectoryName { get; }
        public string RelativeDirectoryName => $"...{DirectoryName.Remove(0, BasePath.Length)}";
        public long Length { get; set; }
        public string LengthHuman => ByteSize.FromBytes(Length).ToString();
        public ulong LinesListCreationTime{ get; }
    }
}
