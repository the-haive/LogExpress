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
    public class ConfigureSeverityViewModel : ViewModelBase, IDisposable
    {
        private static readonly ILogger Logger = Log.ForContext<ConfigureSeverityViewModel>();
        private static readonly Brush ErrorColor = new SolidColorBrush(Colors.Red);
        private static readonly Brush WarningColor = new SolidColorBrush(Colors.Orange);
        private IDisposable _fileMonitorSubscription;
        private IDisposable _parseFilesSubscription;
        private IDisposable _parseSeveritySubscription;
        private ScopedFileMonitor _scopedFileMonitor;
        private IDisposable _severitySubscription;
        private readonly TextInfo _textInfo= new CultureInfo("en-US",false).TextInfo;

        public ConfigureSeverityViewModel(ConfigureSeverityView view, ScopeSettings scopeSettings, SeveritySettings severitySettings)
        {
            View = view;
            ScopeSettings = scopeSettings;
            SeveritySettings = severitySettings ?? new SeveritySettings();
        }

        public ScopeSettings ScopeSettings { get; set; }

        public SeveritySettings SeveritySettings { get; set; }

        public void Init()
        {
            if (SeveritySettings != null)
            {
                SeverityLineSelectionStart = SeveritySettings.SeverityStart;
                SeverityName1 = SeveritySettings.Severities.ContainsKey(1) ? SeveritySettings.Severities[1] : string.Empty;
                SeverityName2 = SeveritySettings.Severities.ContainsKey(2) ? SeveritySettings.Severities[2] : string.Empty;
                SeverityName3 = SeveritySettings.Severities.ContainsKey(3) ? SeveritySettings.Severities[3] : string.Empty;
                SeverityName4 = SeveritySettings.Severities.ContainsKey(4) ? SeveritySettings.Severities[4] : string.Empty;
                SeverityName5 = SeveritySettings.Severities.ContainsKey(5) ? SeveritySettings.Severities[5] : string.Empty;
                SeverityName6 = SeveritySettings.Severities.ContainsKey(6) ? SeveritySettings.Severities[6] : string.Empty;
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

            _severitySubscription = this.WhenAnyValue(
                    x => x.SeverityLineSelectionStart,
                    x => x.SeverityName1,
                    x => x.SeverityName2,
                    x => x.SeverityName3,
                    x => x.SeverityName4,
                    x => x.SeverityName5,
                    x => x.SeverityName6)
                .Subscribe(_ => UpdateSeveritySettings());

            _scopedFileMonitor = new ScopedFileMonitor(ScopeSettings.Folder, new List<string> {ScopeSettings.Pattern}, ScopeSettings.Recursive);

            _fileMonitorSubscription?.Dispose();
            _fileMonitorSubscription =
                _scopedFileMonitor.Connect()
                    .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(LogFilesReady);
            _parseFilesSubscription = this.WhenAnyValue(x => x.LogFiles)
                .Where(files => files != null && files.Count > 0)
                .Throttle(TimeSpan.FromMilliseconds(100))
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Do(changes => Logger.Debug("Updating verification table due file-count: {Count}", changes.Count))
                .Subscribe(_ => UpdateVerificationTable());

            _parseSeveritySubscription = this.WhenAnyValue(
                    x => x.SeveritySettings.SeverityStart,
                    x => x.SeveritySettings.Severities
                )
                .Throttle(TimeSpan.FromMilliseconds(100))
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Do(changes => Logger.Debug("Updating verification table due severitySettings changes. Start={Start}, Count={Count}", changes.Item1, changes.Item2))
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

        private void UpdateSeveritySettings()
        {
            var sevStart = Math.Min(SeverityLineSelectionStart, SeverityLineSelectionEnd);
            SeveritySettings.SeverityStart = sevStart;

            SeveritySettings.Severities = new Dictionary<byte, string>
            {
                [1] = SeverityName1,
                [2] = SeverityName2,
                [3] = SeverityName3,
                [4] = SeverityName4,
                [5] = SeverityName5,
                [6] = SeverityName6
            };
        }

        private void UpdateVerificationTable()
        {
            //LineItem.LogFiles = new ReadOnlyObservableCollection<ScopedFile>(LogFiles);

            Logger.Debug("Updating verification table: File-count={FileCount} Severity-count={SeverityCount}",
                LogFiles.Count,
                SeveritySettings.Severities.Count);
            ParseSamples.Clear();
            var isFirst = true;

            foreach (var scopedFile in LogFiles)
            {
                scopedFile.SeveritySettings = SeveritySettings;
                string content = null;
                byte severity = 0;
                string severityName = string.Empty;
                try
                {
                    content = LineItem.ContentFromDisk(scopedFile, 0);
                    if (scopedFile.SeveritySettings != null && scopedFile.SeveritySettings.Severities.Count > 0)
                    {
                        severity = LineItem.SeverityFromDisk(scopedFile, 0);
                        severityName = severity > 0 ? scopedFile.SeveritySettings?.Severities[severity] : string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error while trying to get data from disk");
                }

                var sample = new SeveritySample()
                {
                    FullName = scopedFile.FullName,
                    RelativeFullName = scopedFile.RelativeFullName,
                    Severity = $"{severityName}",
                    Content = content
                };

                if (severity == 0)
                {
                    sample.SeverityErrorColor = WarningColor;
                    sample.SeverityErrorDetails = "Unable to find a severity level on the first line of the file. \nThis may be an issue with the severity settings. Tune and try again.";
                }

                ParseSamples.Add(sample);

                if (isFirst)
                {
                    SeverityLine = content;
                    isFirst = false;
                }
            }
        }

        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }

        public ObservableCollection<ScopedFile> LogFiles { get; set; } = new ObservableCollection<ScopedFile>();

        public ReactiveCommand<Unit, Unit> OpenCommand { get; set; }

        public ObservableCollection<SeveritySample> ParseSamples{ get; set; } = new ObservableCollection<SeveritySample>();

        public ConfigureSeverityView View { get; }

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

        private void ConfigureTimestampExecute()
        {
            View.Close((ConfigureAction.Back, SeveritySettings));
        }

        private void CancelExecute()
        {
            View.Close((ConfigureAction.Cancel, new object()));
        }

        private void OpenExecute()
        {
            View.Close((ConfigureAction.Continue, SeveritySettings));
        }

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

        ~ConfigureSeverityViewModel()
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

            _severitySubscription?.Dispose();
            _fileMonitorSubscription?.Dispose();
            _parseFilesSubscription?.Dispose();
            _parseSeveritySubscription?.Dispose();
            _scopedFileMonitor?.Dispose();
        }

        #endregion Implementation of IDisposable
    }

    public class SeveritySample: ReactiveObject
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
