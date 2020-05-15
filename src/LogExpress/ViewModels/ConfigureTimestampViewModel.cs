using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
        private static readonly ILogger Logger = Log.ForContext<LineItem>();
        private static readonly Brush ErrorColor = new SolidColorBrush(Colors.Red);
        private static readonly Brush WarningColor = new SolidColorBrush(Colors.Orange);
        private IDisposable _fileMonitorSubscription;
        private IDisposable _parseFilesSubscription;
        private ScopedFileMonitor _scopedFileMonitor;
        private IDisposable _severitySubscription;
        private IDisposable _timestampSubscription;
        private readonly TextInfo _textInfo= new CultureInfo("en-US",false).TextInfo;

        public ConfigureTimestampViewModel(ConfigureTimestampView view, string folder, string pattern, bool recursive,
            Layout layout = null)
        {
            View = view;
            ArgFolder = folder;
            ArgPattern = pattern;
            ArgRecursive = recursive;
            ArgLayout = layout;

        }

        public Layout ArgLayout { get; set; }

        public bool ArgRecursive { get; set; }

        public string ArgPattern { get; set; }

        public string ArgFolder { get; set; }

        public void Init()
        {
            if (ArgLayout != null)
            {
                _layout = ArgLayout;
                TimestampLineSelectionStart = ArgLayout.TimestampStart;
                TimestampLineSelectionEnd = ArgLayout.TimestampStart + ArgLayout.TimestampLength;
                TimestampFormat = ArgLayout.TimestampFormat;
                SeverityLineSelectionStart = ArgLayout.SeverityStart;
                SeverityName1 = ArgLayout.Severities[1];
                SeverityName2 = ArgLayout.Severities[2];
                SeverityName3 = ArgLayout.Severities[3];
                SeverityName4 = ArgLayout.Severities[4];
                SeverityName5 = ArgLayout.Severities[5];
                SeverityName6 = ArgLayout.Severities[6];
            }

            UpperCaseCommand = ReactiveCommand.Create(UpperCaseExecute);
            LowerCaseCommand = ReactiveCommand.Create(LowerCaseExecute);
            TitleCaseCommand = ReactiveCommand.Create(TitleCaseExecute);

            UseNLogCommand = ReactiveCommand.Create(UseNLogExecute);
            UseSeriLogLongCommand = ReactiveCommand.Create(UseSeriLogLongExecute);
            UseSeriLogShortNLogCommand = ReactiveCommand.Create(UseSeriLogShortNLogExecute);
            UseLog4JCommand = ReactiveCommand.Create(UseLog4JExecute);
            UsePythonCommand = ReactiveCommand.Create(UsePythonExecute);

            CancelCommand = ReactiveCommand.Create(CancelExecute);
            OpenCommand = ReactiveCommand.Create(OpenExecute);

            _timestampSubscription = this.WhenAnyValue(
                    x => x.TimestampLineSelectionStart,
                    x => x.TimestampLineSelectionEnd,
                    x => x.TimestampFormat)
                .Subscribe(_ => UpdateLayout());

            _severitySubscription = this.WhenAnyValue(
                    x => x.SeverityLineSelectionStart,
                    x => x.SeverityName1,
                    x => x.SeverityName2,
                    x => x.SeverityName3,
                    x => x.SeverityName4,
                    x => x.SeverityName5,
                    x => x.SeverityName6)
                .Subscribe(_ => UpdateLayout());

            _scopedFileMonitor = new ScopedFileMonitor(ArgFolder, new List<string> {ArgPattern}, ArgRecursive);

            _fileMonitorSubscription?.Dispose();
            _fileMonitorSubscription =
                _scopedFileMonitor.Connect()
                    .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(LogFilesReady);

            _parseFilesSubscription = this.WhenAnyValue(x => x.LogFiles, x => x.Layout)
                .Where(obs =>
                {
                    var (f, l) = obs;
                    return f != null && f.Count > 0 && l != null;
                })
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateVerificationTable());
        }

        private void TitleCaseExecute()
        {
            SeverityName1 = _textInfo.ToTitleCase(_severityName1.ToLower());
            SeverityName2 = _textInfo.ToTitleCase(_severityName2.ToLower());
            SeverityName3 = _textInfo.ToTitleCase(_severityName3.ToLower());
            SeverityName4 = _textInfo.ToTitleCase(_severityName4.ToLower());
            SeverityName5 = _textInfo.ToTitleCase(_severityName5.ToLower());
            SeverityName6 = _textInfo.ToTitleCase(_severityName6.ToLower());
        }

        private void LowerCaseExecute()
        {
            SeverityName1 = SeverityName1.ToLower();
            SeverityName2 = SeverityName2.ToLower();
            SeverityName3 = SeverityName3.ToLower();
            SeverityName4 = SeverityName4.ToLower();
            SeverityName5 = SeverityName5.ToLower();
            SeverityName6 = SeverityName6.ToLower();
        }

        private void UpperCaseExecute()
        {
            SeverityName1 = SeverityName1.ToUpper();
            SeverityName2 = SeverityName2.ToUpper();
            SeverityName3 = SeverityName3.ToUpper();
            SeverityName4 = SeverityName4.ToUpper();
            SeverityName5 = SeverityName5.ToUpper();
            SeverityName6 = SeverityName6.ToUpper();
        }

        private void UsePythonExecute()
        {
            SeverityName1 = string.Empty;
            SeverityName2 = "DEBUG";
            SeverityName3 = "INFO";
            SeverityName4 = "WARN";
            SeverityName5 = "ERROR";
            SeverityName6 = "FATAL";
        }

        private void UseLog4JExecute()
        {
            SeverityName1 = "TRACE";
            SeverityName2 = "DEBUG";
            SeverityName3 = "INFO";
            SeverityName4 = "WARN";
            SeverityName5 = "ERROR";
            SeverityName6 = "FATAL";
        }

        private void UseSeriLogShortNLogExecute()
        {
            SeverityName1 = "VRB";
            SeverityName2 = "DBG";
            SeverityName3 = "INF";
            SeverityName4 = "WRN";
            SeverityName5 = "ERR";
            SeverityName6 = "FTL";
        }

        private void UseSeriLogLongExecute()
        {
            SeverityName1 = "Verbose";
            SeverityName2 = "Debug";
            SeverityName3 = "Info";
            SeverityName4 = "Warning";
            SeverityName5 = "Error";
            SeverityName6 = "Fatal";
        }

        private void UseNLogExecute()
        {
            SeverityName1 = "Trace";
            SeverityName2 = "Debug";
            SeverityName3 = "Info";
            SeverityName4 = "Warn";
            SeverityName5 = "Error";
            SeverityName6 = "Fatal";
        }

        public ReactiveCommand<Unit, Unit> TitleCaseCommand { get; set; }

        public ReactiveCommand<Unit, Unit> LowerCaseCommand { get; set; }

        public ReactiveCommand<Unit, Unit> UpperCaseCommand { get; set; }

        public ReactiveCommand<Unit, Unit> UsePythonCommand { get; set; }

        public ReactiveCommand<Unit, Unit> UseLog4JCommand { get; set; }

        public ReactiveCommand<Unit, Unit> UseSeriLogShortNLogCommand { get; set; }

        public ReactiveCommand<Unit, Unit> UseSeriLogLongCommand { get; set; }

        public ReactiveCommand<Unit, Unit> UseNLogCommand { get; set; }

        private void UpdateLayout()
        {
            var tsStart = Math.Min(TimestampLineSelectionStart, TimestampLineSelectionEnd);
            var tsEnd = Math.Max(TimestampLineSelectionStart, TimestampLineSelectionEnd);
            var tsLength = tsEnd - tsStart;

            var sevStart = Math.Min(SeverityLineSelectionStart, SeverityLineSelectionEnd);
            Layout = new Layout
            {
                TimestampStart = tsStart,
                TimestampLength = tsLength,
                TimestampFormat = TimestampFormat,
                SeverityStart = sevStart,
                Severities = new Dictionary<byte, string>
                {
                    [1] = SeverityName1,
                    [2] = SeverityName2,
                    [3] = SeverityName3,
                    [4] = SeverityName4,
                    [5] = SeverityName5,
                    [6] = SeverityName6
                }
            };
        }

        private void UpdateVerificationTable()
        {
            //LineItem.LogFiles = new ReadOnlyObservableCollection<ScopedFile>(LogFiles);

            ParseSamples.Clear();
            var isFirst = true;
            var lastDate = DateTime.MinValue;
            foreach (var scopedFile in LogFiles)
            {
                scopedFile.Layout = Layout;
                string content = null;
                DateTime? timestamp = null;
                byte severity = 0;
                string severityName = null;
                DateTime startDate = default;
                DateTime endDate = default;
                try
                {
                    content = LineItem.ContentFromDisk(scopedFile, 0);
                    timestamp = LineItem.TimestampFromDisk(scopedFile, 0);
                    severity = LineItem.SeverityFromDisk(scopedFile, 0);
                    severityName = severity > 0 ? scopedFile.Layout.Severities[severity] : string.Empty;
                    startDate = scopedFile.StartDate;
                    endDate = scopedFile.EndDate;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error while trying to get data from disk");
                }

                var sample = new TimestampSample()
                {
                    FullName = scopedFile.FullName,
                    RelativeFullName = scopedFile.RelativeFullName,
                    StartDate = $"{startDate}",
                    EndDate = $"{endDate}",
                    Timestamp = $"{timestamp}",
                    Severity = $"{severityName}",
                    Content = content
                };

                if (scopedFile.StartDate < lastDate)
                {
                    sample.SequenceErrorColor = ErrorColor;
                    sample.SequenceErrorDetails = "The StartDate for the file precedes the EndDate of the previous file. \nConsider changing the log-file pattern - as this indicates that the logfiles are not sequential.";
                }

                if (timestamp == null)
                {
                    sample.TimestampErrorColor = ErrorColor;
                    sample.TimestampErrorDetails = "Unable to get a date from the first entry in this file based on the provided timestamp-settings. \nTune the settings and see if that helps.";
                }

                if (severity == 0)
                {
                    sample.SeverityErrorColor = WarningColor;
                    sample.SeverityErrorDetails = "Unable to find a severity level on the first line of the file. \nThis may be an issue with the severity settings. Tune and try again.";
                }

                ParseSamples.Add(sample);

                if (isFirst)
                {
                    TimestampLine = SeverityLine = content;
                    isFirst = false;
                }

                lastDate = scopedFile.EndDate;
            }
        }

        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }

        public ObservableCollection<ScopedFile> LogFiles { get; set; } = new ObservableCollection<ScopedFile>();

        public ReactiveCommand<Unit, Unit> OpenCommand { get; set; }

        public ObservableCollection<TimestampSample> ParseSamples{ get; set; } = new ObservableCollection<TimestampSample>();

        public ConfigureTimestampView View { get; }

        private void CancelExecute()
        {
            View.Close(null);
        }

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

        private void OpenExecute()
        {
            View.Close(Layout);
        }

        private Layout _layout;
        public Layout Layout { 
            get => _layout; 
            set => this.RaiseAndSetIfChanged(ref _layout, value);
        }

        #region Timestamp

        private string _timestampLine = string.Empty;
        private int _timestampLineSelectionStart = 0;
        private int _timestampLineSelectionEnd = 23;
        private string _timestampFormat = string.Empty;
        private int _timestampLength = 23;
        private int _timestampStart = 1;

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

        #region Severity

        private string _severityLine = string.Empty;
        private int _severityLineSelectionStart = 24;
        private int _severityLineSelectionEnd = 27;
        private string _severityName1 = "VRB"; // "VERBOSE";
        private string _severityName2 = "DBG"; // "DEBUG";
        private string _severityName3 = "INF"; // "INFO";
        private string _severityName4 = "WRN"; // "WARN";
        private string _severityName5 = "ERR"; // "ERROR";
        private string _severityName6 = "FTL"; // "FATAL";
        private int _severityStart = 25;

        public string SeverityLine
        {
            get => _severityLine;
            set => this.RaiseAndSetIfChanged(ref _severityLine, value);
        }
        public int SeverityLineSelectionStart
        {
            get => _severityLineSelectionStart;
            set => this.RaiseAndSetIfChanged(ref _severityLineSelectionStart, value);
        }
        public int SeverityLineSelectionEnd
        {
            get => _severityLineSelectionEnd;
            set => this.RaiseAndSetIfChanged(ref _severityLineSelectionEnd, value);
        }

        public string SeverityName1
        {
            get => _severityName1;
            set => this.RaiseAndSetIfChanged(ref _severityName1, value);
        }

        public string SeverityName2
        {
            get => _severityName2;
            set => this.RaiseAndSetIfChanged(ref _severityName2, value);
        }

        public string SeverityName3
        {
            get => _severityName3;
            set => this.RaiseAndSetIfChanged(ref _severityName3, value);
        }

        public string SeverityName4
        {
            get => _severityName4;
            set => this.RaiseAndSetIfChanged(ref _severityName4, value);
        }

        public string SeverityName5
        {
            get => _severityName5;
            set => this.RaiseAndSetIfChanged(ref _severityName5, value);
        }

        public string SeverityName6
        {
            get => _severityName6;
            set => this.RaiseAndSetIfChanged(ref _severityName6, value);
        }

        public int SeverityStart
        {
            get => _severityStart;
            set => this.RaiseAndSetIfChanged(ref _severityStart, value);
        }

        #endregion Severity

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
            _severitySubscription?.Dispose();
            _fileMonitorSubscription?.Dispose();
            _parseFilesSubscription?.Dispose();
            _scopedFileMonitor?.Dispose();
        }

        #endregion Implementation of IDisposable
    }

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

        public Brush SeverityErrorColor{ get; set; }
        
        public string SequenceErrorDetails{ get; set; } = string.Empty;

        public string TimestampErrorDetails{ get; set; } = string.Empty;

        public string SeverityErrorDetails{ get; set; } = string.Empty;
    }
}
