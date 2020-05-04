using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using ByteSizeLib;
using LogExpress.Services;
using Serilog;
using SixLabors.Memory;

namespace LogExpress.Models
{
    public class ScopedFile
    {
        private static readonly ILogger Logger = Log.ForContext<ScopedFile>();
        private DateTime? _startDate;
        private DateTime? _endDate;

        public ScopedFile(string file, string basePath, Layout layout = null)
        {
            Layout = layout;
            BasePath = basePath;
            var fi = new FileInfo(file);
            CreationTime = fi.CreationTime;
            Name = fi.Name;
            FullName = fi.FullName;
            Length = fi.Length;
            DirectoryName = fi.DirectoryName;
            LinesListCreationTime = (ulong) (CreationTime.Ticks / LineItem.TicksPerSec);
        }

        public Layout Layout { get; set; }
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

        public DateTime StartDate
        {
            get
            {
                if (_startDate.HasValue) return _startDate.Value;

                if (Layout == null) return CreationTime;

                if (Length <= 0)
                {
                    _startDate = CreationTime;
                    return _startDate.Value;
                }

                // Try to fetch the date from the first log-entry
                using var fileStream = new FileStream(FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream);

                var buffer = new Span<char>(new char[Layout.TimestampLength]);

                reader.BaseStream.Seek(Layout.TimestampStart - 1, SeekOrigin.Begin);
                var numRead = reader.Read(buffer);
                if (numRead == -1)
                {
                    Logger.Warning(
                        "The log-file does not start with an entry that has a timestamp. Set to the file CreationTime. File={File}", FullName);
                }
                else
                {
                    _startDate = GetTimestamp(Layout.TimestampFormat, buffer);
                    if (!_startDate.HasValue)
                    {
                        Logger.Warning("The log-file's first timestamp could not be parsed. Set to the file CreationTime. File={File}", FullName);
                    }
                }

                _startDate ??= CreationTime;

                return _startDate.Value;
            }
        }

        public DateTime EndDate
        {
            get
            {
                if (_endDate.HasValue) return _endDate.Value;

                if (Layout == null) return DateTime.MaxValue;

                using var fileStream = new FileStream(FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream);

                var buffer = new Span<char>(new char[Layout.TimestampLength]);

                long startPos = -1 * Layout.TimestampLength;
                
                DateTime? endDate = null;
                
                while (true)
                {
                    // Find position for last NewLine
                    var newLinePos = FindLastNewLineInFile(startPos, reader, Length);
                    // 
                    if (Length - (-1*newLinePos) < Layout.TimestampLength) break;

                    // Get EndDate
                    reader.BaseStream.Seek(newLinePos, SeekOrigin.End);
                    reader.DiscardBufferedData();
                    var numRead = reader.Read(buffer);
                    if (numRead != -1)
                    {
                        endDate = GetTimestamp(Layout.TimestampFormat, buffer);
                        if (!endDate.HasValue)
                        {
                            Logger.Warning("The log-file's last timestamp could not be parsed. Set to MaxValue. File={File}", FullName);
                            endDate = DateTime.MaxValue;
                        }
                        break;
                    }

                    // Directs FindLastNewLine so that it tries to find the previous NewLine
                    startPos = newLinePos - 1;
                }

                if (!endDate.HasValue)
                {
                    Logger.Warning("Could not find the last timestamp in the file. File={File}", FullName);
                    _endDate = DateTime.MaxValue;
                }
                else
                {
                    _endDate = endDate;
                }

                return _endDate.Value;
            }
        }

        public static string SampleSeverities
        {
            get { return ""; }
        }

        private static DateTime? GetTimestamp(string timestampFormat, Span<char> buffer)
        {
            if (string.IsNullOrWhiteSpace(timestampFormat))
            {
                if (DateTime.TryParse(buffer, out var dateTime))
                {
                    return dateTime;
                }
            }
            else
            {
                if (DateTime.TryParseExact(buffer, timestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                {
                    return dateTime;
                }
            }

            return null;
        }

        private static long FindLastNewLineInFile(long startPos, StreamReader reader, long fileLength)
        {
            long i = 0;
            var newLineBuffer = new Span<char>(new char[1]);
            long newLinePos = -1;
            while (true)
            {
                var offset = startPos - i++;
                if (fileLength + offset < 0) break;
                reader.BaseStream.Seek(offset, SeekOrigin.End);
                reader.DiscardBufferedData();
                reader.Read(newLineBuffer);
                if (newLineBuffer[0] == '\n')
                {
                    newLinePos = offset + 1;
                    break;
                }
            }

            return newLinePos;
        }

        // TODO: Move LogLevel-detection outside of this method
        // TODO: Simplify method to only return newlines. The caller can then iterate those and create LineItem entries
        internal static void ReadFileLinePositions(ObservableCollection<LineItem> newLines,
            StreamReader reader,
            Dictionary<WordMatcher, byte> logLevelMatchers,
            ScopedFile file,
            ScopedFile previousFileInfo = null,
            int lastLineNumber = 0,
            uint lastPosition = 0
        )
        {
            if (file.Length <= 0) return;

            FindNewLinePositions();

            // Local method to encapsulate the Span, which is not allowed in async code
            void FindNewLinePositions()
            {
                var buffer = new Span<char>(new char[1]);

                // All files with length > 0 implicitly starts with a line
                var lineNum = previousFileInfo == null ? 1 : lastLineNumber;
                var filePosition = (uint) (previousFileInfo?.Length ?? 0);
                var lastNewLinePos = previousFileInfo == null ? 0 : lastPosition;
                byte logLevel = 0;
                byte lastLogLevel = 0;
                var linePos = 0;
                reader.BaseStream.Seek(filePosition, SeekOrigin.Begin);
                reader.DiscardBufferedData();
                while (!reader.EndOfStream)
                {
                    var numRead = reader.Read(buffer);
                    if (numRead == -1) continue; // End of stream
                    filePosition++;
                    linePos++;

                    // Check if the data read so far matches a logLevel indicator
                    if (logLevel == 0 && linePos >= 25)
                        foreach (var (wordMatcher, level) in logLevelMatchers)
                        {
                            if (!wordMatcher.IsMatch(buffer[0])) continue;
                            logLevel = level;
                            break;
                        }

                    if (buffer[0] == '\n')
                    {
                        lastLogLevel = logLevel > 0 ? logLevel : lastLogLevel;
                        newLines.Add(new LineItem(file, lineNum, lastNewLinePos, lastLogLevel));
                        lastNewLinePos = filePosition;
                        lineNum += 1;
                        logLevel = 0;
                        linePos = 0;
                        foreach (var (wordMatcher, _) in logLevelMatchers) wordMatcher.Reset();
                    }
                }

                if (filePosition >= lastNewLinePos)
                    newLines.Add(new LineItem(file, lineNum, lastNewLinePos, lastLogLevel));
            }
        }

        // TODO: Create method to find all LogLevel dependent on a given start-position (from LineItem.Position, typically)
        // TODO: The method should take a Reader and the Layout in order to not have to create the reader for every read.
        /// <summary>
        /// Find the logLevel for the logfile line given by the layout
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="layout"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        internal static byte ReadFileLineSeverity(StreamReader reader, Layout layout, long position)
        {
            if (layout == null) return 0;

            var longestSeverity = layout.Severities.OrderByDescending(s => s.Value.Length).First().Value.Length;
            var buffer = new Span<char>(new char[longestSeverity]);
            reader.BaseStream.Seek(position + layout.SeverityStart - 1, SeekOrigin.Begin);
            reader.DiscardBufferedData();
            var numRead = reader.Read(buffer);
            
            if (numRead == -1) return 0;
            
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var (severityLevel, severityName) in layout.Severities)
            {
                if (buffer.StartsWith(severityName))
                {
                    return severityLevel;
                }
            }
            return 0;
        }
    }


    public class Layout
    {
        public int TimestampStart { get; set; }
        public int TimestampLength { get; set; }
        public string TimestampFormat { get; set; }
        public int SeverityStart { get; set; }
        public Dictionary<byte, string> Severities { get; set; }
    }
}
