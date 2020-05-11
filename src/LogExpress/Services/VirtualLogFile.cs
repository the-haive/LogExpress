using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Input;
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
using Timer = System.Timers.Timer;

namespace LogExpress.Services
{
    /// <summary>
    ///     Using the given fileSpecs, monitor the matching files, either existing, created, deleted or updated.
    ///     Order the monitored file-list by creation date and allows for looking up a given byte position with a given size.
    /// </summary>
    public class VirtualLogFile : ReactiveObject, IDisposable
    {
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
            {"VERBOSE", 1}
        };

        public ObservableCollection<LineItem> LinesInBgThread;
        private static readonly ILogger Logger = Log.ForContext<VirtualLogFile>();
        private readonly ScopedFileMonitor _fileMonitor;
        private readonly IDisposable _fileMonitorSubscription;
        private readonly ReadOnlyObservableCollection<ScopedFile> _logFiles;

        private readonly Dictionary<WordMatcher, byte> _logLevelMatchers = new Dictionary<WordMatcher, byte>();
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
        // Used to indicate the activeLogFile, as in the last file in the list (newest).
        // This file will have a separate change-check as opposed to monitoring it (which would potentially spam with changes)
        // Instead we check the file for changes 2-3 times per second instead. Which is fast enough for the eye to feel that
        // it is live. Busy log-files could have tons of writes per second, and depending on the logging system changes could potentially
        // be flushed for each write.
        private FileInfo _activeLogFile;

        private Timer _activeLogFileMonitor;
        private ObservableCollection<LineItem> _allLines = new ObservableCollection<LineItem>();
        private bool _analyzeError;
        private ObservableCollection<LineItem> _filteredLines;
        private bool _isAnalyzed = false;
        private List<int> _logLevelFilter = new List<int>();
        private Bitmap _logLevelMap;

        private string _logLevelMapFile;

        private ObservableConcurrentDictionary<byte, long> _logLevelStats;

        private ObservableCollection<LineItem> _newLines;

        private long _totalSize;
        private (DateTime, DateTime) _range;

        /// <summary>
        ///     Setup the virtual log-file manager.
        /// </summary>
        /// <param name="basePath"></param>
        /// <param name="pattern"></param>
        /// <param name="recursive"></param>
        /// <param name="layout"></param>
        public VirtualLogFile(string basePath, string pattern, bool recursive, Layout layout)
        {
            BasePath = basePath;
            Pattern = pattern;
            Layout = layout;
            LineItem.LogFiles = LogFiles;

            foreach (var pair in LogLevelLookup) _logLevelMatchers.Add(new WordMatcher(pair.Key), pair.Value);

            Logger.Debug(
                "Creating instance for basePath={basePath} and filters={filters} with recurse={recurse}",
                basePath, pattern, recursive);

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
                        // Create LogLevelMap to show on the side of the scrollbar
                        CreateLogLevelMapImage();

                    Logger.Debug("Finished analyzing");
                });

            _fileMonitor = new ScopedFileMonitor(BasePath, new List<string> {Pattern}, recursive);

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
                .Subscribe(tuple =>
                {
                    _fileFilterSource.Clear();
                    var sortedItems = LogFiles.OrderBy(l => l.StartDate).ToList();
                    
                    var i = 1;
                    var filterItems = sortedItems.Select(l => new FilterItem<ScopedFile>(l, i++, l.RelativeFullName, l.FullName));
                    _fileFilterSource.AddOrUpdate(filterItems);

                    var startDate = sortedItems.First().StartDate;
                    var endDate = sortedItems.Last().EndDate;
                    Range = (startDate, endDate);

                    MonitorActiveLogFile(tuple);
                });

            this.WhenAnyValue(x => x.IsAnalyzed, x => x.IsFiltering)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(((bool, bool) obs) =>
                {
                    var (isAnalyzed, isFiltering) = obs;
                    ShowProgress = !isAnalyzed || isFiltering;
                    ShowLines = !ShowProgress;
                });

            this.WhenAnyValue(x => x.LogLevelStats, x => x.IsAnalyzed)
                .Where(obs =>
                {
                    var (logLevelStats, isAnalyzed) = obs;
                    return logLevelStats != null && isAnalyzed;
                })
                .Subscribe(_ =>
                {
                    _levelFilterSource.Clear();
                    foreach (var (level, count) in LogLevelStats)
                        _levelFilterSource.AddOrUpdate(new FilterItem<int>(level, level, $"TODO-{level}"));

                    _monthFilterSource.Clear();
                    _monthFilterSource.AddOrUpdate(new FilterItem<int>(6,6, "June"));
                    _monthFilterSource.AddOrUpdate(new FilterItem<int>(8,8, "August"));

                    _yearFilterSource.Clear();
                    _yearFilterSource.AddOrUpdate(new FilterItem<int>(2019,2019, "2019"));
                    _yearFilterSource.AddOrUpdate(new FilterItem<int>(2020, 2020, "2020"));
                });


            // This works because we always create the NewLines collection when there is changes
            this.WhenAnyValue(x => x.NewLines)
                .Where(x => x != null && x.Any())
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(AddNewLinesExecute);

            this.WhenAnyValue(x => x.BackgroundFilteredLines)
                //.Where(x => x != null && x.Any())
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(collection =>
                {
                    IsFiltering = false;
                    IsFiltered = true;
                    FilteredLines = collection; 
                });


            this.WhenAnyValue(x => x.IsAnalyzed, x => x.AllLines, x => x.FileFilterSelected,
                    x => x.YearFilterSelected, x => x.MonthFilterSelected, x => x.LevelFilterSelected)
                .Where((
                    (bool isAnalyzed, ObservableCollection<LineItem> lines, FilterItem<ScopedFile> fileFilterSelected, FilterItem<int>
                        yearFilterSelected, FilterItem<int> monthFilterSelected, FilterItem<int> levelFilterSelected) obs) =>
                {
                    var (isAnalyzed, lines, _, _, _, _) = obs;
                    return isAnalyzed && lines != null;
                })
                //.ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe((
                    (bool isAnalyzed, ObservableCollection<LineItem> lines, FilterItem<ScopedFile> fileFilterSelected, FilterItem<int>
                        yearFilterSelected, FilterItem<int> monthFilterSelected, FilterItem<int> levelFilterSelected) obs) =>
                {
                    var (_, _, fileFilterSelected, yearFilterSelected, monthFilterSelected, levelFilterSelected) =
                        obs;

                    if ((fileFilterSelected == null || fileFilterSelected.Key == 0)
                        && (levelFilterSelected == null || levelFilterSelected.Key == 0)
                        && (yearFilterSelected == null || yearFilterSelected.Key == 0)
                        && (monthFilterSelected == null || monthFilterSelected.Key == 0)
                    )
                    {
                        IsFiltered = false;
                        FilteredLines = AllLines;
                    }
                    else
                    {
                        IsFiltering = true;
                        Task.Factory.StartNew(() =>
                        {
                            // Used to give lines without its own date the same date as the last accepted date
                            var lastTimestamp = DateTime.MinValue;

                            BackgroundFilteredLines = new ObservableCollection<LineItem>(AllLines
                                .Where(item => DoFilter(item, fileFilterSelected, yearFilterSelected,
                                    monthFilterSelected, levelFilterSelected, ref lastTimestamp)));
                        });
                    }
                });

        }

        private bool _showLines;
        public bool ShowLines
        {
            get => _showLines;
            set => this.RaiseAndSetIfChanged(ref _showLines, value);
        }

        private bool _showProgress;
        public bool ShowProgress
        {
            get => _showProgress;
            set => this.RaiseAndSetIfChanged(ref _showProgress, value);
        }

        public FileInfo ActiveLogFile
        {
            get => _activeLogFile;
            set => this.RaiseAndSetIfChanged(ref _activeLogFile, value);
        }

        public ObservableCollection<LineItem> AllLines
        {
            get => _allLines;
            set => this.RaiseAndSetIfChanged(ref _allLines, value);
        }

        public bool AnalyzeError
        {
            get => _analyzeError;
            set => this.RaiseAndSetIfChanged(ref _analyzeError, value);
        }

        public string BasePath { get; set; }

        public string Pattern { get; set; }
        public Layout Layout { get; set; }

        public ObservableCollection<LineItem> FilteredLines
        {
            get => _filteredLines;
            set => this.RaiseAndSetIfChanged(ref _filteredLines, value);
        }
        public bool IsFiltering
        {
            get => _isFiltering;
            set => this.RaiseAndSetIfChanged(ref _isFiltering, value);
        }
        public bool IsFiltered
        {
            get => _isFiltered;
            set => this.RaiseAndSetIfChanged(ref _isFiltered, value);
        }

        public bool IsAnalyzed
        {
            get => _isAnalyzed;
            set => this.RaiseAndSetIfChanged(ref _isAnalyzed, value);
        }

        public ReadOnlyObservableCollection<ScopedFile> LogFiles => _logFiles;

        public Bitmap LogLevelMap
        {
            get => _logLevelMap;
            set => this.RaiseAndSetIfChanged(ref _logLevelMap, value);
        }

        public string LogLevelMapFile
        {
            get => _logLevelMapFile;
            set => this.RaiseAndSetIfChanged(ref _logLevelMapFile, value);
        }

        public ObservableConcurrentDictionary<byte, long> LogLevelStats
        {
            get => _logLevelStats;
            set => this.RaiseAndSetIfChanged(ref _logLevelStats, value);
        }

        public ObservableCollection<LineItem> NewLines
        {
            get => _newLines;
            set => this.RaiseAndSetIfChanged(ref _newLines, value);
        }

        public long TotalSize
        {
            get => _totalSize;
            set => this.RaiseAndSetIfChanged(ref _totalSize, value);
        }

        public (DateTime, DateTime) Range
        {
            get => _range;
            set => this.RaiseAndSetIfChanged(ref _range, value);
        }

        ~VirtualLogFile()
        {
            // Finalizer calls Dispose(false)
            Dispose(false);
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

        private static void LogLevelStatsIncrement(IDictionary<byte, long> logLevelStats, byte logLevel)
        {
            if (!logLevelStats.ContainsKey(logLevel)) logLevelStats.Add(logLevel, 0);

            logLevelStats[logLevel] += 1;
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
                    var scopedFile = iterator.Current;
                    using var fileStream = new FileStream(scopedFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fileStream, scopedFile.Encoding);

                    ScopedFile.ReadFileLinePositions(LinesInBgThread, reader, _logLevelMatchers, scopedFile);
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

        private void CheckActiveLogFileChanged(object sender, ElapsedEventArgs e)
        {
            if (!_logFiles.Any()) return;
            _activeLogFileMonitor.Stop();

            var scopedFile = _logFiles.Last();

            var fileInfo = new FileInfo(scopedFile.FullName);
            if (fileInfo.Length > scopedFile.Length)
            {
                using var fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream, scopedFile.Encoding);
                var newLines = new ObservableCollection<LineItem>();
                ScopedFile.ReadFileLinePositions(newLines, reader, _logLevelMatchers, scopedFile, scopedFile.Length, _allLines.Last().LineNumber, _allLines.Last().Position);
                if (newLines.Any()) NewLines = newLines;

                scopedFile.Length = (uint) fileInfo.Length;
            }

            _activeLogFileMonitor.Start();
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

                    for (var x = 0; x < image.Width; x++) pixelRowSpan[x] = pixel;
                    linesDone++;
                }

                if (linesInThisPart > logLevelMapHeight) image.Mutate(x => x.Resize(10, logLevelMapHeight / parts));

                imgParts.Add(image);
            }

            var combinedImage = parts == 1 ? imgParts[0] : new Image<Rgba32>(10, logLevelMapHeight);
            if (parts > 1)
            {
                var globalRow = 0;
                for (var i = 0; i < parts; i++)
                {
                    var img = imgParts[i];

                    for (var y = 0; y < logLevelMapHeight / parts; y++)
                    {
                        var combinedImageRowSpan = combinedImage.GetPixelRowSpan(globalRow);

                        for (var x = 0; x < img.Width; x++) combinedImageRowSpan[x] = img[x, y];

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
        private bool DoFilter(LineItem lineItem, FilterItem<ScopedFile> fileFilterSelected, FilterItem<int> yearFilterSelected,
            FilterItem<int> monthFilterSelected, FilterItem<int> levelFilterSelected, ref DateTime lastTimestamp)
        {
            // Check the file-filter
            var fileIncluded = fileFilterSelected == null 
                                  || fileFilterSelected.Key == 0
                                  || LogFiles?.FirstOrDefault(l => l.RelativeFullName.Equals(fileFilterSelected?.Name))?.LinesListCreationTime == lineItem.CreationTimeTicks;
            if (!fileIncluded) return false;

            // Check the log-level-filter (includes log-lines that has the default log-level (0)
            var levelIncluded = levelFilterSelected == null || levelFilterSelected.Key == 0 || lineItem.LogLevel == 0 ||
                                lineItem.LogLevel >= levelFilterSelected.Key;
            if (!levelIncluded) return false;

            // If no year-filter is selected then we can just return true (not necessary to check the month-filter)
            if (yearFilterSelected == null || yearFilterSelected.Key == 0) return true;

            // Check the year-filter
            var timestamp = lineItem.Timestamp; // NB! Fetches the line timestamp from disk
            lastTimestamp = timestamp ?? lastTimestamp;

            var year = timestamp?.Year ?? lastTimestamp.Year;
            var yearIncluded = yearFilterSelected?.Key == year;
            if (!yearIncluded) return false;

            // If no month-filter is selected then we can just return true
            if (monthFilterSelected == null || monthFilterSelected.Key == 0) return true;

            // Check the month-filter
            var month = timestamp?.Month ?? lastTimestamp.Month;
            var monthIncluded = monthFilterSelected?.Key == month;
            // Last check
            return monthIncluded;
        }

        private void InitializeLines(IChangeSet<ScopedFile, ulong> changes = null)
        {
            IsAnalyzed = false;
            LogLevelStats = new ObservableConcurrentDictionary<byte, long>();
            // ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
            foreach (byte i in Enumerable.Range(0, 7)) LogLevelStats[i] = 0;

            LineItem.LogFiles = LogFiles;
            TotalSize = LogFiles.Sum(l => l.Length);

            foreach (var scopedFile in LogFiles)
            {
                scopedFile.Layout = Layout;
            }

            // TODO: Use a normal async task for this instead, or is this ok?
            new Thread(AnalyzeLinesInLogFile)
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            }.Start((LogFiles, changes));
        }
        private void MonitorActiveLogFile(
            (ReadOnlyObservableCollection<ScopedFile> scopedFiles, int count, bool isAnalyzed) tuple)
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
   
        #region Filters

        #region LevelFilter
        private readonly SourceCache<FilterItem<int>, int> _levelFilterSource = new SourceCache<FilterItem<int>, int>(t => t.Key);
        
        private FilterItem<int> _levelFilterSelected;
        [UsedImplicitly]
        public FilterItem<int> LevelFilterSelected
        {
            get => _levelFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _levelFilterSelected, value);
        }
        public IObservable<IChangeSet<FilterItem<int>, int>> ConnectLevelFilterItems()
        {
            return _levelFilterSource.Connect();
        }

        #endregion

        #region FileFilter
        private readonly SourceCache<FilterItem<ScopedFile>, int> _fileFilterSource = new SourceCache<FilterItem<ScopedFile>, int>(t => t.Key);

        private FilterItem<ScopedFile> _fileFilterSelected;
        [UsedImplicitly]
        public FilterItem<ScopedFile> FileFilterSelected
        {
            get => _fileFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _fileFilterSelected, value);
        }
        public IObservable<IChangeSet<FilterItem<ScopedFile>, int>> ConnectFileFilterItems()
        {
            return _fileFilterSource.Connect();
        }
        #endregion
        
        #region MonthFilter
        private readonly SourceCache<FilterItem<int>, int> _monthFilterSource = new SourceCache<FilterItem<int>, int>(t => t.Key);
        
        private FilterItem<int> _monthFilterSelected;
        [UsedImplicitly]
        public FilterItem<int> MonthFilterSelected
        {
            get => _monthFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _monthFilterSelected, value);
        }
        public IObservable<IChangeSet<FilterItem<int>, int>> ConnectMonthFilterItems()
        {
            return _monthFilterSource.Connect();
        }
        #endregion
        
        #region YearFilter
        private readonly SourceCache<FilterItem<int>, int> _yearFilterSource = new SourceCache<FilterItem<int>, int>(t => t.Key);

        private FilterItem<int> _yearFilterSelected;
        [UsedImplicitly]
        public FilterItem<int> YearFilterSelected
        {
            get => _yearFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _yearFilterSelected, value);
        }
        public IObservable<IChangeSet<FilterItem<int>, int>> ConnectYearFilterItems()
        {
            return _yearFilterSource.Connect();
        }
        #endregion

        private ObservableCollection<LineItem> _backgroundFilteredLines;
        private bool _isFiltering = false;
        private bool _isFiltered = false;

        private ObservableCollection<LineItem> BackgroundFilteredLines
        {
            get => _backgroundFilteredLines;
            set => this.RaiseAndSetIfChanged(ref _backgroundFilteredLines, value);
        }

        #endregion Filters
    }

    public class WordMatcher
    {
        private readonly string _wordToCheck;
        private bool _disqualified;
        private int _matchCounter;

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
