using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace Flexy
{
    public class LogFileReader
    {
        private static readonly ILogger Logger = Log.ForContext<LogFileReader>();
        private const int DefaultUpdateFreqMs = 500;
        private const int DefaultBlockSize = 500_000;
        private readonly int _blockSize;
        private readonly Dictionary<int, Block> _blocks = new Dictionary<int, Block>();
        private readonly string _filePath;
        private int _lastUsedBlock = -1;

        public FileInfo FileInfo { get; private set; }
        public int UpdateFreqMs { get; set; }

        public LogFileReader(string filePath, int updateFreqMs = DefaultUpdateFreqMs, int blockSize = DefaultBlockSize)
        {
            if (!File.Exists(filePath)) throw new ArgumentException($"The file '{filePath}' could not be found.");
            _filePath = filePath;
            _blockSize = blockSize < 0 ? DefaultBlockSize : blockSize;
            UpdateFreqMs = updateFreqMs < 0 ? DefaultUpdateFreqMs : updateFreqMs;
            CheckFileInfo();
            Logger.Information("Created LogFileReader for '{FilePath}': FileSize={FileSize}, BlockSize={BlockSize}, UpdateFreqMs={UpdateFreqMs}. LastModified={LastModified}", _filePath, FileInfo.Length, _blockSize, UpdateFreqMs, FileInfo.LastWriteTimeUtc);
        }

        public IReadOnlyList<Line> GetLines(long startPos)
        {
            if (startPos > FileInfo.Length) throw new ArgumentException($"The 'startPos' argument is larger than the length of the file. startPos={startPos}");
            var blockId = (int) (startPos / _blockSize); // Integer division by purpose
            return GetLines(blockId);
        }

        public IReadOnlyList<Line> GetPrevLines() => GetLines(_lastUsedBlock-1);

        public IReadOnlyList<Line> GetNextLines() => GetLines(_lastUsedBlock+1);

        private IReadOnlyList<Line> GetLines(int blockId)
        {
            if (blockId < 0) return new List<Line>();
            if (blockId * _blockSize > FileInfo.Length) return new List<Line>();

            if (!_blocks.TryGetValue(blockId, out var block))
            {
                block = new Block(_filePath, blockId, _blockSize, FileInfo.Length);
                _blocks.Add(blockId, block);
            }

            if (block != null) _lastUsedBlock = blockId;

            // Try to convert block (if any) to lines
            return block != null ? block.Lines() : new List<Line>();
        }

        private void CheckFileInfo()
        {
            FileInfo = new FileInfo(_filePath);
        }
    }
}