using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace Flexy
{
    public class TailFile
    {
        public readonly ILogger Logger;
        private readonly LogFileReader _logFileReader;
        private static readonly char[] NewLineChars = {'\r', '\n'};

        public TailFile(TailFileOptions tailFileOptions)
        {
            // TODO: Concatenate the name to something as short as possible while being unique amongst any other tailfile instances running
            Logger = Log.ForContext("FilePath", tailFileOptions.File);
            Options = tailFileOptions;
        }

        public TailFileOptions Options { get; }

        public void Run()
        {
            if (Options.Position)
            {
                if (Options.Bytes)
                {
                    // TODO: Show all bytes from startPosition
                }
                else
                {
                    // Num is the position to start listing lines from.
                    LinesFrom(Options.Num);
                }
            }
            else
            {
                if (Options.Bytes)
                {
                    // TODO: Show last X bytes from end
                }
                else
                {
                    // TODO: Show last Num lines from the end of the file
                }
            }
        }

        public void LinesFrom(long startBytePos)
        {
            using var fileStream = new FileStream(Options.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, Options.ChunkSize, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream);

            reader.BaseStream.Seek(startBytePos, SeekOrigin.Begin);
            Span<char> readBuffer = new char[Options.ChunkSize];
            long lineStartPos = startBytePos == 0 ? reader.BaseStream.Position : -1;
            var currentLine = new StringBuilder();
            while (reader.Peek() > -1)
            {
                var count = reader.Read(readBuffer);
                for (var i = 0; i < count; i++)
                {
                    if (readBuffer[i].Equals('\n'))
                    {
                        if (lineStartPos != -1)
                        {
                            // We have a line
                            
                            var line = readBuffer.Slice((int) lineStartPos, i - (int) lineStartPos).Trim(NewLineChars);
                            Console.WriteLine($"buffer: {currentLine.Append(line)}");
                            currentLine.Clear();
                        }
                        else
                        {
                            Logger.Debug("Skipped the first line, which was partial. LineStartPos={lineStartPos} i={i}", lineStartPos, i);
                        }
                        lineStartPos = i+1;
                    }
                }

                if (lineStartPos > -1 && count > lineStartPos)
                {
                    // Should keep the line found so far
                    currentLine.Append(readBuffer.Slice((int) lineStartPos, count - (int) lineStartPos));
                }
            }

            if (currentLine.Length > 0)
            {
                Console.WriteLine($"   eof: {currentLine.ToString().Trim(NewLineChars)}");
            }
        }
    }
}