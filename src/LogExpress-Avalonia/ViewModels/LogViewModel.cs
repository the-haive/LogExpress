using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using ByteSizeLib;
using LogExpress.Models;
using LogExpress.Services;
using LogExpress.Views;
using ReactiveUI;
using Serilog;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace LogExpress.ViewModels
{
    public class LogViewModel : ViewModelBase
    {
        public static readonly ILogger Logger = Log.ForContext<LogViewModel>();
        private readonly ObservableAsPropertyHelper<string> _humanTotalSize;
        private readonly ObservableAsPropertyHelper<bool> _isAnalyzed;
        private readonly ObservableAsPropertyHelper<string> _logListHeader;
        private readonly ObservableAsPropertyHelper<long> _totalSize;
        private readonly ObservableAsPropertyHelper<Dictionary<byte, long>> _logLevelStats;
        public readonly VirtualLogFile VirtualLogFile;
        private string _basePath;
        private string _filter;
        private ObservableCollection<LineItem> _lines;
        private LineItem _lineSelected;
        private ObservableCollection<LineItem> _linesSelected = new ObservableCollection<LineItem>();
        private string _logLevelMapFile;
        private bool _tail = true;
        private LogView _logView;
        private bool _selectedLast;
        private IObservable<bool> _hasSelection;

        public string BasePath
        {
            get => _basePath;
            set => this.RaiseAndSetIfChanged(ref _basePath, value);
        }

        public string Filter
        {
            get => _filter;
            set => this.RaiseAndSetIfChanged(ref _filter, value);
        }

        public string HumanTotalSize => _humanTotalSize.Value;

        public Dictionary<byte, long> LogLevelStats => _logLevelStats.Value;

        public bool IsAnalyzed => _isAnalyzed != null && _isAnalyzed.Value;

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

        public ObservableCollection<LineItem> LinesSelected
        {
            get => _linesSelected;
            set => this.RaiseAndSetIfChanged(ref _linesSelected, value);
        }

        public ReadOnlyObservableCollection<ScopedFile> LogFiles { get; }

        public string LogLevelMapFile
        {
            get => _logLevelMapFile;
            set => this.RaiseAndSetIfChanged(ref _logLevelMapFile, value);
        }

        public string LogListHeader => _logListHeader.Value;

        public bool Tail
        {
            get => _tail;
            set => this.RaiseAndSetIfChanged(ref _tail, value);
        }

        public long TotalSize => _totalSize.Value;

        #region Constructor / deconstructor

        public LogViewModel(string basePath, string filter, in bool recursive, LogView logView)
        {
            BasePath = basePath;
            Filter = filter;
            Tail = true;
            _logView = logView;

            VirtualLogFile = new VirtualLogFile(BasePath, Filter, recursive);
            LogFiles = VirtualLogFile.LogFiles;

            _totalSize = VirtualLogFile.WhenAnyValue(x => x.TotalSize)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.TotalSize);

            _logLevelStats = VirtualLogFile.WhenAnyValue(x => x.LogLevelStats)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.LogLevelStats);

            _humanTotalSize = VirtualLogFile.WhenAnyValue(x => x.TotalSize)
                .Select(x => ByteSize.FromBytes(x).ToString())
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.HumanTotalSize);

            _isAnalyzed = VirtualLogFile.WhenAnyValue(x => x.IsAnalyzed)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.IsAnalyzed);

            VirtualLogFile.WhenAnyValue(x => x.LogLevelMapFile)
                .Where(imgFileName => !string.IsNullOrWhiteSpace(imgFileName))
                .Subscribe(imgFileName =>
                {
                    var path = Path.Combine(Environment.CurrentDirectory, imgFileName);
                    var bitmap = new Bitmap(path);
                    _logView.LogLevelMap.Source = bitmap;
                });

            _logListHeader = this.WhenAnyValue(x => x.LogFiles.Count)
                .DistinctUntilChanged()
                .Select(x => $"{x} scoped log-files (click to toggle)")
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.LogListHeader);

            this.WhenAnyValue(x => x.IsAnalyzed)
                .Where(x => x)
                .Subscribe(_ =>
                {
                    Lines = VirtualLogFile.Lines;
                });

            this.WhenAnyValue(x => x.Lines.Count, x => x.Tail)
                .Where(((int lineCount, bool tail) tuple) =>
                {
                    var (lineCount, tail) = tuple;
                    return lineCount > 0 && tail;
                })
                .Delay(new TimeSpan(100))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    //_logView.ListBoxControl.SelectedItem = 
                    //_logView.ListBoxControl.ScrollIntoView(_logView.ListBoxControl.SelectedItem);
                    _logView.ListBoxControl.ScrollIntoView(Lines.Last());
                });

            this.WhenAnyValue(x => x.Lines)
                .Where(x => x != null && x.Any())
                .Delay(new TimeSpan(0,0,1))
                .TakeUntil(_ => _selectedLast)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    _logView.ListBoxControl.SelectedItem = Lines.Last();
                    //_logView.ListBoxControl.ScrollIntoView(_logView.ListBoxControl.SelectedItem);
                    _selectedLast = true;
                });

            _hasSelection = this
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
            BrowseTimeYearBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime => new DateTime(contentTime.Year, 1, 1).Ticks - 1));
            BrowseTimeYearFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime => new DateTime(contentTime.Year, 1, 1).AddYears(1).Ticks));
            BrowseTimeMonthBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime => new DateTime(contentTime.Year, contentTime.Month, 1).Ticks - 1));
            BrowseTimeMonthFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime => new DateTime(contentTime.Year, contentTime.Month, 1).AddMonths(1).Ticks));
            BrowseTimeDayBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime => new DateTime(contentTime.Year, contentTime.Month, contentTime.Day).Ticks - 1));
            BrowseTimeDayFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime => new DateTime(contentTime.Year, contentTime.Month, contentTime.Day).AddDays(1).Ticks));
            BrowseTimeHourBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime => new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, 0, 0).Ticks - 1));
            BrowseTimeHourFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime => new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, 0, 0).AddHours(1).Ticks));
            BrowseTimeMinuteBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime => new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, contentTime.Minute, 0).Ticks - 1));
            BrowseTimeMinuteFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime => new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, contentTime.Minute, 0).AddMinutes(1).Ticks));
            BrowseTimeSecondBackCommand = ReactiveCommand.Create(() => BrowseTimeBack(contentTime => new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, contentTime.Minute, contentTime.Second).Ticks - 1));
            BrowseTimeSecondFrwdCommand = ReactiveCommand.Create(() => BrowseTimeFrwd(contentTime => new DateTime(contentTime.Year, contentTime.Month, contentTime.Day, contentTime.Hour, contentTime.Minute, contentTime.Second).AddSeconds(1).Ticks));
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
            CopyCommand = ReactiveCommand.CreateFromTask(CopyExecute, _hasSelection);
            TailCommand = ReactiveCommand.Create(TailExecute);
        }


        ~LogViewModel()
        {
            _isAnalyzed?.Dispose();
            _totalSize?.Dispose();
            VirtualLogFile?.Dispose();
        }

        #endregion Constructor / deconstructor

        #region Toolbar

        public ReactiveCommand<Unit, Unit> BrowseFileFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseFileBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel2FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel2BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel5FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel5BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel6FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel6BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel3FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel3BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel1FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel1BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel4FrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseLevel4BackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeDayFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeDayBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeHourFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeHourBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeMinuteFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeMinuteBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeMonthFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeMonthBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeSecondFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeSecondBackCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeYearFrwdCommand { get; }

        public ReactiveCommand<Unit, Unit> BrowseTimeYearBackCommand { get; }

        public ReactiveCommand<Unit, Unit> CopyCommand { get; }

        public ReactiveCommand<Unit, Unit> TailCommand { get; }

        private void BrowseFileFrwd()
        {
            var match = _lineSelected != null 
                ? Lines.FirstOrDefault(l => l.CreationTimeTicks > _lineSelected.CreationTimeTicks) 
                : Lines.FirstOrDefault();

            if (match == null) return;

            _logView.ListBoxControl.SelectedItem = match;
        }

        private void BrowseFileBack()
        {
            var match = _lineSelected != null 
                ? Lines.LastOrDefault(l => l.CreationTimeTicks < _lineSelected.CreationTimeTicks)
                : Lines.LastOrDefault();

            if (match == null) return;

            _logView.ListBoxControl.SelectedItem = match;
        }


        private async Task<Unit> CopyExecute()
        {
            await Task.Run(() =>
            {
                var copiedText = new StringBuilder();
                foreach (var lineItem in LinesSelected)
                {
                    copiedText.AppendLine(lineItem.Content);
                }

                TextCopy.Clipboard.SetText(copiedText.ToString());
                Logger.Debug("Copied {Count} lines to the clipboard", LinesSelected.Count);
            });
            return default;
        }

        private void TailExecute()
        {
            Tail = !Tail;
        }

        #endregion
        private void BrowseLevelFrwd(int logLevel)
        {
            var match = _lineSelected != null
                ? Lines.FirstOrDefault(l =>
                    l.LogLevel == logLevel && (l.CreationTimeTicks > _lineSelected.CreationTimeTicks ||
                                               l.LineNumber > _lineSelected.LineNumber))
                : Lines.FirstOrDefault();

            if (match == null) return;

            _logView.ListBoxControl.SelectedItem = match;
        }

        private void BrowseLevelBack(int logLevel)
        {
            var match = _lineSelected != null
                ? Lines.LastOrDefault(l =>
                    l.LogLevel == logLevel && (l.CreationTimeTicks < _lineSelected.CreationTimeTicks ||
                                               l.LineNumber < _lineSelected.LineNumber))
                : Lines.LastOrDefault();

            if (match == null) return;

            _logView.ListBoxControl.SelectedItem = match;
        }

        private void BrowseTimeFrwd(Func<DateTime, long> minTicksFactory)
        {
            if (_lineSelected == null) return;

            var selIdx = _logView.ListBoxControl.SelectedIndex;
            var content = Lines[selIdx].Content.Split('|').FirstOrDefault();

            if (!DateTime.TryParse(content, out var contentTime))
            {
                contentTime = DateTime.MinValue;
            }

            var minTicks = minTicksFactory(contentTime); 

            for (var i = selIdx + 1; i < Lines.Count; i++)
            {
                var lineContent = Lines[i].Content.Split('|').FirstOrDefault();

                if (!DateTime.TryParse(lineContent, out var itemTime)) continue;
                if (itemTime.Ticks <= minTicks) continue;
                _logView.ListBoxControl.SelectedItem = Lines[i];
                break;
            }
        }
        private void BrowseTimeBack(Func<DateTime, long> maxTicksFactory)
        {
            if (_lineSelected == null) return;

            var selIdx = _logView.ListBoxControl.SelectedIndex;
            var content = Lines[selIdx].Content.Split('|').FirstOrDefault();

            if (!DateTime.TryParse(content, out var contentTime))
            {
                contentTime = DateTime.MaxValue;
            }

            var maxTicks = maxTicksFactory(contentTime); 

            for (var i = selIdx - 1; i >= 0; i--)
            {
                var lineContent = Lines[i].Content.Split('|').FirstOrDefault();

                if (!DateTime.TryParse(lineContent, out var itemTime)) continue;
                if (itemTime.Ticks >= maxTicks) continue;
                _logView.ListBoxControl.SelectedItem = Lines[i];
                break;
            }
        }
    }
}
