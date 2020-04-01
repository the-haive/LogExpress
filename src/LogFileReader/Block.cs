using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace Flexy
{
    internal class Block
    {
        private static readonly ILogger Logger = Log.ForContext<LogFileReader>();

        public byte[] Data;

        public Block(string filePath, long blockId, int blockSize, long fileInfoLength)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, blockSize, FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream);

            // Actual start of the block
            var offset = blockId * blockSize;

            // Detect how much to read, beyond the end of the normal block
            var offsetEnd = Math.Min(offset + blockSize, fileInfoLength);
            reader.BaseStream.Seek(offsetEnd, SeekOrigin.Begin);
            while (reader.Peek() > -1)
            {
                var i = reader.Read();
                if (i == Convert.ToInt32('\n')) break;
            }
            offsetEnd = reader.BaseStream.Position;
            var length = offsetEnd - offset;

            // Assign Data a properly sized array, and hand it to a MemoryStream
            Data = new byte[length];
            using var memStream = new MemoryStream(Data);

            // Set the originating stream at the beginning of the block to be fetched
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            
            // Copy from the file-stream to the memory-stream, the full length up until the last newline/eof
            CopyStream(reader.BaseStream, memStream, length);
        }

        public IReadOnlyList<Line> Lines()
        {
            // Create a stream on the Byte[]
            using var dataReader = new MemoryStream(Data);

            dataReader.Seek(0, SeekOrigin.Begin);
            var c = new byte[1];
            var lineBytes = new List<byte>(400);
            var lines = new List<Line>(500);
            int count;
            do
            {
                count = dataReader.Read(c, 0, 1);
                if (count == 0 || Encoding.UTF8.GetChars(c)[0] == '\n')
                {
                    if (count != 0 || lineBytes.Count != 0)
                    {
                        lines.Add(new Line()
                        {
                            Pos = dataReader.Position - 1,
                            Content = Encoding.UTF8.GetString(lineBytes.ToArray()).Trim('\r', '\n')
                        });
                    }
                    lineBytes.Clear();
                }
                else
                {
                    lineBytes.AddRange(c);
                }
            } while (count > 0);

            return lines;
        }

        private static void CopyStream(Stream input, Stream output, long bytes)
        {
            if (bytes == 0) return;
            var buffer = new byte[64*1024];
            int read;
            while (bytes > 0 && (read = input.Read(buffer, 0, (int) Math.Min(buffer.Length, bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }
    }
}