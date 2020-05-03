using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
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

        private void BrowseLevelBack(int logLevel)
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.LastOrDefault(l =>
                    l.LogLevel == logLevel && (l.CreationTimeTicks < _lineSelected.CreationTimeTicks ||
                                               l.LineNumber < _lineSelected.LineNumber))
                : VirtualLogFile.FilteredLines.LastOrDefault();

            if (match == null) return;

            _logView.ListBoxControl.SelectedItem = match;
        }

        private void BrowseLevelFrwd(int logLevel)
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.FirstOrDefault(l =>
                    l.LogLevel == logLevel && (l.CreationTimeTicks > _lineSelected.CreationTimeTicks ||
                                               l.LineNumber > _lineSelected.LineNumber))
                : VirtualLogFile.FilteredLines.FirstOrDefault();

            if (match == null) return;

            _logView.ListBoxControl.SelectedItem = match;
        }

        private void BrowseTimeBack(Func<DateTime, long> maxTicksFactory)
        {
            if (_lineSelected == null) return;

            var selIdx = _logView.ListBoxControl.SelectedIndex;
            var content = VirtualLogFile.FilteredLines[selIdx].Content.Split('|').FirstOrDefault();

            if (!DateTime.TryParse(content, out var contentTime)) contentTime = DateTime.MaxValue;

            var maxTicks = maxTicksFactory(contentTime);

            for (var i = selIdx - 1; i >= 0; i--)
            {
                var lineContent = VirtualLogFile.FilteredLines[i].Content.Split('|').FirstOrDefault();

                if (!DateTime.TryParse(lineContent, out var itemTime)) continue;
                if (itemTime.Ticks >= maxTicks) continue;
                _logView.ListBoxControl.SelectedItem = VirtualLogFile.FilteredLines[i];
                break;
            }
        }

        private void BrowseTimeFrwd(Func<DateTime, long> minTicksFactory)
        {
            if (_lineSelected == null) return;

            var selIdx = _logView.ListBoxControl.SelectedIndex;
            var content = VirtualLogFile.FilteredLines[selIdx].Content.Split('|').FirstOrDefault();

            if (!DateTime.TryParse(content, out var contentTime)) contentTime = DateTime.MinValue;

            var minTicks = minTicksFactory(contentTime);

            for (var i = selIdx + 1; i < VirtualLogFile.FilteredLines.Count; i++)
            {
                var lineContent = VirtualLogFile.FilteredLines[i].Content.Split('|').FirstOrDefault();

                if (!DateTime.TryParse(lineContent, out var itemTime)) continue;
                if (itemTime.Ticks <= minTicks) continue;
                _logView.ListBoxControl.SelectedItem = VirtualLogFile.FilteredLines[i];
                break;
            }
        }

        #region Constructor / deconstructor

        public LogViewModel(string basePath, string filter, in bool recursive, LogView logView)
        {
            BasePath = basePath;
            Tail = true;
            _logView = logView;

            VirtualLogFile = new VirtualLogFile(BasePath, filter, recursive);

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


/*            this.WhenAnyValue(x => x.LogFilesFilter)
                .Subscribe(_ => { VirtualLogFile.LogFileFilterSelectedOLD = LogFilesFilter?.FirstOrDefault(); });

            _logFilesFilter = VirtualLogFile.WhenAnyValue(x => x.LogFiles.Count)
                .Select(scopedFiles =>
                {
                    var list = new List<LogFileFilter> {new LogFileFilter("All log-files")};
                    list.AddRange(VirtualLogFile.LogFiles?.Select(scopedFile => new LogFileFilter(scopedFile)));
                    return list;
                }).ToProperty(this, x => x.LogFilesFilter);
*/
            this.WhenAnyValue(x => x.VirtualLogFile.FilteredLines.Count, x => x.Tail)
                .Where(((int lineCount, bool tail) tuple) =>
                {
                    var (lineCount, tail) = tuple;
                    return lineCount > 0 && tail;
                })
                .Delay(new TimeSpan(100))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    _logView.ListBoxControl.ScrollIntoView(VirtualLogFile.FilteredLines.Last());
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
                    _logView.ListBoxControl.SelectedItem = VirtualLogFile.FilteredLines.Last();
                    _selectedLast = true;
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
            BrowseTimeDayBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day).Ticks - 1));
            BrowseTimeDayFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day).AddDays(1).Ticks));
            BrowseTimeHourBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, 0, 0).Ticks - 1));
            BrowseTimeHourFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime =>
                new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, 0, 0).AddHours(1)
                    .Ticks));
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
            BrowseLevel4BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(4));
            BrowseLevel4FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(4));
            BrowseLevel5BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(5));
            BrowseLevel5FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(5));
            BrowseLevel6BackCommand = ReactiveCommand.Create(() => BrowseLevelBack(6));
            BrowseLevel6FrwdCommand = ReactiveCommand.Create(() => BrowseLevelFrwd(6));
            CopyCommand = ReactiveCommand.CreateFromTask(CopyExecute, _hasLineSelection);
            TailCommand = ReactiveCommand.Create(TailExecute);
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

        #region Toolbar

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseFileBackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseFileFrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel1BackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel1FrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel2BackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel2FrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel3BackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel3FrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel4BackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel4FrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel5BackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel5FrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel6BackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseLevel6FrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeDayBackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeDayFrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeHourBackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeHourFrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeMinuteBackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeMinuteFrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeMonthBackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeMonthFrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeSecondBackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeSecondFrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeYearBackCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> BrowseTimeYearFrwdCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> CopyCommand { get; }

        [UsedImplicitly] public ReactiveCommand<Unit, Unit> TailCommand { get; }

        private void BrowseFileBack()
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.LastOrDefault(l => l.CreationTimeTicks < _lineSelected.CreationTimeTicks)
                : VirtualLogFile.FilteredLines.LastOrDefault();

            if (match == null) return;

            _logView.ListBoxControl.SelectedItem = match;
        }

        private void BrowseFileFrwd()
        {
            var match = _lineSelected != null
                ? VirtualLogFile.FilteredLines.FirstOrDefault(l => l.CreationTimeTicks > _lineSelected.CreationTimeTicks)
                : VirtualLogFile.FilteredLines.FirstOrDefault();

            if (match == null) return;

            _logView.ListBoxControl.SelectedItem = match;
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

        #endregion Toolbar
    }
}
