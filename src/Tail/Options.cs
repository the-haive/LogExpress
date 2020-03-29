using System.Collections.Generic;
using System.IO;

namespace Flexy
{
    public class Options
    {
        public bool Follow { get; set; }
        public long Num { get; set; }
        public bool Position { get; set; }
        public bool Bytes { get; set; }
        public int Verbosity { get; set; }
        public bool Quiet { get; set; }
        public int Sleep { get; set; }
        public long ChunkSize { get; set; }
        public IReadOnlyList<FileSystemInfo> Files { get; set; }
    }
}