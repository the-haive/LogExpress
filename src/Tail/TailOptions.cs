using System.Collections.Generic;

namespace Flexy
{
    public abstract class BaseOptions
    {
        public bool Bytes { get; set; }
        public int ChunkSize { get; set; }
        public bool Follow { get; set; }
        public long Num { get; set; }
        public bool Position { get; set; }
        public bool Quiet { get; set; }
        public int Sleep { get; set; }
        public int Verbosity { get; set; }
    }

    public class TailFileOptions : BaseOptions
    {
        public TailFileOptions(BaseOptions t, string file, string displayName)
        {
            Bytes = t.Bytes;
            ChunkSize = t.ChunkSize;
            Follow = t.Follow;
            Num = t.Num;
            Position = t.Position;
            Quiet = t.Quiet;
            Sleep = t.Sleep;
            Verbosity = t.Verbosity;
            File = file;
            DisplayName = displayName;
        }

        public string DisplayName { get; set; }

        public string File { get; set; }
    }

    public class TailOptions : BaseOptions
    {
        public List<string> Files { get; set; }
    }
}