using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Timers;
using Avalonia.Media.Imaging;
using DynamicData;
using DynamicData.Binding;
using JetBrains.Annotations;
using LogExpress.Models;
using LogExpress.Utils;
using ReactiveUI;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ILogger = Serilog.ILogger;
using Path = System.IO.Path;
using Timer = System.Timers.Timer;


namespace LogExpress.Services
{
    /// <summary>
    ///     Using the given fileSpecs, monitor the matching files, either existing, created, deleted or updated.
    ///     Order the monitored file-list by creation date and allows for looking up a given byte position with a given size.
    /// </summary>
    public class VirtualLogFile : ReactiveObject, IDisposable
    {
        /**
         * TODO-list
         * * Filters:
         *   • Change the scopedFiles detector to parse start and end to find the date-range for every file.
         *   • Use the aforementioned start-date as the creation-date in the file-info (avoid problems with copied files)
         *   • Check for overlaps in the log-files loaded and show error message if that happens
         *   • Use the aforementioned start- and end-dates to create a filter year-list.
         *   • Use the above year-list filter to find out where to start and stop filtering, when the filter is applied.
         *   • If more than two years range, then consider looking for the years in-between (should be rare cases only...?)
         *   • Do not allow selecting a month until a year has been chosen. When chosen iterate the relevant files for months,
         *     using a binary search approach. 
         *   • When a month-filter is chosen, use the info from the above month-list to know when to start and stop including lines.
         *   • When using the day or hour up/down navigators, first use the file-info to see if the file is relevant, then use a
         *     binary search to find the previous/next day or hour.
         * * Mini-map:
         *   • Move the generation of the map to the LogView
         *   • Create a double resolution mini-map for AllLines and another for FilteredLines (if they differ).
         *     Low-res: In the UI this will be about 3_000 pixels in height, and will contain all lines.
         *     High-res: In the UI this will be a "fisheye", showing the higher resolution image at the same position as the
         *       low-res version, giving the user a chance of seeing the actual log-levels.
         *   • Each of the two above mentioned mini-maps are in reality to be composed by multiple images:
         *   * First of all, we want them to be in two different resolutions:
         *         Lo-res: In the UI this will be about 3_000 pixels in height, and will contain all lines.
         *          
         * * File changes:
         *   • File appends (active file only): Already done
         *   • File rollover:
         *     - If a new file is created, which becomes the new active file, then we assume rollover:
         *         This is an easy scenario to include, as it means that we just add the new file to the file-list and mark it as the
         *         active file and start monitoring it.
         *     - If the active file is renamed, then we assume rollover:
         *         We need to modify the "active file"-lines to reference the renamed file (changed folder and or name)
         *         The new file is then just added and monitored as the new active file.
         *   • File deleted: We should be able to remove the associated lines.
         *   • File changed (not active): Alert user about the change, allowing for refresh?
         *   • File moved: Update the affected lines to reference the file
         *   • File added (not becoming the new active): Alert user about the change, allowing for refresh?
         */
        // TODO: 
        // TODO: Add list of months to be used in the log-view, to bind to UI-actions and to use to create the FilteredLines

        public readonly Dictionary<string, byte> LogLevelLookup = new Dictionary<string, byte>
        {
            {"VRB", 1},
            {"DBG", 2},
            {"INF", 3},
            {"WRN", 4},
            {"ERR", 5},
            {"FTL", 6},
            {"WARN", 4},
            {"TRACE", 1},
            {"DEBUG", 2},
            {"FATAL", 6},
            {"VERBOSE", 1},
        };

        private ObservableConcurrentDictionary<byte, long> _logLevelStats;

        private static readonly ILogger Logger = Log.ForContext<VirtualLogFile>();

        private bool _analyzeError;
        private readonly ScopedFileMonitor _fileMonitor;

        private readonly IDisposable _fileMonitorSubscription;
        private bool _isAnalyzed = true;
        private readonly ReadOnlyObservableCollection<ScopedFile> _logFiles;
        private long _totalSize;
        private LogFileFilter _logFileFilterSelected;
        private int _yearFilterSelected;
        private int _monthFilterSelected;
        private int _levelFilterSelected;

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
                    AllLines = LinesInBgThread;

                    if (AllLines != null && AllLines.Any())
                    {
                        // Create LogLevelMap to show on the side of the scrollbar
                        CreateLogLevelMapImage();
                    }

                    Logger.Debug("Finished analyzing");
                });

            _fileMonitor = new ScopedFileMonitor(BasePath, new List<string>{Filter}, recursive);

            _fileMonitorSubscription = _fileMonitor.Connect()
                .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                .Bind(out _logFiles)
                .Subscribe(InitializeLines);

            this.WhenAnyValue(x => x.LogFiles, x => x.LogFiles.Count, x => x.IsAnalyzed)
                .Where(((ReadOnlyObservableCollection<ScopedFile> logFiles, int count, bool isAnalyzed) obs) =>
                {
                    var (_, count, isAnalyzed) = obs;
                    return isAnalyzed && count > 0;
                })
                .DistinctUntilChanged()
                .Subscribe(MonitorActiveLogFile);

            // This works because we always create the NewLines collection when there is changes
            this.WhenAnyValue(x => x.NewLines)
                .Where(x => x != null && x.Any())
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(AddNewLinesExecute);

            this.WhenAnyValue(x => x.IsAnalyzed, x => x.AllLines, x => x.LogFileFilterSelected, x => x.YearFilterSelected, x => x.MonthFilterSelected, x => x.LevelFilterSelected)
                .Where((
                    (bool isAnalyzed, ObservableCollection<LineItem> lines, LogFileFilter logFileFilterSelected, int
                        yearFilterSelected, int monthFilterSelected, int levelFilterSelected) obs) =>
                {
                    var (isAnalyzed, lines, _, _, _, _) = obs;
                    return isAnalyzed && lines != null;
                })
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(((bool isAnalyzed, ObservableCollection<LineItem> lines, LogFileFilter logFileFilterSelected, int yearFilterSelected, int monthFilterSelected, int levelFilterSelected) obs) =>
                {
                    var (_, _, logFileFilterSelected, yearFilterSelected, monthFilterSelected, levelFilterSelected) = obs;

                    if (logFileFilterSelected?.ScopedFile == null
                        && levelFilterSelected == 0
                        && yearFilterSelected == 0
                        && monthFilterSelected == 0
                    )
                    {
                        FilteredLines = AllLines;
                    }
                    else
                    {
                        // Used to give lines without its own date the same date as the last accepted date
                        var lastTimestamp = DateTime.MinValue;

                        FilteredLines = new ObservableCollection<LineItem>(AllLines
                            .Where(item => DoFilter(item, logFileFilterSelected, yearFilterSelected, monthFilterSelected, levelFilterSelected, ref lastTimestamp)));

                    }
                });


        }
        private bool DoFilter(LineItem lineItem, LogFileFilter logFileFilterSelected, int yearFilterSelected, int monthFilterSelected, int levelFilterSelected, ref DateTime lastTimestamp)
        {
            // Check the logfile-filter
            var logFileIncluded = logFileFilterSelected?.ScopedFile == null || lineItem.CreationTimeTicks == logFileFilterSelected.ScopedFile.LinesListCreationTime;
            if (!logFileIncluded) return false;

            // Check the log-level-filter (includes log-lines that has the default log-level (0)
            var levelIncluded = levelFilterSelected == 0 || lineItem.LogLevel == 0 || lineItem.LogLevel >= levelFilterSelected;
            if (!levelIncluded) return false;

            // If no year-filter is selected then we can just return true (not necessary to check the month-filter)
            if (yearFilterSelected == 0) return true;

            // Check the year-filter
            var timestamp = lineItem.Timestamp; // NB! Fetches the line timestamp from disk
            lastTimestamp = timestamp ?? lastTimestamp;

            var year = timestamp?.Year ?? lastTimestamp.Year;
            var yearIncluded = yearFilterSelected == year;
            if (!yearIncluded) return false;

            // If no month-filter is selected then we can just return true
            if (monthFilterSelected == 0) return true;

            // Check the month-filter
            var month = timestamp?.Month ?? lastTimestamp.Month;
            var monthIncluded = monthFilterSelected == month;
            // Last check
            return monthIncluded;
        }

        private void AddNewLinesExecute(ObservableCollection<LineItem> newLines)
        {
            foreach (var lineItem in newLines)
            {
                var addPosition = -1;
                for (var i = AllLines.Count - 1; i >= 0; i--)
                {
                    if (AllLines[i].CreationTimeTicks == lineItem.CreationTimeTicks 
                        && AllLines[i].Position == lineItem.Position)
                    {
                        addPosition = i;
                        break;
                    }

                    if (AllLines[i].CreationTimeTicks < lineItem.CreationTimeTicks
                        || AllLines[i].Position < lineItem.Position)
                    {
                        addPosition = -1;
                        break;
                    }
                }

                if (addPosition == -1)
                {
                    AllLines.Add(lineItem);
                }
                else
                {
                    LogLevelStats[AllLines[addPosition].LogLevel]--;
                    AllLines[addPosition] = lineItem;
                }
                LogLevelStats[lineItem.LogLevel]++;
            }
            TotalSize = LogFiles.Sum(l => l.Length);

            newLines.Clear();
        }

        // TODO: Create log-map on the fly based on filter-changes in the Lines in the UI
        // Which means perhaps move it from here to the UI as a responsibility?
        private void CreateLogLevelMapImage()
        {
            const int targetPartImageHeight = 500_000;
            const int logLevelMapHeight = 2_048;

            var miniMapFolder = Path.Combine(App.DataFolder, "tmp");
            Directory.CreateDirectory(miniMapFolder);

            var numLines = AllLines.Count;

            var parts = numLines / targetPartImageHeight + 1;
            var linesPerPart = (int) Math.Ceiling((double) numLines / parts);

            var imgParts = new List<Image<Rgba32>>(parts);

            var linesDone = 0;
            for (var i = 0; i < parts; i++)
            {
                var linesInThisPart = Math.Min(linesPerPart * (i + 1), numLines) - linesDone;

                var image = new Image<Rgba32>(10, linesInThisPart);

                for (var y = 0; y < linesInThisPart; y++)
                {
                    var lineItem = AllLines[linesDone];

                    LogLevelStatsIncrement(LogLevelStats, lineItem.LogLevel);

                    var pixel = lineItem.LogLevel switch
                    {
                        6 => Rgba32.Red,
                        5 => Rgba32.Salmon,
                        4 => Rgba32.Gold,
                        3 => Rgba32.Beige,
                        2 => Rgba32.Wheat,
                        1 => Rgba32.Tan,
                        _ => Rgba32.FloralWhite
                    };

                    var pixelRowSpan = image.GetPixelRowSpan(y);

                    for (var x = 0; x < image.Width; x++)
                    {
                        pixelRowSpan[x] = pixel;
                    }
                    linesDone++;
                }

                if (linesInThisPart > logLevelMapHeight)
                {
                    image.Mutate(x => x.Resize(10, logLevelMapHeight / parts));
                }

                imgParts.Add(image);
            }

            var combinedImage = parts == 1 ? imgParts[0] : new Image<Rgba32>(10, logLevelMapHeight);
            if (parts > 1)
            {
                var globalRow = 0;
                for (int i = 0; i < parts; i++)
                {
                    var img = imgParts[i];

                    for (var y = 0; y < logLevelMapHeight / parts; y++)
                    {
                        var combinedImageRowSpan = combinedImage.GetPixelRowSpan(globalRow);

                        for (var x = 0; x < img.Width; x++)
                        {
                            combinedImageRowSpan[x] = img[x, y];
                        }

                        globalRow++;
                    }
                }
            }

            var fileName = Path.Combine(miniMapFolder, "logLevelMap.bmp");
            combinedImage.Save(fileName, new BmpEncoder());

            LogLevelMapFile = fileName;

            imgParts.ForEach(i => i.Dispose());
            combinedImage.Dispose();
        }

        private static void LogLevelStatsIncrement(IDictionary<byte, long> logLevelStats, byte logLevel)
        {
            if (!logLevelStats.ContainsKey(logLevel))
            {
                logLevelStats.Add(logLevel, 0);
            }

            logLevelStats[logLevel] += 1;
        }

        private string _logLevelMapFile;

        public string LogLevelMapFile
        {
            get => _logLevelMapFile;
            set => this.RaiseAndSetIfChanged(ref _logLevelMapFile, value);
        }

        private ObservableCollection<LineItem> _filteredLines;

        public ObservableCollection<LineItem> FilteredLines
        {
            get => _filteredLines;
            set => this.RaiseAndSetIfChanged(ref _filteredLines, value);
        }

        [UsedImplicitly]
        public LogFileFilter LogFileFilterSelected
        {
            get => _logFileFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _logFileFilterSelected, value);
        }

        [UsedImplicitly]
        public int YearFilterSelected
        {
            get => _yearFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _yearFilterSelected, value);
        }

        [UsedImplicitly]
        public int MonthFilterSelected
        {
            get => _monthFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _monthFilterSelected, value);
        }

        [UsedImplicitly]
        public int LevelFilterSelected
        {
            get => _levelFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _levelFilterSelected, value);
        }

        private void MonitorActiveLogFile((ReadOnlyObservableCollection<ScopedFile> scopedFiles, int count, bool isAnalyzed) tuple)
        {
            var (scopedFiles, _, isAnalyzed) = tuple;

            if (scopedFiles == null || !scopedFiles.Any() || !isAnalyzed)
            {
                _activeLogFileMonitor?.Dispose();
                _activeLogFileMonitor = null;
            } 
            else 
            {
                // Three checks every second
                _activeLogFileMonitor = new Timer(333);
                _activeLogFileMonitor.Elapsed += CheckActiveLogFileChanged;
                _activeLogFileMonitor.AutoReset = true;
                _activeLogFileMonitor.Enabled = true;
            }
        }

        private void CheckActiveLogFileChanged(object sender, ElapsedEventArgs e)
        {
            if (!_logFiles.Any()) return;
            _activeLogFileMonitor.Stop();

            var previousFileInfo = _logFiles.Last();

            var currentFileInfo = new ScopedFile(previousFileInfo.FullName, BasePath);
            if (currentFileInfo.Length > previousFileInfo.Length)
            {
                var newLines = new ObservableCollection<LineItem>();
                ReadFileLinePositions(newLines, currentFileInfo, previousFileInfo);
                if (newLines.Any()) NewLines = newLines;

                previousFileInfo.Length = currentFileInfo.Length;
            }
            _activeLogFileMonitor.Start();
        }

        private ObservableCollection<LineItem> _newLines;

        public ObservableCollection<LineItem> NewLines
        {
            get => _newLines;
            set => this.RaiseAndSetIfChanged(ref _newLines, value);
        }

        public bool AnalyzeError
        {
            get => _analyzeError;
            set => this.RaiseAndSetIfChanged(ref _analyzeError, value);
        }

        public string BasePath { get; set; }

        public string Filter { get; set; }

        private ObservableCollection<LineItem> _allLines = new ObservableCollection<LineItem>();

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

        public ObservableConcurrentDictionary<byte, long> LogLevelStats
        {
            get => _logLevelStats;
            set => this.RaiseAndSetIfChanged(ref _logLevelStats, value);
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
        private readonly Dictionary<WordMatcher, byte> _logLevelMatchers = new Dictionary<WordMatcher, byte>();
        private Bitmap _logLevelMap;
        private Timer _activeLogFileMonitor;

        public Bitmap LogLevelMap
        {
            get => _logLevelMap;
            set => this.RaiseAndSetIfChanged(ref _logLevelMap, value);
        }

        public ObservableCollection<LineItem> AllLines
        {
            get => _allLines;
            set => this.RaiseAndSetIfChanged(ref _allLines, value);
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
            _activeLogFileMonitor?.Dispose();
        }

        private void AnalyzeLinesInLogFile(object arg)
        {
            var (logFiles, changes) =
                ((ReadOnlyObservableCollection<ScopedFile>, IChangeSet<ScopedFile, ulong>)) arg;

            LinesInBgThread = new ObservableCollection<LineItem>();

            // Do the all 
            var iterator = logFiles.GetEnumerator();
            try
            {
                while (iterator.MoveNext())
                {
                    var fileInfo = iterator.Current;
                    ReadFileLinePositions(LinesInBgThread, fileInfo);
                }

                iterator.Dispose();
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

        private void ReadFileLinePositions(ObservableCollection<LineItem> linesCollection, ScopedFile file,
            ScopedFile previousFileInfo = null)
        {

            if (file.Length <= 0) return; 

            using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);

            FindNewLinePositions();

            // Local method to encapsulate the Span, which is not allowed in async code
            void FindNewLinePositions()
            {
                var buffer = new Span<char>(new char[1]);

                // All files with length > 0 implicitly starts with a line
                int lineNum = previousFileInfo == null ? 1 : _allLines.Last().LineNumber;
                uint filePosition = (uint) (previousFileInfo?.Length ?? 0);
                uint lastNewLinePos = previousFileInfo == null ? 0 : _allLines.Last().Position;
                byte logLevel = 0;
                byte lastLogLevel = 0;
                int linePos = 0;
                reader.BaseStream.Seek(filePosition, SeekOrigin.Begin);
                while (!reader.EndOfStream)
                {
                    var numRead = reader.Read(buffer);
                    if (numRead == -1) continue; // End of stream
                    filePosition++;
                    linePos++;

                    // Check if the data read so far matches a logLevel indicator
                    if (logLevel == 0 && linePos >= 25)
                    {
                        foreach (var (wordMatcher, level) in _logLevelMatchers)
                        {
                            if (!wordMatcher.IsMatch(buffer[0])) continue;
                            logLevel = level;
                            break;
                        }
                    }

                    if (buffer[0] == '\n')
                    {
                        lastLogLevel = logLevel > 0 ? logLevel : lastLogLevel;
                        linesCollection.Add(new LineItem(file, lineNum, lastNewLinePos, lastLogLevel));
                        lastNewLinePos = filePosition;
                        lineNum += 1;
                        logLevel = 0;
                        linePos = 0;
                        foreach (var (wordMatcher, _) in _logLevelMatchers)
                        {
                            wordMatcher.Reset();
                        }
                    }
                }

                if (filePosition >= lastNewLinePos)
                {
                    linesCollection.Add(new LineItem(file, lineNum, lastNewLinePos, lastLogLevel));
                }
            }
        }

        private void InitializeLines(IChangeSet<ScopedFile, ulong> changes = null)
        {
            IsAnalyzed = false;
            LogLevelStats = new ObservableConcurrentDictionary<byte, long>();
            // ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
            foreach (byte i in Enumerable.Range(0,6))
            {
                LogLevelStats[i] = 0;
            };

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
        private bool _disqualified;

        public WordMatcher(string wordToCheck)
        {
            _wordToCheck = wordToCheck;
        }

        public bool IsMatch(char nextChar)
        {
            // Already disqualified
            if (_disqualified) return false;
            
            // Check if the character matches (negate to update _disqualified immediately)
            _disqualified = !_wordToCheck[_matchCounter].Equals(nextChar);
            
            // No match
            if (_disqualified) return false;

            // Actual match!
            return ++_matchCounter == _wordToCheck.Length;
        }

        public void Reset()
        {
            _matchCounter = 0;
            _disqualified = false;
        }
    }
}