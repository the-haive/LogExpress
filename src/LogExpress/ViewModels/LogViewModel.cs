using System;
using System.Collections.Generic;
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

        private readonly LogView _logView;
        private readonly ObservableAsPropertyHelper<long> _totalSize;

        [UsedImplicitly] public VirtualLogFile VirtualLogFile { get; set; }

        private string _basePath;
        private ObservableCollection<LineItem> _lines;
        private LineItem _lineSelected;
        private ObservableCollection<LineItem> _linesSelected = new ObservableCollection<LineItem>();
        private string _severityMapFile;
        private bool _selectedLast;

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

        public string SeverityMapFile
        {
            get => _severityMapFile;
            set => this.RaiseAndSetIfChanged(ref _severityMapFile, value);
        }

        [UsedImplicitly] public long TotalSize => _totalSize.Value;

        #region Constructor / deconstructor

        public LogViewModel(string basePath, string filter, in bool recursive, Layout layout, LogView logView)
        {
            BasePath = basePath;
            Tail = true;
            _logView = logView;

            Severity1Name = "TRACE";
            Severity2Name = "DEBUG";
            Severity3Name = "INFO";
            Severity4Name = "WARN";
            Severity4Name = "ERROR";
            Severity4Name = "FATAL";

            TimeFilterItems = new ReadOnlyObservableCollection<FilterItem<int>>(new ObservableCollection<FilterItem<int>>(new List<FilterItem<int>>()
            {
                new FilterItem<int>(0, 0,"Anytime"),
                new FilterItem<int>(1, 1,"Last Hour"),
                new FilterItem<int>(2, 2,"Last Day"),
                new FilterItem<int>(3, 3,"Last Week"),
                new FilterItem<int>(4, 4,"Last Month"),
                new FilterItem<int>(5, 5,"Custom Range..."),
            }));

            VirtualLogFile = new VirtualLogFile(BasePath, filter, recursive, layout);

            _totalSize = VirtualLogFile.WhenAnyValue(x => x.TotalSize)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.TotalSize);

            VirtualLogFile.WhenAnyValue(x => x.SeverityMapFile)
                .Where(imgFileName => !string.IsNullOrWhiteSpace(imgFileName))
                .Subscribe(imgFileName =>
                {
                    var path = Path.Combine(Environment.CurrentDirectory, imgFileName);
                    var bitmap = new Bitmap(path);
                    _logView.SeverityMap.Source = bitmap;
                });

            VirtualLogFile.WhenAnyValue(x => x.Layout)
                .Where(obsLayout => obsLayout != null)
                .Subscribe(obsLayout =>
                {
                    Severity1Name = obsLayout.Severities[1];
                    Severity2Name = obsLayout.Severities[2];
                    Severity3Name = obsLayout.Severities[3];
                    Severity4Name = obsLayout.Severities[4];
                    Severity5Name = obsLayout.Severities[5];
                    Severity6Name = obsLayout.Severities[6];
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

            _fileFilterEnabled = this.WhenAnyValue(x => x.FileFilterItems.Count, x => x.VirtualLogFile.IsFiltering)
                .Select(obs =>
                {
                    var (logFileCount, isFiltering) = obs;
                    return !isFiltering && logFileCount > 0;
                })
                .DistinctUntilChanged()
                .ToProperty(this, x => x.FileFilterEnabled);

            VirtualLogFile.ConnectSeverityFilterItems()
                .Sort(SortExpressionComparer<FilterItem<int>>.Ascending(t => t.Key))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _severityFilterItems)
                .Subscribe(_ =>
                {
                    if (_logView.SeverityFilterCtrl.SelectedItem == null)
                    {
                        _logView.SeverityFilterCtrl.SelectedIndex = 0;
                    }
                });

            _severityFilterEnabled = this.WhenAnyValue(x => x.SeverityFilterItems.Count, x => x.VirtualLogFile.IsFiltering)
                .Select(obs =>
                {
                    var (severityCount, isFiltering) = obs;
                    return !isFiltering && severityCount > 0;
                })
                .DistinctUntilChanged()
                .ToProperty(this, x => x.SeverityFilterEnabled);

            _timeFilterEnabled = this.WhenAnyValue(x => x.TimeFilterItems, x => x.VirtualLogFile.IsFiltering)
                .Select(obs =>
                {
                    var (timeFilterItems, isFiltering) = obs;
                    return !isFiltering && timeFilterItems.Count > 0;
                })
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Do(_ =>
                {
                    if (_logView.TimeFilterCtrl.SelectedItem == null)
                    {
                        TimeFilterSelected = TimeFilterItems.First();
                        //_logView.TimeFilterCtrl.SelectedIndex = 0;
                    }
                })
                .ToProperty(this, x => x.TimeFilterEnabled);

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
                .WhenAnyValue(x => x.LineSelected).Select(x => x != null);

            BrowseSearchBackCommand = ReactiveCommand.Create(BrowseSearchBack);
            BrowseSearchFrwdCommand = ReactiveCommand.Create(BrowseSearchFrwd);

            BrowseSeverity4BackCommand = ReactiveCommand.Create(() => BrowseSeverityBack(4));
            BrowseSeverity4FrwdCommand = ReactiveCommand.Create(() => BrowseSeverityFrwd(4));
            BrowseSeverity5BackCommand = ReactiveCommand.Create(() => BrowseSeverityBack(5));
            BrowseSeverity5FrwdCommand = ReactiveCommand.Create(() => BrowseSeverityFrwd(5));
            BrowseSeverity6BackCommand = ReactiveCommand.Create(() => BrowseSeverityBack(6));
            BrowseSeverity6FrwdCommand = ReactiveCommand.Create(() => BrowseSeverityFrwd(6));
            
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

        #region FileFilter
        private readonly ReadOnlyObservableCollection<FilterItem<ScopedFile>> _fileFilterItems;
        public ReadOnlyObservableCollection<FilterItem<ScopedFile>> FileFilterItems => _fileFilterItems;

        private readonly ObservableAsPropertyHelper<bool> _fileFilterEnabled;
        [UsedImplicitly] public bool FileFilterEnabled => _fileFilterEnabled.Value;

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

        #endregion

        #region SeverityFilter

        private readonly ReadOnlyObservableCollection<FilterItem<int>> _severityFilterItems;
        public ReadOnlyObservableCollection<FilterItem<int>> SeverityFilterItems => _severityFilterItems;
        
        private readonly ObservableAsPropertyHelper<bool> _severityFilterEnabled;
        [UsedImplicitly] public bool SeverityFilterEnabled => _severityFilterEnabled.Value;
        
        private void BrowseSeverityBack(int level)
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.LastOrDefault(l =>
                    l.Severity == level && (l.CreationTimeTicks < _lineSelected.CreationTimeTicks ||
                                               l.LineNumber < _lineSelected.LineNumber))
                : VirtualLogFile.FilteredLines.LastOrDefault();

            if (match == null) return;

            _logView.LinesCtrl.SelectedItem = match;
        }

        private void BrowseSeverityFrwd(int level)
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.FirstOrDefault(l =>
                    l.Severity == level && (l.CreationTimeTicks > _lineSelected.CreationTimeTicks ||
                                               l.LineNumber > _lineSelected.LineNumber))
                : VirtualLogFile.FilteredLines.FirstOrDefault();

            if (match == null) return;

            _logView.LinesCtrl.SelectedItem = match;
        }
        
        #endregion


        #region TimeFilter

        private ReadOnlyObservableCollection<FilterItem<int>> _timeFilterItems;

        [UsedImplicitly] public ReadOnlyObservableCollection<FilterItem<int>> TimeFilterItems
        {
            get => _timeFilterItems;
            set => this.RaiseAndSetIfChanged(ref _timeFilterItems, value);
        }

        private readonly ObservableAsPropertyHelper<bool> _timeFilterEnabled;
        [UsedImplicitly] public bool TimeFilterEnabled => _timeFilterEnabled.Value;

        private FilterItem<int> _timeFilterSelected;
        [UsedImplicitly]
        public FilterItem<int> TimeFilterSelected
        {
            get => _timeFilterSelected;
            set => this.RaiseAndSetIfChanged(ref _timeFilterSelected, value);
        }

        #endregion
        
        #region Search Filter & Browse

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

        internal void BrowseSearchBack()
        {
            DoFindExecute(VirtualLogFile.FilteredLines.Last(), -1, 0);
        }

        internal void BrowseSearchFrwd()
        {
            DoFindExecute(VirtualLogFile.FilteredLines.First(), +1, VirtualLogFile.FilteredLines.Count);
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


        #endregion

        #region SeverityBrowse

        private string _severity1Name;

        public string Severity1Name
        {
            get => _severity1Name;
            set => this.RaiseAndSetIfChanged(ref _severity1Name, value);
        }

        private string _severity2Name;

        public string Severity2Name
        {
            get => _severity2Name;
            set => this.RaiseAndSetIfChanged(ref _severity2Name, value);
        }

        private string _severity3Name;

        public string Severity3Name
        {
            get => _severity3Name;
            set => this.RaiseAndSetIfChanged(ref _severity3Name, value);
        }

        private string _severity4Name;

        public string Severity4Name
        {
            get => _severity4Name;
            set => this.RaiseAndSetIfChanged(ref _severity4Name, value);
        }

        private string _severity5Name;

        public string Severity5Name
        {
            get => _severity5Name;
            set => this.RaiseAndSetIfChanged(ref _severity5Name, value);
        }

        private string _severity6Name;

        public string Severity6Name
        {
            get => _severity6Name;
            set => this.RaiseAndSetIfChanged(ref _severity6Name, value);
        }

        public ReactiveCommand<Unit, Unit> BrowseSeverity4BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseSeverity4FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseSeverity5BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseSeverity5FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseSeverity6BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseSeverity6FrwdCommand { get; }

        #endregion

        #region TimeBrowse

        public ReactiveCommand<Unit, Unit> BrowseTimeDayBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeDayFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeHourBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeHourFrwdCommand { get; }

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

        #endregion

        #region Tools
        private bool _tail = true;

        public ReactiveCommand<Unit, Unit> CopyCommand { get; }

        public ReactiveCommand<Unit, Unit> TailCommand { get; }

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

        [UsedImplicitly]
        public bool Tail
        {
            get => _tail;
            set => this.RaiseAndSetIfChanged(ref _tail, value);
        }

        private void TailExecute()
        {
            Tail = !Tail;
        }

        #endregion

        public ReactiveCommand<Unit, Unit> DeselectCommand { get; }

        
        private void DeselectExecute()
        {
            if (LineSelected != null)
            {
                _logView.LinesCtrl.SelectedItems = null;
            }
        }

    }
}
