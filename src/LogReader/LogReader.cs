using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;
using Serilog.Events;

namespace Flexy
{
    /// <summary>
    /// Creates a log-reader for a given file that is:
    /// * V1:
    ///   a) Not locking the file
    ///   b) Holding a given number of lines (applicable for the position), either at:
    ///     1) A fixed position (initiated via percentage), for when a line is selected or the scrollbar is not at 100%
    ///     2) Or tailing (updating when the file changes)
    ///   c) As fast performing as possible
    ///   d) Detecting rollover (and updating)
    ///   e) Checking for file-updates every second (instead of using events, which for logfiles will be spammy)
    ///   e) Supporting a subscription system so that there is a way for anyone to get notified of changes
    /// * V2:
    ///   a) Add filter background-job(s) to fetch lines that match given criteria, as subtabs of the document. The filters should also be named and possible to reuse. Also, allow these filters to save/update in a separate file - even without viewing the file. This is  
    ///   b) Allow view to consider all the archived rollover files too, making the scrollbar longer, and somehow show the actual filename and num as context when browsing "back in time"
    /// </summary>
    public class LogReader
    {
        public readonly ILogger Logger;
        private readonly string _filePath;

        /// <summary>
        /// Creates a LogReader for the given file, at the given position and with the given no of Lines
        /// </summary>
        /// <param name="filePath">The path to the file to read</param>
        public LogReader(string filePath)
        {
            // Set the context for the logging
            Logger = Log.ForContext<LogReader>().ForContext("FilePath", filePath);
            _filePath = filePath;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fromPercentage">The position to read from in the file, 0-100 inclusive. Values outside are clamped to 0 or 100 respectively.</param>
        /// <param name="numLines">The number of lines to load</param>
        /// <returns> The Lines</returns>
        public Lines LoadSegment(int fromPercentage = 100, long numLines = 10)
        {
            // Limit to 0 to 100 (%)
            fromPercentage = Math.Min(100, Math.Max(0, fromPercentage));
            numLines = Math.Max(numLines, 0);

            // var fileSize = reader.BaseStream.Length;

            return FindLinesAfterOffset(fromPercentage, numLines);


        }

        private Lines FindLinesAfterOffset(int viewPosition, long viewSize)
        {
            Lines lines;
            using (var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {

                var reader = new StreamReader(fileStream);

                var startPositions = new List<long>();
                var offset = (long) (viewPosition / 100.0 * reader.BaseStream.Length);
                long pos = 0;

                if (viewPosition == 100)
                {
                    startPositions = FindLinesBeforeOffset(reader, offset, viewSize);
                    pos = offset;
                }
                else
                {
                    Logger.Debug("FindLinesAfterOffset> Seek: {Offset}", offset);
                    reader.BaseStream.Seek(offset, SeekOrigin.Begin);

                    var newLine = false;
                    while (startPositions.Count <= viewSize + 1)
                    {
                        if (startPositions.Count == viewSize + 1)
                        {
                            pos--;
                            break;
                        }

                        if (pos == 0 && offset == 0) startPositions.Add(pos);
                        var c = reader.BaseStream.ReadByte();
                        if (c == -1)
                        {
                            pos--;
                            break;
                        }

                        if (newLine)
                        {
                            startPositions.Add(pos);
                            newLine = false;
                            Logger.Debug("FindLinesAfterOffset> NewLine: {Position}", pos);
                        }

                        if (c == Convert.ToInt32('\n'))
                            newLine = true;
                        pos++;
                    }

                    if (startPositions.Count > 0 && pos > startPositions.Last()) startPositions.Add(pos);

                    if (offset != 0)
                    {
                        if (startPositions.Count == 0)
                        {
                            viewPosition = 100;
                            return FindLinesAfterOffset(viewPosition, viewSize);
                        }

                        if (startPositions.Count < viewSize + 1)
                        {
                            // Call method to add more lines before the first found position => FindLinesBeforeOffset(offset, lines)
                            // First we need to simulate the end of the file being at the first position we added
                            var size = viewSize - startPositions.Count;
                            var addedStartPositions = FindLinesBeforeOffset(reader, offset, size);
                            // Then we need to merge the negative positions into the startPositions collection.
                            startPositions.AddRange(addedStartPositions);
                            startPositions.Sort();
                        }
                    }
                }

                lines = LoadLines(reader, viewSize, startPositions, pos, offset);
            }

            return lines;
        }

        private List<long> FindLinesBeforeOffset(StreamReader reader, long offset, long size)
        {
            Logger.Debug("FindLinesBeforeOffset> Need {ViewSize} lines", size);

            var startPositions = new List<long>();

            long pos = -1;
            while (startPositions.Count < size + 1)
            {
                var seekPos = offset + pos;
                if (seekPos < 0)
                {
                    startPositions.Add(pos+1);
                    break;
                }
                
                Logger.Debug("FindLinesBeforeOffset> Seek: {Position}", seekPos);
                reader.BaseStream.Seek(seekPos, SeekOrigin.Begin);
                var c = reader.BaseStream.ReadByte();

                if (c == -1) break;

                if (c == Convert.ToInt32('\n'))
                {
                    var startPos = pos + 1;
                    if (!startPositions.Contains(startPos)) startPositions.Add(startPos);
                    Logger.Debug("FindLinesBeforeOffset> NewLine: {Position}", startPos);
                }
                pos--;
            }
            startPositions.Reverse();
            return startPositions;
        }

        private Lines LoadLines(StreamReader reader, long viewSize, IReadOnlyList<long> startPositions, long pos, long offset)
        {
            Logger.Debug("LoadLines> StartPositions: {StartPositions}", string.Join(",", startPositions));
            var lines = new Lines();
            var first = long.MaxValue;
            var last = long.MinValue;
            for (var i = 1; i <= startPositions.Count && i <= viewSize; i++)
            {
                if (i == startPositions.Count && i > 1) break;
                var startPos = startPositions[i - 1];
                var endPos = i < startPositions.Count ? startPositions[i] : pos;
                var length = endPos - startPos;
                var offsetStart = offset + startPos;
                first = Math.Min(first, offsetStart);
                last = Math.Max(last, offsetStart + length);
                reader.BaseStream.Seek(offsetStart, SeekOrigin.Begin);
                var lineBuffer = new char[length];
                reader.ReadBlock(lineBuffer, 0, (int) length);
                reader.DiscardBufferedData();
                var line = new string(lineBuffer);
                line = line.Trim('\r', '\n');
                lines.Add(startPos, -1, line);
                Logger.Debug("LoadLines> Seek: {OffsetStart,3}, Length: {Length,2}, Content: \"{Content}\"", offsetStart, length, line);
            }

            if (Logger.IsEnabled(LogEventLevel.Debug))
            {
                var count = last - first;
                var buffer = new char[count];

                reader.BaseStream.Seek(first, SeekOrigin.Begin);
                reader.ReadBlock(buffer, 0, (int) count);
                reader.DiscardBufferedData();

                Logger.Debug("LoadLines> Seek: {First}, Count: {Count}", first, count);

                var bufferRaw = new string(buffer).Replace("\r", "\\r").Replace("\n", "\\n");
                Logger.Debug("LoadLines> Buffer (raw):\r\n\"{BufferRaw}\"" + Environment.NewLine, bufferRaw);

                Logger.Debug("LoadLines> Buffer (string):\r\n\"{Buffer}\"" + Environment.NewLine, new string(buffer));

                var lineData = string.Join("\"\n\"", lines.Select(l => l.Content));
                Logger.Debug("LoadLines> Lines:\r\n\"{Lines}\"", lineData);
            }

            return lines;
        }
    }
}