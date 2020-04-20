using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace LogExpress.Models
{
    public class LineItem
    {
        public static ReadOnlyObservableCollection<FileInfo> LogFiles;
        private static readonly ILogger Logger = Log.ForContext<LineItem>();

        // ReSharper disable once UnusedMember.Global
        public string Content
        {
            get
            {
                if (LogFiles?[LogFileIndex] == null)
                {
                    Logger.Error("The LogFiles helper property is not initialized");
                    return null;
                }

                return ReadLineFromFilePosition(LogFiles[LogFileIndex], Position);
            }
        }

        public long GlobalPosition => LogFiles?.Take(LogFileIndex).Sum(x => x.Length) + Position ?? 0;
        public long LineNum { get; set; }
        public int LogFileIndex { get; set; }
        public long Position { get; set; }

        private static string ReadLineFromFilePosition(FileInfo file, long position)
        {
            if (file.Length <= 0)
            {
                Logger.Error("Unable to find the file that was trying to be read.");
                return null;
            }

            using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var reader = new StreamReader(fileStream);
            reader.BaseStream.Seek(position, SeekOrigin.Begin);
            var buffer = new Span<char>(new char[1]);
            var line = new StringBuilder(300);

            while (!reader.EndOfStream)
            {
                var numRead = reader.Read(buffer);
                if (numRead == -1) continue; // End of stream
                if (buffer[0] == '\r' || buffer[0] == '\n') break;
                // TODO: Use proper encoding for creating the line
                line.Append(buffer);
            }

            // TODO: Figure out how to do line decorations (Timestamp & Level)
            return line.ToString();
        }
    }
}
