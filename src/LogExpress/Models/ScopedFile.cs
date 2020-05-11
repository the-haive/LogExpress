using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ByteSizeLib;
using LogExpress.Services;
using Serilog;
using UtfUnknown;
using Encoding = System.Text.Encoding;

namespace LogExpress.Models
{
    public class ScopedFile
    {
        private static readonly ILogger Logger = Log.ForContext<ScopedFile>();
        private DateTime? _startDate;
        private DateTime? _endDate;
        private Layout _layout;
        private Encoding _encoding;

        public ScopedFile(string file, string basePath, Layout layout = null)
        {
            Layout = layout;
            BasePath = basePath;
            var fi = new FileInfo(file);
            CreationTime = fi.CreationTime;
            Name = fi.Name;
            FullName = fi.FullName;
            Length = (uint) fi.Length;
            DirectoryName = fi.DirectoryName;
            LinesListCreationTime = (ulong) (CreationTime.Ticks / LineItem.TicksPerSec);
        }

        public Layout Layout
        {
            get => _layout;
            set
            {
                _startDate = null;
                _endDate = null;
                _layout = value;
            }
        }

        public Encoding Encoding
        {
            get
            {
                if (_encoding == null)
                {
                    var result = CharsetDetector.DetectFromFile(FullName);
                    // If we detect ASCII, then we read as UTF-8, as the log *could* contain only ASCII characters *so far*
                    // - but could end up with UTF-8 characters later. It is safe to use UTF-8 for ASCII, as ASCII is a subset of
                    // UTF-8. If the file is UTF-8 (which is not unlikely) then we are safer choosing that over ASCII.
                    _encoding = Equals(result.Detected.Encoding, Encoding.ASCII) ? Encoding.UTF8 : result.Detected.Encoding;
                    var encodingName = Equals(result.Detected.Encoding, Encoding.ASCII) ? "UTF-8 (ASCII)" : result.Detected.EncodingName;
                    Logger.Debug("Set encoding '{Encoding}' (confidence: {Confidence}%) for file {File}",
                        encodingName, (result.Detected.Confidence*100).ToString("F2"), FullName);
                }
                return _encoding;
            }
        }

        public string BasePath { get; set; }
        public DateTime CreationTime { get; }
        public string Name{ get; }
        public string FullName{ get; }
        public string RelativeFullName => $"...{FullName.Remove(0, BasePath.Length)}";
        public string DirectoryName { get; }
        public string RelativeDirectoryName => $"...{DirectoryName.Remove(0, BasePath.Length)}";
        public uint Length { get; set; }
        public string LengthHuman => ByteSize.FromBytes(Length).ToString();
        public ulong LinesListCreationTime{ get; }

        public DateTime StartDate
        {
            get
            {
                if (_startDate.HasValue) return _startDate.Value;

                if (Layout == null) return DateTime.MinValue;

                if (Length <= 0) return DateTime.MinValue;

                // Try to fetch the date from the first log-entry
                using var fileStream = new FileStream(FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream, Encoding);

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
                    _startDate = GetTimestamp(buffer);
                    if (!_startDate.HasValue)
                    {
                        Logger.Warning("The log-file's first timestamp could not be parsed. Set to the file CreationTime. File={File}", FullName);
                    }
                }

                _startDate ??= DateTime.MinValue;

                return _startDate.Value;
            }
        }

        public DateTime EndDate
        {
            get
            {
                if (_endDate.HasValue) return _endDate.Value;

                if (Layout == null) return DateTime.MaxValue;

                if (Length <= 0) return DateTime.MaxValue;

                // Try to fetch the date from the last log-entry
                using var fileStream = new FileStream(FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream, Encoding);

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
                        endDate = GetTimestamp(buffer);
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

        public DateTime? GetTimestamp(Span<char> buffer)
        {
            if (string.IsNullOrWhiteSpace(Layout.TimestampFormat))
            {
                if (DateTime.TryParse(buffer, out var dateTime))
                {
                    return dateTime;
                }
            }
            else
            {
                if (DateTime.TryParseExact(buffer, Layout.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
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
            uint lastLength = 0,
            int lastLineNumber = 1,
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
/*
                var lineNum = previousFileInfo == null ? 1 : lastLineNumber;
                var filePosition = (uint) (previousFileInfo?.Length ?? 0);
                var lastNewLinePos = previousFileInfo == null ? 0 : lastPosition;
*/
                var lineNum = lastLineNumber;
                var filePosition = lastLength;
                var lastNewLinePos = lastPosition;
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
