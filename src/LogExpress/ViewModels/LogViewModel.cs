using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Imaging;
using DynamicData;
using DynamicData.Binding;
using JetBrains.Annotations;
using LogExpress.Models;
using LogExpress.Services;
using LogExpress.Views;
using ReactiveUI;
using Serilog;
using TextCopy;
using Path = System.IO.Path;
using ScopedFile = LogExpress.Models.ScopedFile;

namespace LogExpress.ViewModels
{
    public class LogViewModel : ViewModelBase
    {
        private static readonly ILogger Logger = Log.ForContext<LogViewModel>();
        private readonly IObservable<bool> _hasLineSelection;
        //private readonly ObservableAsPropertyHelper<List<LogFileFilter>> _logFilesFilter;
        private readonly LogView _logView;
        private readonly ObservableAsPropertyHelper<long> _totalSize;

        [UsedImplicitly] public VirtualLogFile VirtualLogFile { get; set; }

        private string _basePath;
        private ObservableCollection<LineItem> _lines;
        private LineItem _lineSelected;
        private ObservableCollection<LineItem> _linesSelected = new ObservableCollection<LineItem>();
        private string _logLevelMapFile;
        private bool _selectedLast;
        private bool _tail = true;

        [UsedImplicitly]
        public string BasePath
        {
            get => _basePath;
            set => this.RaiseAndSetIfChanged(ref _basePath, value);
        }

        public ObservableCollection<LineItem> Lines
        {
            get => _lines;
            set => this.RaiseAndSetIfChanged(ref _lines, value);
        }

        public LineItem LineSelected
        {
            get => _lineSelected;
            set => this.RaiseAndSetIfChanged(ref _lineSelected, value);
        }

        [UsedImplicitly]
        public ObservableCollection<LineItem> LinesSelected
        {
            get => _linesSelected;
            set => this.RaiseAndSetIfChanged(ref _linesSelected, value);
        }


        //[UsedImplicitly] public List<LogFileFilter> LogFilesFilter => _logFilesFilter?.Value;

        public string LogLevelMapFile
        {
            get => _logLevelMapFile;
            set => this.RaiseAndSetIfChanged(ref _logLevelMapFile, value);
        }

        [UsedImplicitly]
        public bool Tail
        {
            get => _tail;
            set => this.RaiseAndSetIfChanged(ref _tail, value);
        }

        [UsedImplicitly] public long TotalSize => _totalSize.Value;

        internal void BrowseSearchBack()
        {
            DoFindExecute(VirtualLogFile.FilteredLines.Last(), -1, 0);
        }

        internal void BrowseSearchFrwd()
        {
            DoFindExecute(VirtualLogFile.FilteredLines.First(), +1, VirtualLogFile.FilteredLines.Count);
        }

        private void BrowseLevelBack(int logLevel)
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.LastOrDefault(l =>
                    l.LogLevel == logLevel && (l.CreationTimeTicks < _lineSelected.CreationTimeTicks ||
                                               l.LineNumber < _lineSelected.LineNumber))
                : VirtualLogFile.FilteredLines.LastOrDefault();

            if (match == null) return;

            _logView.LinesCtrl.SelectedItem = match;
        }

        private void BrowseLevelFrwd(int logLevel)
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.FirstOrDefault(l =>
                    l.LogLevel == logLevel && (l.CreationTimeTicks > _lineSelected.CreationTimeTicks ||
                                               l.LineNumber > _lineSelected.LineNumber))
                : VirtualLogFile.FilteredLines.FirstOrDefault();

            if (match == null) return;

            _logView.LinesCtrl.SelectedItem = match;
        }

        private void BrowseTimeBack(Func<DateTime, long> maxTicksFactory)
        {
            if (_lineSelected == null) return;

            var selIdx = _logView.LinesCtrl.SelectedIndex;
            var content = VirtualLogFile.FilteredLines[selIdx].Content.Split('|').FirstOrDefault();

            if (!DateTime.TryParse(content, out var contentTime)) contentTime = DateTime.MaxValue;

            var maxTicks = maxTicksFactory(contentTime);

            for (var i = selIdx - 1; i >= 0; i--)
            {
                var lineContent = VirtualLogFile.FilteredLines[i].Content.Split('|').FirstOrDefault();

                if (!DateTime.TryParse(lineContent, out var itemTime)) continue;
                if (itemTime.Ticks >= maxTicks) continue;
                _logView.LinesCtrl.SelectedItem = VirtualLogFile.FilteredLines[i];
                break;
            }
        }

        private void BrowseTimeFrwd(Func<DateTime, long> minTicksFactory)
        {
            if (_lineSelected == null) return;

            var selIdx = _logView.LinesCtrl.SelectedIndex;
            var content = VirtualLogFile.FilteredLines[selIdx].Content.Split('|').FirstOrDefault();

            if (!DateTime.TryParse(content, out var contentTime)) contentTime = DateTime.MinValue;

            var minTicks = minTicksFactory(contentTime);

            for (var i = selIdx + 1; i < VirtualLogFile.FilteredLines.Count; i++)
            {
                var lineContent = VirtualLogFile.FilteredLines[i].Content.Split('|').FirstOrDefault();

                if (!DateTime.TryParse(lineContent, out var itemTime)) continue;
                if (itemTime.Ticks <= minTicks) continue;
                _logView.LinesCtrl.SelectedItem = VirtualLogFile.FilteredLines[i];
                break;
            }
        }

        #region Constructor / deconstructor

        public LogViewModel(string basePath, string filter, in bool recursive, Layout layout, LogView logView)
        {
            BasePath = basePath;
            Tail = true;
            _logView = logView;

            VirtualLogFile = new VirtualLogFile(BasePath, filter, recursive, layout);

            _totalSize = VirtualLogFile.WhenAnyValue(x => x.TotalSize)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.TotalSize);

            VirtualLogFile.WhenAnyValue(x => x.LogLevelMapFile)
                .Where(imgFileName => !string.IsNullOrWhiteSpace(imgFileName))
                .Subscribe(imgFileName =>
                {
                    var path = Path.Combine(Environment.CurrentDirectory, imgFileName);
                    var bitmap = new Bitmap(path);
                    _logView.LogLevelMap.Source = bitmap;
                });

            VirtualLogFile.WhenAnyValue(x => x.AllLines)
                .Where(x => x != null)
                .Subscribe(x =>
                {
                    VirtualLogFile.AllLines.CollectionChanged -= LinesUpdated;
                    VirtualLogFile.AllLines.CollectionChanged += LinesUpdated;
                });

            this.WhenAnyValue(x => x.LineUpdateNeeded)
                .Subscribe(changes =>
                {
                    LineUpdateNeeded = false;
                });

            VirtualLogFile.ConnectFileFilterItems()
                .Prepend(new ChangeSet<FilterItem<ScopedFile>, int>
                {
                    new Change<FilterItem<ScopedFile>, int>(ChangeReason.Add, 0,
                        new FilterItem<ScopedFile>(null, 0, "All"))
                })
                .Sort(SortExpressionComparer<FilterItem<ScopedFile>>.Ascending(t => t.Key))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _fileFilterItems)
                .Subscribe(_ =>
                {
                    if (_logView.FileFilterCtrl.SelectedItem == null)
                    {
                        _logView.FileFilterCtrl.SelectedIndex = 0;
                    }
                });
/*
            VirtualLogFile.ConnectYearFilterItems()
                .Prepend(new ChangeSet<FilterItem<int>, int>
                    {new Change<FilterItem<int>, int>(ChangeReason.Add, 0, new FilterItem<int>(0, 0, "All"))})
                .Sort(SortExpressionComparer<FilterItem<int>>.Ascending(t => t.Key))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _yearFilterItems)
                .Subscribe(_ =>
                {
*//*                    if (_logView.YearFilterCtrl.SelectedItem == null)
                    {
                        _logView.YearFilterCtrl.SelectedIndex = 0;
                    }
*//*                });
*/
/*
            VirtualLogFile.ConnectMonthFilterItems()
                .Prepend(new ChangeSet<FilterItem<int>, int>
                    {new Change<FilterItem<int>, int>(ChangeReason.Add, 0, new FilterItem<int>(0, 0, "All"))})
                .Sort(SortExpressionComparer<FilterItem<int>>.Ascending(t => t.Key))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _monthFilterItems)
                .Subscribe(_ =>
                {
*//*                    if (_logView.MonthFilterCtrl.SelectedItem == null)
                    {
                        _logView.MonthFilterCtrl.SelectedIndex = 0;
                    }
*//*                });
*/
            VirtualLogFile.ConnectLevelFilterItems()
                .Sort(SortExpressionComparer<FilterItem<int>>.Ascending(t => t.Key))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _levelFilterItems)
                .Subscribe(_ =>
                {
                    if (_logView.LevelFilterCtrl.SelectedItem == null)
                    {
                        _logView.LevelFilterCtrl.SelectedIndex = 0;
                    }
                });

            _levelFilterEnabled = this.WhenAnyValue(x => x.LevelFilterItems.Count, x => x.VirtualLogFile.IsFiltering)
                .Select(obs =>
                {
                    var (levelCount, isFiltering) = obs;
                    return !isFiltering && levelCount > 0;
                })
                .DistinctUntilChanged()
                .ToProperty(this, x => x.LevelFilterEnabled);

            _fileFilterEnabled = this.WhenAnyValue(x => x.FileFilterItems.Count, x => x.VirtualLogFile.IsFiltering)
                .Select(obs =>
                {
                    var (logFileCount, isFiltering) = obs;
                    return !isFiltering && logFileCount > 0;
                })
                .DistinctUntilChanged()
                .ToProperty(this, x => x.FileFilterEnabled);
/*
            _monthFilterEnabled = this.WhenAnyValue(x => x.MonthFilterItems.Count, x => x.VirtualLogFile.YearFilterSelected, x => x.VirtualLogFile.IsFiltering)
                .Select(obs =>
                {
                    var (monthFilterItemsCount, yearFilterSelected, isFiltering) = obs;
                    return !isFiltering && yearFilterSelected?.Key > 0 && monthFilterItemsCount > 0;
                })
                .DistinctUntilChanged()
                .ToProperty(this, x => x.MonthFilterEnabled);

            _yearFilterEnabled = this.WhenAnyValue(x => x.YearFilterItems.Count, x => x.VirtualLogFile.IsFiltering)
                .Select(obs =>
                {
                    var (yearCount, isFiltering) = obs;
                    return !isFiltering && yearCount > 0;
                })
                .DistinctUntilChanged()
                .ToProperty(this, x => x.YearFilterEnabled);
*/

            this.WhenAnyValue(x => x.VirtualLogFile.FilteredLines.Count, x => x.Tail, x => x.LineSelected)
                .Where(((int lineCount, bool tail, LineItem lineSelected) tuple) =>
                {
                    var (lineCount, tail, lineSelected) = tuple;
                    // Only scrollToLast when there is content and we are tailing and no line is selected
                    return lineCount > 0 && tail && lineSelected == null;
                })
                .Delay(new TimeSpan(100))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    _logView.LinesCtrl.ScrollIntoView(VirtualLogFile.FilteredLines.Last());
                });

            // TODO: Check if the listBox is overflowing, and only show the minimap if it is. If it is not overflowing then the colorinformation is redundant as you can already see everything in the list.
            /**
             * Steven Kirk @grokys 12:42
             * @Spiralis you can get the scrollviewer via ListBox.Scroll and check the Extent and Viewport properties: if the extent is larger than the viewport then you have an "overflow"
             */

            // TODO: Move rendering the minimap here (fromthe VirtualLogFile)
            // TODO: Append to the minimap for normal log writes (line-additions).
            // TODO: An idea could be to make the original loaded lines the Main minimap, then any added lines become the "Appended" minmap. Show these in a grid, with weight-factors according to the number of lines they represent.
            // TODO: If/when the number of appended lines is larger than i.e. 500_000 lines then recreate the main minimap and delete the appended.
            // TODO: If the filter changes, then we rebuild the minimap dynamically 

            VirtualLogFile.WhenAnyValue(x => x.FilteredLines)
                .Where(x => x != null && x.Any())
                .Delay(new TimeSpan(0, 0, 1))
                .TakeUntil(_ => _selectedLast)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    _logView.LinesCtrl.ScrollIntoView(VirtualLogFile.FilteredLines.Last());
                    //_logView.LinesCtrl.SelectedItem = VirtualLogFile.FilteredLines.Last();
                    //_selectedLast = true;
                });

            _hasLineSelection = this
                .WhenAnyValue(x => x.LinesSelected.Count, x => x > 0)
                .ObserveOn(RxApp.MainThreadScheduler);

            /*
                        _linesSelected.WhenAnyValue(x => x.Count)
                            .Where(x => x > 0)
                            .Throttle(new TimeSpan(0,0,0,0, 100))
                            .Subscribe(_ => CopySelectedLinesToClipBoard());
            */
/*
            BrowseFileBackCommand = ReactiveCommand.Create(BrowseFileBack);
            BrowseFileFrwdCommand = ReactiveCommand.Create(BrowseFileFrwd);
            BrowseTimeYearBackCommand = ReactiveCommand.Create(() =>
                BrowseTimeBack(contentTime => new DateTime(contentTime.Year, 1, 1).Ticks - 1));
            BrowseTimeYearFrwdCommand = ReactiveCommand.Create(() =>
                BrowseTimeFrwd(contentTime => new DateTime(contentTime.Year, 1, 1).AddYears(1).Ticks));
            BrowseTimeMonthBackCommand = ReactiveCommand.Create(() =>
                BrowseTimeBack(contentTime => new DateTime(contentTime.Year, contentTime.Month, 1).Ticks - 1));
            BrowseTimeMonthFrwdCommand = ReactiveCommand.Create(() =>
                BrowseTimeFrwd(contentTime => new DateTime(contentTime.Year, contentTime.Month, 1).AddMonths(1).Ticks));
*/
/*
            BrowseTimeMinuteBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, contentTime.Minute,
                    0).Ticks - 1));
            BrowseTimeMinuteFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, contentTime.Minute,
                    0).AddMinutes(1).Ticks));
            BrowseTimeSecondBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, contentTime.Minute,
                    contentTime.Second).Ticks - 1));
            BrowseTimeSecondFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, contentTime.Minute,
                    contentTime.Second).AddSeconds(1).Ticks));
            BrowseLevel1BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(1));
            BrowseLevel1FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(1));
            BrowseLevel2BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(2));
            BrowseLevel2FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(2));
            BrowseLevel3BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(3));
            BrowseLevel3FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(3));
*/

            BrowseSearchBackCommand = ReactiveCommand.Create(BrowseSearchBack);
            BrowseSearchFrwdCommand = ReactiveCommand.Create(BrowseSearchFrwd);

            BrowseLevel4BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(4));
            BrowseLevel4FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(4));
            BrowseLevel5BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(5));
            BrowseLevel5FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(5));
            BrowseLevel6BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(6));
            BrowseLevel6FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(6));
            
            BrowseTimeDayBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day).Ticks - 1));
            BrowseTimeDayFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day).AddDays(1).Ticks));
            BrowseTimeHourBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, 0, 0).Ticks - 1));
            BrowseTimeHourFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, 0, 0).AddHours(1)
                    .Ticks));
            
            CopyCommand = ReactiveCommand.CreateFromTask(CopyExecute, _hasLineSelection);
            TailCommand = ReactiveCommand.Create(TailExecute);

            DeselectCommand = ReactiveCommand.Create(DeselectExecute);
        }

        public bool _lineUpdateNeeded;
        
        public bool LineUpdateNeeded
        {
            get => _lineUpdateNeeded;
            set { this.RaiseAndSetIfChanged(ref _lineUpdateNeeded, value); }
        }

        private void LinesUpdated(object sender, NotifyCollectionChangedEventArgs e)
        {
            LineUpdateNeeded = true;
        }

        ~LogViewModel()
        {
            if (VirtualLogFile != null) VirtualLogFile.AllLines.CollectionChanged -= LinesUpdated;
            _totalSize?.Dispose();
            VirtualLogFile?.Dispose();
        }

        #endregion Constructor / deconstructor

        #region Filters

        #region LevelFilter
        private readonly ReadOnlyObservableCollection<FilterItem<int>> _levelFilterItems;
        public ReadOnlyObservableCollection<FilterItem<int>> LevelFilterItems => _levelFilterItems;
        
        private readonly ObservableAsPropertyHelper<bool> _levelFilterEnabled;
        [UsedImplicitly] public bool LevelFilterEnabled => _levelFilterEnabled.Value;
        
        private int _levelFilterSelected;
        [UsedImplicitly]
        public int LevelFilterSelected
        {
            get => _levelFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _levelFilterSelected, value);
        }
        #endregion

        #region LogFileFilter
        private readonly ReadOnlyObservableCollection<FilterItem<ScopedFile>> _fileFilterItems;
        public ReadOnlyObservableCollection<FilterItem<ScopedFile>> FileFilterItems => _fileFilterItems;

        private readonly ObservableAsPropertyHelper<bool> _fileFilterEnabled;
        [UsedImplicitly] public bool FileFilterEnabled => _fileFilterEnabled.Value;

        private ScopedFile _fileFilterSelected;
        [UsedImplicitly]
        public ScopedFile FileFilterSelected
        {
            get => _fileFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _fileFilterSelected, value);
        }
        #endregion
        
        #region MonthFilter
        private readonly ReadOnlyObservableCollection<FilterItem<int>> _monthFilterItems;
        public ReadOnlyObservableCollection<FilterItem<int>> MonthFilterItems => _monthFilterItems;
        
        private readonly ObservableAsPropertyHelper<bool> _monthFilterEnabled;
        [UsedImplicitly] public bool MonthFilterEnabled => _monthFilterEnabled.Value;

        private int _monthFilterSelected;
        [UsedImplicitly]
        public int MonthFilterSelected
        {
            get => _monthFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _monthFilterSelected, value);
        }
        #endregion
        
        #region YearFilter
        private readonly ReadOnlyObservableCollection<FilterItem<int>> _yearFilterItems;
        public ReadOnlyObservableCollection<FilterItem<int>> YearFilterItems => _yearFilterItems;

        private readonly ObservableAsPropertyHelper<bool> _yearFilterEnabled;
        [UsedImplicitly] public bool YearFilterEnabled => _yearFilterEnabled.Value;

        private FilterItem<int> _yearFilterSelected;
        [UsedImplicitly]
        public FilterItem<int> YearFilterSelected
        {
            get => _yearFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _yearFilterSelected, value);
        }
        #endregion

        #endregion Filters

        #region Search/browse
        private string _searchQuery;
        [UsedImplicitly] public string SearchQuery
        {
            get => _searchQuery;
            set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
        }

        private bool _searchIsCaseSensitive = true;
        [UsedImplicitly] public bool SearchIsCaseSensitive
        {
            get => _searchIsCaseSensitive;
            set => this.RaiseAndSetIfChanged(ref _searchIsCaseSensitive, value);
        }

        private bool _searchIsRegex = false;
        [UsedImplicitly] public bool SearchIsRegex
        {
            get => _searchIsRegex;
            set => this.RaiseAndSetIfChanged(ref _searchIsRegex, value);
        }


        public ReactiveCommand<Unit, Unit> SearchFilter { get; }
        public ReactiveCommand<Unit, Unit> SearchFilterReset { get; }
        public ReactiveCommand<Unit, Unit> BrowseSearchBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseSearchFrwdCommand { get; }

        #endregion

        #region Toolbar

/*
        public ReactiveCommand<Unit, Unit> BrowseFileBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseFileFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel1BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel1FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel2BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel2FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel3BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel3FrwdCommand { get; }
*/
        public ReactiveCommand<Unit, Unit> BrowseLevel4BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel4FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel5BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel5FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel6BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel6FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeDayBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeDayFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeHourBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeHourFrwdCommand { get; }

/*
        public ReactiveCommand<Unit, Unit> BrowseTimeMinuteBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeMinuteFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeMonthBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeMonthFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeSecondBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeSecondFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeYearBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeYearFrwdCommand { get; }
*/
        public ReactiveCommand<Unit, Unit> CopyCommand { get; }

        public ReactiveCommand<Unit, Unit> TailCommand { get; }
        public ReactiveCommand<Unit, Unit> DeselectCommand { get; }

        private void BrowseFileBack()
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.LastOrDefault(l => l.CreationTimeTicks < _lineSelected.CreationTimeTicks)
                : VirtualLogFile.FilteredLines.LastOrDefault();

            if (match == null) return;

            _logView.LinesCtrl.SelectedItem = match;
        }

        private void BrowseFileFrwd()
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.FirstOrDefault(l => l.CreationTimeTicks > _lineSelected.CreationTimeTicks)
                : VirtualLogFile.FilteredLines.FirstOrDefault();

            if (match == null) return;

            _logView.LinesCtrl.SelectedItem = match;
        }

        private void DoFindExecute(LineItem noSelectionStart, int increment, int to)
        {
            // TODO: Run the find in the background, showing a progress-bar in the UI.
            // TODO: Should allow UI to be responsive.
            // TODO: If the filter changes or another search is executed then cancel this one.
            var query = SearchQuery;
            if (string.IsNullOrWhiteSpace(query)) return;
            var isCaseSensitive = SearchIsCaseSensitive;
            var isRegex = SearchIsRegex;
            var startLine = LineSelected ?? noSelectionStart;
            var current = VirtualLogFile.FilteredLines.IndexOf(startLine);
            if (startLine == LineSelected) current += increment;

            var stringComparison = isCaseSensitive ? StringComparison.InvariantCulture : StringComparison.CurrentCultureIgnoreCase;
            
            var regexOptions = RegexOptions.Compiled;
            if (!isCaseSensitive) regexOptions |= RegexOptions.IgnoreCase;
            var regex = new Regex(query, regexOptions);

            var found = -1;
            string content = string.Empty;
            var line = -1;
            // Linear slow search
            for (var i = current; increment > 0 ? i < to : i >= to; i += increment)
            {
                line = i;
                content = VirtualLogFile.FilteredLines[i].Content;
                if (isRegex)
                {
                    var match = regex.Match(content);
                    if (match.Success) found = match.Index;
                }
                else
                {
                    found = content.IndexOf(query, stringComparison);
                }
                if (found >= 0) break;
            }

            if (found >= 0)
            {
                _logView.LinesCtrl.SelectedIndex = line;
                _logView.LinesCtrl.ScrollIntoView(line);

                // TODO: Mark the first occurrences of the word in the found line
            }
        }

        private async Task<Unit> CopyExecute()
        {
            await Task.Run(() =>
            {
                var copiedText = new StringBuilder();
                foreach (var lineItem in LinesSelected) copiedText.AppendLine(lineItem.Content);

                Clipboard.SetText(copiedText.ToString());
                Logger.Debug("Copied {Count} lines to the clipboard", LinesSelected.Count);
            });
            return default;
        }

        private void TailExecute()
        {
            Tail = !Tail;
        }

        private void DeselectExecute()
        {
            if (LineSelected != null)
            {
                _logView.LinesCtrl.SelectedItems = null;
            }
        }

        #endregion Toolbar
    }
}
