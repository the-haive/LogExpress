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
        private bool _tail;
        private LogView _logView;
        private bool _selectedLast;
        private IObservable<bool> _canCopy;

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

            this.WhenAnyValue(x => x.Lines)
                .Where(x => x != null && x.Any())
                .Delay(new TimeSpan(0,0,1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .TakeUntil(_ => _selectedLast)
                .Subscribe(_ =>
                {
                    _logView.ListBoxControl.SelectedItem = Lines.Last();
                    _logView.ListBoxControl.ScrollIntoView(_logView.ListBoxControl.SelectedIndex);
                    _selectedLast = true;
                });

            _canCopy = this
                .WhenAnyValue(x => x.LinesSelected.Count, x => x > 0)
                .ObserveOn(RxApp.MainThreadScheduler);

/*
            _linesSelected.WhenAnyValue(x => x.Count)
                .Where(x => x > 0)
                .Throttle(new TimeSpan(0,0,0,0, 100))
                .Subscribe(_ => CopySelectedLinesToClipBoard());
*/
            FileBrowserUpCommand = ReactiveCommand.Create(FileBrowserUpExecute);
            FileBrowserDownCommand = ReactiveCommand.Create(FileBrowserDownExecute);
            TimeBrowserYearUpCommand = ReactiveCommand.Create(TimeBrowserYearUpExecute);
            TimeBrowserYearDownCommand = ReactiveCommand.Create(TimeBrowserYearDownExecute);
            TimeBrowserMonthUpCommand = ReactiveCommand.Create(TimeBrowserMonthUpExecute);
            TimeBrowserMonthDownCommand = ReactiveCommand.Create(TimeBrowserMonthDownExecute);
            TimeBrowserDayUpCommand = ReactiveCommand.Create(TimeBrowserDayUpExecute);
            TimeBrowserDayDownCommand = ReactiveCommand.Create(TimeBrowserDayDownExecute);
            TimeBrowserHourUpCommand = ReactiveCommand.Create(TimeBrowserHourUpExecute);
            TimeBrowserHourDownCommand = ReactiveCommand.Create(TimeBrowserHourDownExecute);
            TimeBrowserMinUpCommand = ReactiveCommand.Create(TimeBrowserMinUpExecute);
            TimeBrowserMinDownCommand = ReactiveCommand.Create(TimeBrowserMinDownExecute);
            TimeBrowserSecUpCommand = ReactiveCommand.Create(TimeBrowserSecUpExecute);
            TimeBrowserSecDownCommand = ReactiveCommand.Create(TimeBrowserSecDownExecute);
            LevelBrowserTraceUpCommand = ReactiveCommand.Create(LevelBrowserTraceUpExecute);
            LevelBrowserTraceDownCommand = ReactiveCommand.Create(LevelBrowserTraceDownExecute);
            LevelBrowserDebugUpCommand = ReactiveCommand.Create(LevelBrowserDebugUpExecute);
            LevelBrowserDebugDownCommand = ReactiveCommand.Create(LevelBrowserDebugDownExecute);
            LevelBrowserInfoUpCommand = ReactiveCommand.Create(LevelBrowserInfoUpExecute);
            LevelBrowserInfoDownCommand = ReactiveCommand.Create(LevelBrowserInfoDownExecute);
            LevelBrowserWarnUpCommand = ReactiveCommand.Create(LevelBrowserWarnUpExecute);
            LevelBrowserWarnDownCommand = ReactiveCommand.Create(LevelBrowserWarnDownExecute);
            LevelBrowserErrorUpCommand = ReactiveCommand.Create(LevelBrowserErrorUpExecute);
            LevelBrowserErrorDownCommand = ReactiveCommand.Create(LevelBrowserErrorDownExecute);
            LevelBrowserFatalUpCommand = ReactiveCommand.Create(LevelBrowserFatalUpExecute);
            LevelBrowserFatalDownCommand = ReactiveCommand.Create(LevelBrowserFatalDownExecute);
            CopyCommand = ReactiveCommand.CreateFromTask(CopyExecute, _canCopy);
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

        public ReactiveCommand<Unit, Unit> FileBrowserDownCommand { get; }

        public ReactiveCommand<Unit, Unit> FileBrowserUpCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserDebugDownCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserDebugUpCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserErrorDownCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserErrorUpCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserFatalDownCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserFatalUpCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserInfoDownCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserInfoUpCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserTraceDownCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserTraceUpCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserWarnDownCommand { get; }

        public ReactiveCommand<Unit, Unit> LevelBrowserWarnUpCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserDayDownCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserDayUpCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserHourDownCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserHourUpCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserMinDownCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserMinUpCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserMonthDownCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserMonthUpCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserSecDownCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserSecUpCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserYearDownCommand { get; }

        public ReactiveCommand<Unit, Unit> TimeBrowserYearUpCommand { get; }

        public ReactiveCommand<Unit, Unit> CopyCommand { get; }

        public ReactiveCommand<Unit, Unit> TailCommand { get; }

        private void FileBrowserDownExecute()
        {
            // TODO: Implement command
        }

        private void FileBrowserUpExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserDebugDownExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserDebugUpExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserErrorDownExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserErrorUpExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserFatalDownExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserFatalUpExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserInfoDownExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserInfoUpExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserTraceDownExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserTraceUpExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserWarnDownExecute()
        {
            // TODO: Implement command
        }

        private void LevelBrowserWarnUpExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserDayDownExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserDayUpExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserHourDownExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserHourUpExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserMinDownExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserMinUpExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserMonthDownExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserMonthUpExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserSecDownExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserSecUpExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserYearDownExecute()
        {
            // TODO: Implement command
        }

        private void TimeBrowserYearUpExecute()
        {
            // TODO: Implement command
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
            // TODO: Implement command
        }

        #endregion
    }
}
