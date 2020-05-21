using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Media;
using DynamicData;
using DynamicData.Binding;
using LogExpress.Models;
using LogExpress.Services;
using LogExpress.Views;
using ReactiveUI;
using Serilog;

namespace LogExpress.ViewModels
{
    public class ConfigureTimestampViewModel : ViewModelBase, IDisposable
    {
        private static readonly ILogger Logger = Log.ForContext<ConfigureTimestampViewModel>();
        private static readonly Brush ErrorColor = new SolidColorBrush(Colors.Red);
        private IDisposable _fileMonitorSubscription;
        private IDisposable _parseFilesSubscription;
        private ScopedFileMonitor _scopedFileMonitor;
        private IDisposable _timestampSubscription;

        public ConfigureTimestampViewModel(ConfigureTimestampView view, ScopeSettings scopeSettings, TimestampSettings timestampSettings)
        {
            View = view;
            ScopeSettings = scopeSettings;
            TimestampSettings = timestampSettings ?? new TimestampSettings();
        }

        public ScopeSettings ScopeSettings { get; set; }

        public TimestampSettings TimestampSettings { get; set; }

        public void Init()
        {
            if (TimestampSettings != null)
            {
                TimestampLineSelectionStart = TimestampSettings.TimestampStart;
                TimestampLineSelectionEnd = TimestampSettings.TimestampStart + TimestampSettings.TimestampLength;
                TimestampFormat = TimestampSettings.TimestampFormat;
            }

            ConfigureScopeCommand = ReactiveCommand.Create(ConfigureScopeExecute);
            CancelCommand = ReactiveCommand.Create(CancelExecute);
            ConfigureSeverityCommand = ReactiveCommand.Create(ConfigureSeverityExecute);

            _timestampSubscription = this.WhenAnyValue(
                    x => x.TimestampLineSelectionStart,
                    x => x.TimestampLineSelectionEnd,
                    x => x.TimestampFormat)
/*                .Where(obs =>
                {
                    var (start, end, _) = obs;
                    return start > -1 && end != start;
                })
*/                .Throttle(TimeSpan.FromMilliseconds(100))
                .Subscribe(_ => UpdateTimestampSettings());

            _scopedFileMonitor = new ScopedFileMonitor(ScopeSettings.Folder, new List<string> {ScopeSettings.Pattern}, ScopeSettings.Recursive);

            _fileMonitorSubscription?.Dispose();
            _fileMonitorSubscription =
                _scopedFileMonitor.Connect()
                    .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(LogFilesReady);

            _parseFilesSubscription = this.WhenAnyValue(
                    x => x.LogFiles, 
                    x => x.TimestampSettings.TimestampStart,
                    x => x.TimestampSettings.TimestampLength,
                    x => x.TimestampSettings.TimestampFormat)
                .Where(obs =>
                {
                    var (files, _, _, _) = obs;
                    return files != null && files.Count > 0;
                })
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateFileSamples());

            _lineSampleSubscription = this.WhenAnyValue(x => x.FileSampleSelectedItem)
                .Subscribe(UpdateLineSamples);
        }

        private void UpdateLineSamples(FileSample selectedFile)
        {
            LineSamples.Clear();

            if (selectedFile == null) return;

            using var fileStream = new FileStream(selectedFile.ScopedFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream, selectedFile.ScopedFile.Encoding);

            LineItem.LogFiles = new ReadOnlyObservableCollection<ScopedFile>(LogFiles);

            var lines = new ObservableCollection<LineItem>();
            ScopedFile.ReadFileLinePositions(lines, reader, selectedFile.ScopedFile, maxLinesToRead: 100);
            
            var tsStart = selectedFile.ScopedFile.TimestampSettings.TimestampStart;
            var tsLength = selectedFile.ScopedFile.TimestampSettings.TimestampLength;
            
            foreach (var lineItem in lines)
            {
                var content = lineItem.Content;
                var buffer = content.Substring(tsStart, tsLength).ToCharArray();
                var timestamp = selectedFile.ScopedFile.GetTimestamp(buffer);

                var lineSample = new LineSample()
                {
                    Timestamp = $"{timestamp:F}",
                    Content = content
                };

                LineSamples.Add(lineSample);
            }
            Logger.Debug("Have {Count} lines from file {Filename}", LineSamples.Count, selectedFile.RelativeFullName);
        }

        private void UpdateTimestampSettings()
        {
            var tsStart = Math.Min(TimestampLineSelectionStart, TimestampLineSelectionEnd);
            var tsEnd = Math.Max(TimestampLineSelectionStart, TimestampLineSelectionEnd);
            var tsLength = tsEnd - tsStart;

            Logger.Debug("Changing timestamp settings: Start={Start} Length:{Length} Format='{Format}'", tsStart, tsLength, TimestampFormat);
            TimestampSettings.TimestampStart = tsStart;
            TimestampSettings.TimestampLength = tsLength;
            TimestampSettings.TimestampFormat = TimestampFormat;
        }

        private void UpdateFileSamples()
        {
            //LineItem.LogFiles = new ReadOnlyObservableCollection<ScopedFile>(LogFiles);
            Logger.Debug(
                "Updating the verification tables, based on timestamp settings: Start={Start} Length:{Length} Format='{Format}'",
                TimestampSettings.TimestampStart, TimestampSettings.TimestampLength, TimestampSettings.TimestampFormat);

            // Iterate all files to get the Start and End dates. StartDate is used as order in the next for-loop, where we check the date-consistency.
            FoundTimestamp = true;
            foreach (var scopedFile in LogFiles)
            {
                scopedFile.TimestampSettings = TimestampSettings;
                scopedFile.ResetStartAndEndDates();
                try
                {
                    FoundTimestamp = FoundTimestamp && scopedFile.StartDate > DateTime.MinValue;
                    FoundTimestamp = FoundTimestamp && scopedFile.EndDate < DateTime.MaxValue;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error while trying to get timestamps from disk. Filename: {}", scopedFile.FullName);
                }
            }

            // Ite
            FileSamples.Clear();
            var isFirst = true;
            var lastDate = DateTime.MinValue;
            SeqFilesVerified = true;
            var msComponents = new List<int>();
            FileSampleSelectedItem = null;
            foreach (var scopedFile in LogFiles.OrderBy(f => FoundTimestamp ? f.StartDate : f.CreationTime))
            {

                var seqStartError = scopedFile.StartDate == DateTime.MinValue || scopedFile.StartDate < lastDate;
                var seqEndError = scopedFile.EndDate == DateTime.MaxValue || scopedFile.EndDate < scopedFile.StartDate;
                SeqFilesVerified = SeqFilesVerified && !seqStartError && !seqEndError;

                if (isFirst)
                {
                    TimestampLine = LineItem.ContentFromDisk(scopedFile, 0);

                    TimestampParseResult = scopedFile.StartDate == DateTime.MinValue ? "Unable to get/parse date" : $"{scopedFile.StartDate:F}";
                    isFirst = false;
                }

                var fileSample = new FileSample
                {
                    ScopedFile = scopedFile,
                    FullName = scopedFile.FullName,
                    RelativeFullName = scopedFile.RelativeFullName,
                    StartDate = $"{scopedFile.StartDate}",
                    EndDate = $"{scopedFile.EndDate}",
                    SeqStartError = seqStartError,
                    SeqEndError = seqEndError
                };
                FileSamples.Add(fileSample);

                if (FileSampleSelectedItem == null && (seqStartError || seqEndError)) FileSampleSelectedItem = fileSample;

                lastDate = scopedFile.EndDate;

                // For now only sampling the start and end
                // TODO: Sample first X lines from each file
                msComponents.Add(scopedFile.StartDate.Millisecond);
                msComponents.Add(scopedFile.EndDate.Millisecond);


                //Logger.Debug("{RelativeFullName}: Seq={SeqOK} StartDate={StartDate} EndDate={EndDate}", scopedFile.RelativeFullName, SeqFilesVerified, scopedFile.StartDate, scopedFile.EndDate);
            }

            FoundTimestampMessage = FoundTimestamp ? "Timestamp detected" : "Timestamp not detected";

            HasMsResolution = FoundTimestamp && msComponents.Distinct().Count() != 1;
            HasMsResolutionMessage = HasMsResolution ? "Millisecond resolution detected" :
                FoundTimestamp ? "Millisecond resolution not detected" :
                "Not able to check for millisecond resolution, since the timestamp was not detected";

            SeqFilesVerifiedMessage = SeqFilesVerified
                ? "Logfile entries are sequential without overlapping timestamps"
                : "Logfile entries are not sequential from one file to the other";

            //Logger.Debug("All files have millisecond resolution? {EntryTimestampsVerified} ({MsCollection})", EntryTimestampsVerified, string.Join(",", msComponents));
        }

        public string TimestampParseResult
        {
            get => _timestampParseResult;
            set => this.RaiseAndSetIfChanged(ref _timestampParseResult, value);
        }

        public string FoundTimestampMessage
        {
            get => _foundTimestampMessage;
            set => this.RaiseAndSetIfChanged(ref _foundTimestampMessage, value);
        }

        public string HasMsResolutionMessage
        {
            get => _foundTimestampMessage;
            set => this.RaiseAndSetIfChanged(ref _foundTimestampMessage, value);
        }
        public string SeqFilesVerifiedMessage
        {
            get => _seqFilesVerifiedMessage;
            set => this.RaiseAndSetIfChanged(ref _seqFilesVerifiedMessage, value);
        }

        public bool SeqFilesVerified
        {
            get => _seqFilesVerified;
            set => this.RaiseAndSetIfChanged(ref _seqFilesVerified, value);
        }

        public bool FoundTimestamp
        {
            get => _foundTimestamp;
            set => this.RaiseAndSetIfChanged(ref _foundTimestamp, value);
        }

        public bool HasMsResolution
        {
            get => _hasMsResolution;
            set => this.RaiseAndSetIfChanged(ref _hasMsResolution, value);
        }

        public FileSample FileSampleSelectedItem
        {
            get => _fileSampleSelectedItem;
            set => this.RaiseAndSetIfChanged(ref _fileSampleSelectedItem, value);
        }

        public ReactiveCommand<Unit, Unit> ConfigureScopeCommand { get; set; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }
        public ReactiveCommand<Unit, Unit> ConfigureSeverityCommand { get; set; }

        public ObservableCollection<ScopedFile> LogFiles { get; set; } = new ObservableCollection<ScopedFile>();
        
        //public ObservableCollection<TimestampSample> ParseSamples{ get; set; } = new ObservableCollection<TimestampSample>();
        public ObservableCollection<FileSample> FileSamples { get; set; } = new ObservableCollection<FileSample>();
        public ObservableCollection<LineSample> LineSamples { get; set; } = new ObservableCollection<LineSample>();

        public ConfigureTimestampView View { get; }

        private void LogFilesReady(IChangeSet<ScopedFile, ulong> changes = null)
        {
            if (changes == null) return;
            foreach (var change in changes)
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                        LogFiles.Add(change.Current);
                        break;

                    case ChangeReason.Remove:
                        LogFiles.Remove(change.Current);
                        break;

                    case ChangeReason.Update:
                    case ChangeReason.Refresh:
                        var logFile = LogFiles.FirstOrDefault(l => l == change.Current);
                        logFile = change.Current;
                        break;

                    case ChangeReason.Moved:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
        }

        private void ConfigureScopeExecute()
        {
            // true indicates that the user wants to navigate back
            View.Close((ConfigureAction.Back, TimestampSettings));
        }

        private void CancelExecute()
        {
            View.Close((ConfigureAction.Cancel, new object()));
        }

        private void ConfigureSeverityExecute()
        {
            View.Close((ConfigureAction.Continue, TimestampSettings));
        }

        #region Timestamp

        private string _timestampLine = string.Empty;
        private int _timestampLineSelectionStart = 0;
        private int _timestampLineSelectionEnd = 23;
        private string _timestampFormat = string.Empty;
        private int _timestampLength = 23;
        private int _timestampStart = 1;
        private bool _foundTimestamp;
        private bool _hasMsResolution;
        private bool _seqFilesVerified;
        private string _foundTimestampMessage;
        private string _seqFilesVerifiedMessage;
        private string _timestampParseResult;
        private FileSample _fileSampleSelectedItem;
        private IDisposable _lineSampleSubscription;

        public string TimestampLine
        {
            get => _timestampLine;
            set => this.RaiseAndSetIfChanged(ref _timestampLine, value);
        }

        public int TimestampLineSelectionStart
        {
            get => _timestampLineSelectionStart;
            set => this.RaiseAndSetIfChanged(ref _timestampLineSelectionStart, value);
        }

        public int TimestampLineSelectionEnd
        {
            get => _timestampLineSelectionEnd;
            set => this.RaiseAndSetIfChanged(ref _timestampLineSelectionEnd, value);
        }

        public string TimestampFormat
        {
            get => _timestampFormat;
            set => this.RaiseAndSetIfChanged(ref _timestampFormat, value);
        }

        public int TimestampLength
        {
            get => _timestampLength;
            set => this.RaiseAndSetIfChanged(ref _timestampLength, value);
        }

        public int TimestampStart
        {
            get => _timestampStart;
            set => this.RaiseAndSetIfChanged(ref _timestampStart, value);
        }

        #endregion Timestamp

        #region Implementation of IDisposable

        ~ConfigureTimestampViewModel()
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

            _timestampSubscription?.Dispose();
            _fileMonitorSubscription?.Dispose();
            _parseFilesSubscription?.Dispose();
            _scopedFileMonitor?.Dispose();
            _lineSampleSubscription?.Dispose();
        }

        #endregion Implementation of IDisposable
    }

/*
    public class TimestampSample: ReactiveObject
    {
        public string RelativeFullName { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string Timestamp { get; set; }
        public string Severity { get; set; }
        public string Content { get; set; }
        public string FullName { get; set; }


        public Brush SequenceErrorColor { get; set; }

        public Brush TimestampErrorColor{ get; set; }

        public string SequenceErrorDetails{ get; set; } = string.Empty;

        public string TimestampErrorDetails{ get; set; } = string.Empty;

    }
*/    
    public class FileSample: ReactiveObject
    {
        public ScopedFile ScopedFile { get; set; }
        public string RelativeFullName { get; set; }
        public string FullName { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public bool SeqStartError { get; set; }
        public bool SeqEndError { get; set; }
    }

    public class LineSample: ReactiveObject
    {
        public string Timestamp { get; set; }
        public string Content { get; set; }
    }

    public enum ConfigureAction
    {
        Back,
        Cancel,
        Continue
    }
}
