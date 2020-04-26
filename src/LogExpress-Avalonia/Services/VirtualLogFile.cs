using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DynamicData;
using DynamicData.Binding;
using LogExpress.Models;
using Microsoft.VisualBasic.FileIO;
using ReactiveUI;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using Color = Avalonia.Media.Color;
using Path = System.IO.Path;


namespace LogExpress.Services
{
    public class ScopedFile
    {
        public ScopedFile(string file)
        {
            var fi = new FileInfo(file);
            CreationTime = fi.CreationTime;
            Name = fi.Name;
            FullName = fi.FullName;
            Length = fi.Length;
            DirectoryName = fi.DirectoryName;
            LinesListCreationTime = (ulong) (CreationTime.Ticks / LineItem.TicksPerSec);
        }

        public DateTime CreationTime { get; }
        public string Name{ get; }
        public string FullName{ get; }
        public object DirectoryName { get; }
        public long Length{ get; }
        public ulong LinesListCreationTime{ get; }
        public Dictionary<byte, long> LogLevelStats{ get; }

    }

    /// <summary>
    ///     Using the given fileSpecs, monitor the matching files, either existing, created, deleted or updated.
    ///     Order the monitored file-list by creation date and allows for looking up a given byte position with a given size.
    /// </summary>
    public class VirtualLogFile : ReactiveObject, IDisposable
    {
        private const int SKImageMaxSize = 32_000;

        public Dictionary<string, byte> LogLevelLookup = new Dictionary<string, byte>
        {
            {"|VRB", 1},
            {"|VERBOSE", 1},
            {"|TRACE", 1},
            {"|DBG", 2},
            {"|DEBUG", 2},
            {"|INF", 3},
            //{"|INFORMATION", 3},
            {"|WRN", 4},
            {"|WARN", 4},
            //|{"WARNING", 4},
            {"|ERR", 5},
            //{"ERROR", 5},
            {"|FTL", 6},
            {"|FATAL", 6}
        };

        private static readonly ILogger Logger = Log.ForContext<VirtualLogFile>();

        private bool _analyzeError;
        private readonly ScopedFilesMonitor _fileMonitor;

        private readonly IDisposable _fileMonitorSubscription;
        private bool _isAnalyzed = true;
        private readonly ReadOnlyObservableCollection<ScopedFile> _logFiles;
        private long _totalSize;

        // Used to indicate the activeLogFile, as in the last file in the list (newest).
        // This file will have a separate change-check as opposed to monitoring it (which would potentially spam with changes)
        // Instead we check the file for changes 2-3 times per second instead. Which is fast enough for the eye to feel that
        // it is live. Busy log-files could have tons of writes per second, and depending on the logging system changes could potentially
        // be flushed for each write.
        private FileInfo _activeLogFile;

        private FileInfo _selectedLogFileFilter;
        private List<int> _logLevelFilter = new List<int>();

        /// <summary>
        /// Setup the virtual log-file manager.
        /// </summary>
        /// <param name="basePath"></param>
        /// <param name="filter"></param>
        /// <param name="recursive"></param>
        public VirtualLogFile(string basePath, string filter, bool recursive)
        {
            BasePath = basePath;
            Filter = filter;
            LineItem.LogFiles = LogFiles;

            foreach (var pair in LogLevelLookup)
            {
                _logLevelMatchers.Add(new WordMatcher(pair.Key), pair.Value);
            }

            Logger.Debug(
                "Creating instance for basePath={basePath} and filters={filters} with recurse={recurse}",
                basePath, filter, recursive);

            this.WhenAnyValue(x => x.AnalyzeError)
                .Where(x => x)
                .DistinctUntilChanged()
                .Subscribe(_ =>
                {
                    // TODO: Fix potential forever loop with Analyze failures
                    AnalyzeError = false;
                    InitializeLines();
                });

            this.WhenAnyValue(x => x.IsAnalyzed)
                .Where(x => x)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(x =>
                {
                    Lines = LinesInBgThread;
                    if (Lines != null && Lines.Any())
                    {
                        // Create LogLevelMap to show on the side of the scrollbar
                        CreateLogLevelMapImage();
                    }
/*                    if (LinesInBgThread != null)
                    {
                        Lines.AddRange(LinesInBgThread);
                        LinesInBgThread = null;
                    }
*/                    Logger.Debug("Finished analyzing");
                });

            _fileMonitor = new ScopedFilesMonitor(BasePath, new List<string>{Filter}, recursive);

            _fileMonitorSubscription = _fileMonitor.Connect()
                .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                .Bind(out _logFiles)
                .Subscribe(InitializeLines);

            this.WhenAnyValue(x => x.LogFiles)
                .Where(x => x.Any())
                .Select(x => x.LastOrDefault())
                .DistinctUntilChanged()
                .Subscribe(MonitorActiveLogFile);
        }

        private void CreateLogLevelMapImage()
        {
            LogLevelMapFiles = null;
            _logLevelMap?.Dispose();

            // Create or empty the temp logLevel bitmap directory
            var tmpFolder = Path.Combine("tmp","LogLevelBitmaps");
            var di = Directory.CreateDirectory(tmpFolder);

            foreach (var file in di.GetFiles())
            {
                file.Delete(); 
            }
            foreach (var dir in di.GetDirectories())
            {
                dir.Delete(true); 
            }

            var parts = Lines.Count / SKImageMaxSize;
            var linesInLastPiece = Lines.Count % SKImageMaxSize;
            var fileNames = new Dictionary<string, long>();

            for (var part = 0; part < parts+1; part++)
            {
                var height = part < parts ? SKImageMaxSize : linesInLastPiece;

                if (height <= 0) break;

                using var bm = new Image<Rgba32>(1, height);
                var lineStart = part * SKImageMaxSize;

                for (var y = 0; y < height; y++)
                {
                    var lineNumber = lineStart + y;
                    var lineItem = Lines[lineNumber];
                    var pixel = lineItem.LogLevel switch
                    {
                        6 => Rgba32.Red,
                        5 => Rgba32.Salmon,
                        4 => Rgba32.Gold,
                        3 => Rgba32.Beige,
                        2 => Rgba32.Wheat,
                        1 => Rgba32.Tan,
                        _ => Rgba32.White
                    };
                    bm[y, 0] = pixel;
                }

                var fileName = Path.Combine(tmpFolder, $"logLevelMap{part:000}.bmp");
                fileNames.Add(fileName, height);
                bm.Save(fileName, new BmpEncoder());
            }

            LogLevelMapFiles = fileNames;
        }

        public Dictionary<string, long> _logLevelMapFiles;

        public Dictionary<string, long> LogLevelMapFiles
        {
            get => _logLevelMapFiles;
            set => this.RaiseAndSetIfChanged(ref _logLevelMapFiles, value);
        }

        private void MonitorActiveLogFile(ScopedFile f)
        {
            // TODO: Update the ActiveLogFile monitor
        }

        public bool AnalyzeError
        {
            get => _analyzeError;
            set => this.RaiseAndSetIfChanged(ref _analyzeError, value);
        }

        public string BasePath { get; set; }

        public string Filter { get; set; }

        private ObservableCollection<LineItem> _lines = new ObservableCollection<LineItem>();

        public bool IsAnalyzed
        {
            get => _isAnalyzed;
            set => this.RaiseAndSetIfChanged(ref _isAnalyzed, value);
        }

        public ReadOnlyObservableCollection<ScopedFile> LogFiles => _logFiles;

        public FileInfo ActiveLogFile
        {
            get => _activeLogFile;
            set => this.RaiseAndSetIfChanged(ref _activeLogFile, value);
        }

        public long TotalSize
        {
            get => _totalSize;
            set => this.RaiseAndSetIfChanged(ref _totalSize, value);
        }

        // Used to indicate whether or not a single logfile has been selected. 
        // If so then that file's lines are the only ones that should be shown. 
        // If null (none selected) then all logfiles' lines are shown.
        public FileInfo SelectedLogFileFilter
        {
            get => _selectedLogFileFilter;
            set => this.RaiseAndSetIfChanged(ref _selectedLogFileFilter, value);
        }

        /// <summary>
        /// Used to specify which logLevels to show
        /// </summary>
        public List<int> LogLevelFilter
        {
            get => _logLevelFilter;
            set => this.RaiseAndSetIfChanged(ref _logLevelFilter, value);
        }

        public ObservableCollection<LineItem> LinesInBgThread;
        private Dictionary<WordMatcher, byte> _logLevelMatchers = new Dictionary<WordMatcher, byte>();
        private Bitmap _logLevelMap;

        public Bitmap LogLevelMap
        {
            get => _logLevelMap;
            set => this.RaiseAndSetIfChanged(ref _logLevelMap, value);
        }

        public ObservableCollection<LineItem> Lines
        {
            get => _lines;
            set => this.RaiseAndSetIfChanged(ref _lines, value);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            _logLevelMap.Dispose();
            _fileMonitorSubscription?.Dispose();
            _fileMonitor?.Dispose();
        }

        private void AnalyzeLinesInLogFile(object arg)
        {
            var (logFiles, changes) =
                ((ReadOnlyObservableCollection<ScopedFile>, IChangeSet<ScopedFile, ulong>)) arg;

/*
            foreach (var change in changes)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                        // Add the associated lines for this file
                        AddLinesForFile(change.CurrentIndex, change.Current);
                        break;
                    case ChangeReason.Remove:
                        // Remove the associated lines for this logfile
                        RemoveLinesForFile(change.CurrentIndex, change.Current);
                        break;
                    case ChangeReason.Refresh: // Not sure what 
                    case ChangeReason.Moved: // Items will never move in the virtual log file
                    case ChangeReason.Update:
                        // Ignore these changes. Updates to the ActiveLogFile is handled within the VirtualLogFile itself. Any other update is not expected for logs.
                        continue;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change.Reason));
                }
            }
            IsAnalyzed = true;
*/

            LinesInBgThread = new ObservableCollection<LineItem>();
            //var lines = new ObservableCollection<LineItem>();

            // Do the all 
            var iterator = logFiles.GetEnumerator();
            try
            {
                while (iterator.MoveNext())
                {
                    var fileInfo = iterator.Current;
                    ReadFileLinePositions(fileInfo);
/*
                    _linesOLD.Edit(async editLines =>
                    {
                        await ReadFileLinePositions(fileInfo, editLines);
                    });
*/                    
                    //Debug.Assert(fileInfo != null, nameof(fileInfo) + " != null");
                }

                iterator.Dispose();
                //Lines.AddRange(lines);
                IsAnalyzed = true;
            }
            catch (Exception ex)
            {
                Logger.Error("Error during analyze", ex);
                AnalyzeError = true;
            }
            finally
            {
                iterator.Dispose();
            }
        }
/*
        private void AddLinesForFile(int index, FileInfo fileInfo)
        {
            _linesOLD.Edit(async editLines =>
            {
                await ReadFileLinePositions(fileInfo, index, editLines);
            });
        }

        private void RemoveLinesForFile(int index, FileInfo fileInfo)
        {
            _linesOLD.Edit(editLines =>
            {
                foreach (var item in _linesOLD.Items)
                {
                    if (item.LogFileIndex == index)
                        editLines.RemoveKey(item.GlobalPosition);
                }
            });
        }
*/

        private void ReadFileLinePositions(ScopedFile file)
        {

            if (file.Length <= 0) return; 

            using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);

            FindNewLinePositions();

            // Local method to encapsulate the Span, which is not allowed in async code
            void FindNewLinePositions()
            {
                var buffer = new Span<char>(new char[1]);



                var stopwatch = Stopwatch.StartNew();
                // All files with length > 0 implicitly starts with a line
                var lineNum = 1;
                uint filePosition = 0;
                uint lastNewLinePos = 0;
                byte logLevel = 0;
                while (!reader.EndOfStream)
                {
                    var numRead = reader.Read(buffer);
                    if (numRead == -1) continue; // End of stream
                    filePosition++;

                    // Check if the data read so far matches a logLevel indicator
                    if (logLevel == 0)
                    {
                        foreach (var logLevelMatcher in _logLevelMatchers)
                        {
                            if (logLevelMatcher.Key.IsMatch(buffer[0]))
                            {
                                logLevel = logLevelMatcher.Value;
                                break;
                            }
                        }

                        if (logLevel > 0)
                        {
                            foreach (var logLevelMatcher in _logLevelMatchers) logLevelMatcher.Key.Reset();
                        }
                    }

                    if (buffer[0] == '\n')
                    {
                        LinesInBgThread.Add(new LineItem(file, lineNum, lastNewLinePos, logLevel));
                        lastNewLinePos = filePosition;
                        lineNum += 1;
                        logLevel = 0;
                    }
                }

                if (filePosition >= lastNewLinePos)
                {
                    LinesInBgThread.Add(new LineItem(file, lineNum, lastNewLinePos, logLevel));
                }

                stopwatch.Stop();
                var duration = stopwatch.Elapsed;
                Logger.Debug("Time spent on analyzing lines in {LogFile}: {Duration}", file.FullName, duration);
            }
        }

        private void InitializeLines(IChangeSet<ScopedFile, ulong> changes = null)
        {
            IsAnalyzed = false;
            LineItem.LogFiles = LogFiles;
            TotalSize = LogFiles.Sum(l => l.Length);

            // TODO: Use a normal async task for this instead, or is this ok?
            new Thread(AnalyzeLinesInLogFile)
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            }.Start((LogFiles, changes));
        }
    }

    public class WordMatcher
    {
        private readonly string _wordToCheck;
        private int _matchCounter;

        public WordMatcher(string wordToCheck)
        {
            _wordToCheck = wordToCheck;
        }

        public bool IsMatch(char nextChar)
        {
            if (_wordToCheck[_matchCounter].Equals(nextChar))
            {
                _matchCounter++;
            }
            else _matchCounter = 0;

            return _matchCounter == _wordToCheck.Length;
        }

        public void Reset()
        {
            _matchCounter = 0;
        }
    }
}