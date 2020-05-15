using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Timer = System.Timers.Timer;

namespace LogExpress.Services
{
    /// <summary>
    ///     Using the given fileSpecs, monitor the matching files, either existing, created, deleted or updated.
    ///     Order the monitored file-list by creation date and allows for looking up a given byte position with a given size.
    /// </summary>
    public class VirtualLogFile : ReactiveObject, IDisposable
    {
        public ObservableCollection<LineItem> LinesInBgThread;
        private static readonly ILogger Logger = Log.ForContext<VirtualLogFile>();
        private readonly ScopedFileMonitor _fileMonitor;
        private readonly IDisposable _fileMonitorSubscription;
        private readonly ReadOnlyObservableCollection<ScopedFile> _logFiles;

        private Timer _activeLogFileMonitor;
        private ObservableCollection<LineItem> _allLines = new ObservableCollection<LineItem>();
        private bool _analyzeError;
        private ObservableCollection<LineItem> _filteredLines;
        private bool _isAnalyzed;
        private ObservableCollection<LineItem> _newLines;
        private bool _newLinesRead;
        private (DateTime, DateTime) _range;
        private Bitmap _severityMap;

        private string _severityMapFile;

        private ObservableConcurrentDictionary<byte, long> _severityStats;
        private bool _showLines;
        private bool _showProgress;
        private long _totalSize;
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

            Logger.Debug(
                "Creating instance for basePath={basePath} and filters={filters} with recurse={recurse}",
                basePath, pattern, recursive);

            this.WhenAnyValue(x => x.AnalyzeError)
                .Where(x => x)
                .DistinctUntilChanged()
                .Subscribe(_ =>
                {
                    // TODO: Fix potential forever loop with Analyze failures
                    Init();
                });

            this.WhenAnyValue(x => x.NewLinesRead)
                .Where(newLinesRead => newLinesRead)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    AllLines = LinesInBgThread;
                    Logger.Debug("Starting reading severities");
                    var timer = Stopwatch.StartNew();
                    InitializeSeverities();
                    Logger.Debug("Finished reading severities. Duration: {Duration}", timer.Elapsed);
                    IsAnalyzed = true;
                });

            this.WhenAnyValue(x => x.IsAnalyzed)
                .Where(isAnalyzed => isAnalyzed)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    // Create SeverityMap to show on the side of the scrollbar
                    if (AllLines != null && AllLines.Any())
                    {
                        Logger.Debug("Creating severity mini-map");
                        var timer = Stopwatch.StartNew();
                        CreateSeverityMapImage();
                        Logger.Debug("Finished creating severity mini-map. Duration: {Duration}", timer.Elapsed);
                    }

                    MonitorActiveLogFile();
                    Logger.Debug("Monitoring active log-file for changes");
                });

            _fileMonitor = new ScopedFileMonitor(BasePath, new List<string> {Pattern}, recursive);

            _fileMonitorSubscription = _fileMonitor.Connect()
                .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                .Bind(out _logFiles)
                .Subscribe(Init);

            this.WhenAnyValue(x => x.LogFiles, x => x.LogFiles.Count, x => x.NewLinesRead)
                .Where(((ReadOnlyObservableCollection<ScopedFile> logFiles, int count, bool newLinesRead) obs) =>
                {
                    var (_, count, newLinesRead) = obs;
                    return newLinesRead && count > 0;
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

                });

            this.WhenAnyValue(x => x.IsAnalyzed, x => x.IsFiltering)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(((bool, bool) obs) =>
                {
                    var (isAnalyzed, isFiltering) = obs;
                    ShowProgress = !isAnalyzed || isFiltering;
                    ShowLines = !ShowProgress;
                });

            this.WhenAnyValue(x => x.IsAnalyzed)
                .Where(isAnalyzed => isAnalyzed)
                .Subscribe(_ =>
                {
                    _severityFilterSource.Clear();
                    _severityFilterSource.AddOrUpdate(new FilterItem<int>(0, 0, "All", "Show all severities, including entries with undetected severity"));
                    foreach (var (level, severity) in Layout.Severities)
                    {
                        var name = level == 0 ? "Any" : $"{level}-{severity}";
                        var toolTip = level == 6
                            ? "Show severity level 6"
                            : $"Show severity level {severity} and higher";
                        _severityFilterSource.AddOrUpdate(new FilterItem<int>(level, level, name, toolTip));
                    }
                });

            // This works because we always create the NewLines collection when there is changes
            this.WhenAnyValue(x => x.NewLines)
                .Where(x => x != null && x.Any())
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(AddNewLinesExecute);

            this.WhenAnyValue(x => x.BackgroundFilteredLines)
                .Where(backgroundFilteredLines => backgroundFilteredLines != null && backgroundFilteredLines.Any())
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(backgroundFilteredLines =>
                {
                    IsFiltering = false;
                    IsFiltered = true;
                    FilteredLines = backgroundFilteredLines; 
                });


            this.WhenAnyValue(x => x.IsAnalyzed, x => x.AllLines, x => x.FileFilterSelected,
                    x => x.SeverityFilterSelected)
                .Where(obs =>
                {
                    var (isAnalyzed, lines, _, _) = obs;
                    return isAnalyzed && lines != null;
                })
                .Subscribe(obs =>
                {
                    var (_, _, fileFilterSelected, severityFilterSelected) = obs;

                    if ((fileFilterSelected == null || fileFilterSelected.Key == 0)
                        && (severityFilterSelected == null || severityFilterSelected.Key == 0)
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
                                .Where(item => DoFilter(item, fileFilterSelected, severityFilterSelected, ref lastTimestamp)));
                        });
                    }
                });

        }

        private void Init(IChangeSet<ScopedFile, ulong> set = null)
        {
            AnalyzeError = false;
            IsAnalyzed = false;
            NewLinesRead = false;
            ShowProgress = true;
            ShowLines = !ShowProgress;
            Logger.Debug("Starting reading newlines");
            var timer = Stopwatch.StartNew();
            InitializeLines(set);
        }

        ~VirtualLogFile()
        {
            // Finalizer calls Dispose(false)
            Dispose(false);
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

        public ObservableCollection<LineItem> FilteredLines
        {
            get => _filteredLines;
            set => this.RaiseAndSetIfChanged(ref _filteredLines, value);
        }

        public bool NewLinesRead
        {
            get => _newLinesRead;
            set => this.RaiseAndSetIfChanged(ref _newLinesRead, value);
        }

        public bool IsAnalyzed
        {
            get => _isAnalyzed;
            set => this.RaiseAndSetIfChanged(ref _isAnalyzed, value);
        }

        public bool IsFiltered
        {
            get => _isFiltered;
            set => this.RaiseAndSetIfChanged(ref _isFiltered, value);
        }

        public bool IsFiltering
        {
            get => _isFiltering;
            set => this.RaiseAndSetIfChanged(ref _isFiltering, value);
        }

        public Layout Layout
        {
            get => _layout;
            set => this.RaiseAndSetIfChanged(ref _layout, value);
        }

        public ReadOnlyObservableCollection<ScopedFile> LogFiles => _logFiles;

        public ObservableCollection<LineItem> NewLines
        {
            get => _newLines;
            set => this.RaiseAndSetIfChanged(ref _newLines, value);
        }

        public string Pattern { get; set; }

        public (DateTime, DateTime) Range
        {
            get => _range;
            set => this.RaiseAndSetIfChanged(ref _range, value);
        }

        public Bitmap SeverityMap
        {
            get => _severityMap;
            set => this.RaiseAndSetIfChanged(ref _severityMap, value);
        }

        public string SeverityMapFile
        {
            get => _severityMapFile;
            set => this.RaiseAndSetIfChanged(ref _severityMapFile, value);
        }

        public ObservableConcurrentDictionary<byte, long> SeverityStats
        {
            get => _severityStats;
            set => this.RaiseAndSetIfChanged(ref _severityStats, value);
        }

        public bool ShowLines
        {
            get => _showLines;
            set => this.RaiseAndSetIfChanged(ref _showLines, value);
        }
        public bool ShowProgress
        {
            get => _showProgress;
            set => this.RaiseAndSetIfChanged(ref _showProgress, value);
        }
        public long TotalSize
        {
            get => _totalSize;
            set => this.RaiseAndSetIfChanged(ref _totalSize, value);
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            _activeLogFileMonitor?.Stop();
            _allLines.Clear();
            _filteredLines.Clear();
            _newLines.Clear();
            _backgroundFilteredLines.Clear();
            _severityStats.Clear();
            _severityMap.Dispose();
            _fileMonitorSubscription?.Dispose();
            _fileMonitor?.Dispose();
            _activeLogFileMonitor?.Dispose();
            _severityFilterSource.Dispose();
        }

        private static void SeverityStatsIncrement(IDictionary<byte, long> severityStats, byte severity)
        {
            if (!severityStats.ContainsKey(severity)) severityStats.Add(severity, 0);

            severityStats[severity] += 1;
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
                    SeverityStats[AllLines[addPosition].Severity]--;
                    AllLines[addPosition] = lineItem;
                }

                SeverityStats[lineItem.Severity]++;
            }

            TotalSize = LogFiles.Sum(l => l.Length);

            newLines.Clear();
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
                ScopedFile.ReadFileLinePositions(newLines, reader/*, _severityMatchers*/, scopedFile, scopedFile.Length, _allLines.Last().LineNumber, _allLines.Last().Position);
                byte lastSeverity = 0;
                var count = newLines.Count;
                for (var i = 0; i < count; i++)
                {
                    var lineItem = newLines[i];
                    var severity = ScopedFile.ReadFileLineSeverity(reader, scopedFile.Layout, lineItem.Position);
                    lineItem.Severity = severity != 0 ? severity : lastSeverity;
                    lastSeverity = severity;
                }

                if (newLines.Any()) NewLines = newLines;

                scopedFile.Length = (uint) fileInfo.Length;
            }

            _activeLogFileMonitor.Start();
        }

        // TODO: Create log-map on the fly based on filter-changes in the Lines in the UI
        // Which means perhaps move it from here to the UI as a responsibility?
        private void CreateSeverityMapImage()
        {
            SeverityStats = new ObservableConcurrentDictionary<byte, long>();
            // ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
            foreach (byte i in Enumerable.Range(0, 7)) SeverityStats[i] = 0;

            const int targetPartImageHeight = 500_000;
            const int severityMapHeight = 2_048;

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

                    SeverityStatsIncrement(SeverityStats, lineItem.Severity);

                    var pixel = lineItem.Severity switch
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

                if (linesInThisPart > severityMapHeight) image.Mutate(x => x.Resize(10, severityMapHeight / parts));

                imgParts.Add(image);
            }

            var combinedImage = parts == 1 ? imgParts[0] : new Image<Rgba32>(10, severityMapHeight);
            if (parts > 1)
            {
                var globalRow = 0;
                for (var i = 0; i < parts; i++)
                {
                    var img = imgParts[i];

                    for (var y = 0; y < severityMapHeight / parts; y++)
                    {
                        var combinedImageRowSpan = combinedImage.GetPixelRowSpan(globalRow);

                        for (var x = 0; x < img.Width; x++) combinedImageRowSpan[x] = img[x, y];

                        globalRow++;
                    }
                }
            }

            var fileName = Path.Combine(miniMapFolder, "SeverityMap.bmp");
            combinedImage.Save(fileName, new BmpEncoder());

            SeverityMapFile = fileName;

            imgParts.ForEach(i => i.Dispose());
            combinedImage.Dispose();
        }

        private bool DoFilter(LineItem lineItem, FilterItem<ScopedFile> fileFilterSelected, FilterItem<int> severityFilterSelected, ref DateTime lastTimestamp)
        {
            // Check the file-filter
            var fileIncluded = fileFilterSelected == null 
                                  || fileFilterSelected.Key == 0
                                  || LogFiles?.FirstOrDefault(l => l.RelativeFullName.Equals(fileFilterSelected?.Name))?.LinesListCreationTime == lineItem.CreationTimeTicks;
            if (!fileIncluded) return false;

            // Check the severity-filter (includes log-lines that has the default severity (0)
            var severityIncluded = severityFilterSelected == null || severityFilterSelected.Key == 0 || lineItem.Severity == 0 ||
                                lineItem.Severity >= severityFilterSelected.Key;
            if (!severityIncluded) return false;

            return true;
        }

        private void InitializeSeverities()
        {
            ScopedFile currentScopedFile = null;
            FileStream currentFileStream = null;
            StreamReader currentReader = null;

            byte lastSeverity = 0;
            var count = AllLines.Count;
            for (var i = 0; i < count; i++)
            {
                var lineItem = AllLines[i];
                if (lineItem.LogFile != currentScopedFile)
                {
                    if (currentReader != null)
                    {
                        currentReader.Dispose();
                        currentFileStream.Dispose();
                    }

                    currentScopedFile = lineItem.LogFile;
                    currentFileStream = new FileStream(currentScopedFile.FullName, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite);
                    currentReader = new StreamReader(currentFileStream, currentScopedFile.Encoding);
                }

                var severity = ScopedFile.ReadFileLineSeverity(currentReader, currentScopedFile?.Layout, lineItem.Position);

                // We use the severity for the previous line, if this line had no severity
                lineItem.Severity = severity != 0 ? severity : lastSeverity;
                lastSeverity = severity;
            }

            if (currentReader != null)
            {
                currentReader.Dispose();
                currentFileStream.Dispose();
            }
        }

        private void InitializeLines(IChangeSet<ScopedFile, ulong> changes = null)
        {
            LineItem.LogFiles = LogFiles;
            TotalSize = LogFiles.Sum(l => l.Length);

            foreach (var scopedFile in LogFiles)
            {
                scopedFile.Layout = Layout;
            }

            // TODO: Use a normal async task for this instead, or is this ok?
            
            LinesInBgThread = new ObservableCollection<LineItem>();

            // Do the all
            var iterator = LogFiles.GetEnumerator();
            try
            {
                var timer = Stopwatch.StartNew();
                while (iterator.MoveNext())
                {
                    var scopedFile = iterator.Current;
                    if (scopedFile == null)
                    {
                        Logger.Error("Unexpected error: The ScopedFile object in the LogFiles connection was null");
                        break;
                    }
                    using var fileStream = new FileStream(scopedFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fileStream, scopedFile.Encoding);

                    ScopedFile.ReadFileLinePositions(LinesInBgThread, reader/*, _severityMatchers*/, scopedFile);
                }

                Logger.Debug("Finished reading newlines. Duration: {Duration}", timer.Elapsed);
                ShowProgress = false;
                ShowLines = !ShowProgress;
                NewLinesRead = true;
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

        private void MonitorActiveLogFile()
        {

            if (LogFiles == null || !LogFiles.Any() || !IsAnalyzed)
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

        #region SeverityFilter
        private readonly SourceCache<FilterItem<int>, int> _severityFilterSource = new SourceCache<FilterItem<int>, int>(t => t.Key);
        
        private FilterItem<int> _severityFilterSelected;
        [UsedImplicitly]
        public FilterItem<int> SeverityFilterSelected
        {
            get => _severityFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _severityFilterSelected, value);
        }
        public IObservable<IChangeSet<FilterItem<int>, int>> ConnectSeverityFilterItems()
        {
            return _severityFilterSource.Connect();
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
        
        private ObservableCollection<LineItem> _backgroundFilteredLines;
        private bool _isFiltered = false;
        private bool _isFiltering = false;
        private Layout _layout;

        private ObservableCollection<LineItem> BackgroundFilteredLines
        {
            get => _backgroundFilteredLines;
            set => this.RaiseAndSetIfChanged(ref _backgroundFilteredLines, value);
        }

        #endregion Filters
    }
}
