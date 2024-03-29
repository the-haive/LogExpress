﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ByteSizeLib;
using LogExpress.Services;
using LogExpress.ViewModels;
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
        private Encoding _encoding;

        public ScopedFile(string file, string basePath, TimestampSettings timestampSettings = null, SeveritySettings severitySettings = null)
        {
            TimestampSettings = timestampSettings;
            SeveritySettings = severitySettings;
            BasePath = basePath;
            var fi = new FileInfo(file);
            CreationTime = fi.CreationTime;
            Name = fi.Name;
            FullName = fi.FullName;
            Length = (uint) fi.Length;
            DirectoryName = fi.DirectoryName;
            LinesListCreationTime = (ulong) (CreationTime.Ticks / LineItem.TicksPerSec);
        }

        public TimestampSettings TimestampSettings { get; set; }
        public SeveritySettings SeveritySettings { get; set; }

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

                if (TimestampSettings == null) return DateTime.MinValue;

                if (Length <= 0) return DateTime.MinValue;

                // Try to fetch the date from the first log-entry
                using var fileStream = new FileStream(FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream, Encoding);

                var buffer = new Span<char>(new char[TimestampSettings.TimestampLength]);

                reader.BaseStream.Seek(TimestampSettings.TimestampStart, SeekOrigin.Begin);
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

                if (TimestampSettings == null) return DateTime.MaxValue;

                if (Length <= 0) return DateTime.MaxValue;

                // Try to fetch the date from the last log-entry
                using var fileStream = new FileStream(FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream, Encoding);

                var buffer = new Span<char>(new char[TimestampSettings.TimestampLength]);

                long startPos = -1 * TimestampSettings.TimestampLength;
                
                DateTime? endDate = null;
                
                while (true)
                {
                    // Find position for last NewLine
                    var newLinePos = FindLastNewLineInFile(startPos, reader, Length);
                    // 
                    if (Length - (-1*newLinePos) < TimestampSettings.TimestampLength) break;

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

        public DateTime? GetTimestamp(Span<char> buffer)
        {
            if (string.IsNullOrWhiteSpace(TimestampSettings.TimestampFormat))
            {
                if (DateTime.TryParse(buffer, out var dateTime))
                {
                    return dateTime;
                }
            }
            else
            {
                if (DateTime.TryParseExact(buffer, TimestampSettings.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                {
                    return dateTime;
                }
            }

            return null;
        }

        public void ResetStartAndEndDates()
        {
            _startDate = null;
            _endDate = null;
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

        /// <summary>
        /// Generate an Observable collection of LineItems
        /// </summary>
        /// <param name="newLines">The newLines collection to store found newlines in</param>
        /// <param name="reader">The reader instance to use when finding the lines</param>
        /// <param name="file">The scoped file to operate on</param>
        /// <param name="lastLength">The length for the previous time it was called</param>
        /// <param name="lastLineNumber">The lineNumber for the previous time it was called</param>
        /// <param name="lastPosition">The position for the previous time it was called</param>
        /// <param name="maxLinesToRead">When to stop reading newlines (disabled if 0, default 0)</param>
        public static void ReadFileLinePositions(ObservableCollection<LineItem> newLines,
            StreamReader reader,
            ScopedFile file,
            uint lastLength = 0,
            int lastLineNumber = 1,
            uint lastPosition = 0,
            uint maxLinesToRead = 0
        )
        {
            if (file.Length <= 0) return;

            var buffer = new Span<char>(new char[1]);

            var lineNum = lastLineNumber;
            var filePosition = lastLength;
            var lastNewLinePos = lastPosition;
            reader.BaseStream.Seek(filePosition, SeekOrigin.Begin);
            reader.DiscardBufferedData();
            while (!reader.EndOfStream)
            {
                var numRead = reader.Read(buffer);
                if (numRead == -1) continue; // End of stream
                filePosition++;
                if (buffer[0] == '\n')
                {
                    newLines.Add(new LineItem(file, lineNum, lastNewLinePos));
                    lastNewLinePos = filePosition;
                    lineNum += 1;
                    if (maxLinesToRead > 0 && lineNum >= maxLinesToRead) break;
                }
            }

            if (filePosition >= lastNewLinePos)
                newLines.Add(new LineItem(file, lineNum, lastNewLinePos));
        }
        
        /// <summary>
        /// Find the logLevel in the logfile line
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="severitySettings"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        internal static KeyValuePair<byte, string> ReadFileLineSeverity(StreamReader reader, SeveritySettings severitySettings, uint position)
        {
            var buffer = new Span<char>(new char[severitySettings.MaxSeverityNameLength]);

            reader.BaseStream.Seek(position + severitySettings.SeverityStart, SeekOrigin.Begin);
            reader.DiscardBufferedData();
            var numRead = reader.Read(buffer);
            
            if (numRead == -1) return new KeyValuePair<byte, string>(0, buffer.ToString());
            
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var severity in severitySettings.Severities ?? new Dictionary<byte, string>())
            {
                if (buffer.StartsWith(severity.Value))
                {
                    return severity;
                }
            }
            return new KeyValuePair<byte, string>(0, buffer.ToString());
        }
    }


/*    public class Layout
    {
        private Dictionary<byte, string> _severities;
        private int _maxSeverityNameLength;
        public int TimestampStart { get; set; }
        public int TimestampLength { get; set; }
        public string TimestampFormat { get; set; }
        public int SeverityStart { get; set; }

        public Dictionary<byte, string> Severities
        {
            get => _severities;
            set => _severities = value.Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        public int MaxSeverityNameLength
        {
            get
            {
                if (_maxSeverityNameLength == 0)
                {
                    _maxSeverityNameLength = Severities.OrderByDescending(s => s.Value.Length).First().Value.Length;
                }

                return _maxSeverityNameLength;
            }
        }
    }
*/}
