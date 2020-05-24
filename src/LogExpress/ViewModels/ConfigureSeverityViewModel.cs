using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
    public class ConfigureSeverityViewModel : ViewModelBase, IDisposable
    {
        private static readonly ILogger Logger = Log.ForContext<ConfigureSeverityViewModel>();
        private IDisposable _fileMonitorSubscription;
        private IDisposable _parseFilesSubscription;
        private ScopedFileMonitor _scopedFileMonitor;
        private IDisposable _severitySubscription;
        private IDisposable _severitySubscription2;
        private readonly TextInfo _textInfo= new CultureInfo("en-US",false).TextInfo;
        private SeverityFileSample _severityFileSampleSelectedItem;
        private IDisposable _lineSampleSubscription;


        private static readonly Dictionary<string, List<string>> Templates = new Dictionary<string, List<string>>
        {
            {"NLog", new List<string> {"Trace", "Debug", "Info", "Warn", "Error", "Fatal"}},
            {"SeriLog-long", new List<string> {"Verbose", "Debug", "Info", "Warning", "Error", "Fatal"}},
            {"SeriLog-short", new List<string> {"VRB", "DBG", "INF", "WRN", "ERR", "FTL"}},
            {"Log4j", new List<string> {"TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL"}},
            {"Python", new List<string> {string.Empty, "DEBUG", "INFO", "WARN", "ERROR", "FATAL",}}
        };

        public ConfigureSeverityViewModel(ConfigureSeverityView view, ScopeSettings scopeSettings, TimestampSettings timestampSettings, SeveritySettings severitySettings)
        {
            View = view;
            ScopeSettings = scopeSettings;
            TimestampSettings = timestampSettings;
            SeveritySettings = severitySettings ?? new SeveritySettings();
        }

        public ScopeSettings ScopeSettings { get; set; }

        public TimestampSettings TimestampSettings { get; set; }

        public SeveritySettings SeveritySettings
        {
            get => _severitySettings;
            set => this.RaiseAndSetIfChanged(ref _severitySettings, value);
        }

        public async void Init()
        {
            if (SeveritySettings != null)
            {
                SeverityName1 = SeveritySettings.Severities.ContainsKey(1)
                    ? SeveritySettings.Severities[1]
                    : Templates["NLog"][0];
                SeverityName2 = SeveritySettings.Severities.ContainsKey(2)
                    ? SeveritySettings.Severities[2]
                    : Templates["NLog"][1];
                SeverityName3 = SeveritySettings.Severities.ContainsKey(3)
                    ? SeveritySettings.Severities[3]
                    : Templates["NLog"][2];
                SeverityName4 = SeveritySettings.Severities.ContainsKey(4)
                    ? SeveritySettings.Severities[4]
                    : Templates["NLog"][3];
                SeverityName5 = SeveritySettings.Severities.ContainsKey(5)
                    ? SeveritySettings.Severities[5]
                    : Templates["NLog"][4];
                SeverityName6 = SeveritySettings.Severities.ContainsKey(6)
                    ? SeveritySettings.Severities[6]
                    : Templates["NLog"][5];
                SeverityLineSelectionStart = SeveritySettings.SeverityStart;
                SeverityLineSelectionEnd = SeverityLineSelectionStart + SeveritySettings.MaxSeverityNameLength;
            }

            UpperCaseCommand = ReactiveCommand.Create(UpperCaseExecute);
            LowerCaseCommand = ReactiveCommand.Create(LowerCaseExecute);
            TitleCaseCommand = ReactiveCommand.Create(TitleCaseExecute);

            // TODO: Get templates from config-file - apply via i.e. dropdown, instead of hardcoded values
            UseNLogCommand = ReactiveCommand.Create(UseNLogExecute);
            UseSeriLogLongCommand = ReactiveCommand.Create(UseSeriLogLongExecute);
            UseSeriLogShortNLogCommand = ReactiveCommand.Create(UseSeriLogShortNLogExecute);
            UseLog4JCommand = ReactiveCommand.Create(UseLog4JExecute);
            UsePythonCommand = ReactiveCommand.Create(UsePythonExecute);

            ConfigureTimestampCommand = ReactiveCommand.Create(ConfigureTimestampExecute);
            CancelCommand = ReactiveCommand.Create(CancelExecute);
            OpenCommand = ReactiveCommand.Create(OpenExecute);

            _severitySubscription = this.WhenAnyValue(
                    x => x.SeverityLineSelectionStart,
                    x => x.SeverityLineSelectionEnd)
                .Throttle(TimeSpan.FromMilliseconds(100))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    UpdateSeveritySettings();
                });

            _severitySubscription2 = this.WhenAnyValue(
                    x => x.SeverityName1,
                    x => x.SeverityName2,
                    x => x.SeverityName3,
                    x => x.SeverityName4,
                    x => x.SeverityName5,
                    x => x.SeverityName6)
                .Throttle(TimeSpan.FromMilliseconds(100))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    UpdateSeveritySettings();
                });

            _scopedFileMonitor = new ScopedFileMonitor(ScopeSettings.Folder, new List<string> {ScopeSettings.Pattern}, ScopeSettings.Recursive);

            _fileMonitorSubscription?.Dispose();
            _fileMonitorSubscription =
                _scopedFileMonitor.Connect()
                    .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(LogFilesReady);

            _parseFilesSubscription = this.WhenAnyValue(x => x.LogFiles, x=> x.SeveritySettings)
                .Where(obs =>
                {
                    var (files, severitySettings) = obs;
                    return files != null && files.Count > 0 && severitySettings != null;
                })
                .Throttle(TimeSpan.FromMilliseconds(100))
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateFileSamples());

            _lineSampleSubscription = this.WhenAnyValue(x => x.SeverityFileSampleSelectedItem)
                .Subscribe(UpdateLineSamples);
           
        }


        private void UpdateLineSamples(SeverityFileSample selectedSeverityFile)
        {
            LineSamples.Clear();

            if (selectedSeverityFile == null || !LogFiles.Any()) return;

            LineItem.LogFiles = new ReadOnlyObservableCollection<ScopedFile>(LogFiles);

            GetFileSeverities(selectedSeverityFile.ScopedFile, 100, out var lineSamples, true);
            LineSamples.AddRange(lineSamples);
            
            Logger.Debug("Have {Count} lines from file {Filename}", LineSamples.Count, selectedSeverityFile.RelativeFullName);
        }

        public string NLogToolTip => string.Join("\n", Templates["NLog"]);
        public string SeriLogLongToolTip => string.Join("\n", Templates["SeriLog-long"]);
        public string SeriLogShortNLogToolTip => string.Join("\n", Templates["SeriLog-short"]);
        public string Log4JToolTip => string.Join("\n", Templates["Log4j"]);
        public string PythonToolTip => string.Join("\n", Templates["Python"]);

        private void UseNLogExecute()
        {
            SeverityName1 = Templates["NLog"][0];
            SeverityName2 = Templates["NLog"][1];
            SeverityName3 = Templates["NLog"][2];
            SeverityName4 = Templates["NLog"][3];
            SeverityName5 = Templates["NLog"][4];
            SeverityName6 = Templates["NLog"][5];
        }

        private void UseSeriLogLongExecute()
        {
            SeverityName1 = Templates["SeriLog-long"][0];
            SeverityName2 = Templates["SeriLog-long"][1];
            SeverityName3 = Templates["SeriLog-long"][2];
            SeverityName4 = Templates["SeriLog-long"][3];
            SeverityName5 = Templates["SeriLog-long"][4];
            SeverityName6 = Templates["SeriLog-long"][5];
        }

        private void UseSeriLogShortNLogExecute()
        {
            SeverityName1 = Templates["SeriLog-short"][0];
            SeverityName2 = Templates["SeriLog-short"][1];
            SeverityName3 = Templates["SeriLog-short"][2];
            SeverityName4 = Templates["SeriLog-short"][3];
            SeverityName5 = Templates["SeriLog-short"][4];
            SeverityName6 = Templates["SeriLog-short"][5];
        }

        private void UseLog4JExecute()
        {
            SeverityName1 = Templates["Log4j"][0];
            SeverityName2 = Templates["Log4j"][1];
            SeverityName3 = Templates["Log4j"][2];
            SeverityName4 = Templates["Log4j"][3];
            SeverityName5 = Templates["Log4j"][4];
            SeverityName6 = Templates["Log4j"][5];
        }

        private void UsePythonExecute()
        {
            SeverityName1 = Templates["Python"][0];
            SeverityName2 = Templates["Python"][1];
            SeverityName3 = Templates["Python"][2];
            SeverityName4 = Templates["Python"][3];
            SeverityName5 = Templates["Python"][4];
            SeverityName6 = Templates["Python"][5];
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

        public SeverityFileSample SeverityFileSampleSelectedItem
        {
            get => _severityFileSampleSelectedItem;
            set => this.RaiseAndSetIfChanged(ref _severityFileSampleSelectedItem, value);
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
            var severitySettings = new SeveritySettings
            {
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
            this.RaisePropertyChanged(nameof(SeverityLineSelectionStart));
            this.RaisePropertyChanged(nameof(SeverityLineSelectionEnd));

            SeveritySettings = severitySettings;
        }

        private void UpdateFileSamples()
        {
            Logger.Debug(
                "Updating the verification tables, based on severity settings: Start={Start} Severities={Severities}",
                SeveritySettings.SeverityStart, string.Join(",", SeveritySettings.Severities));

            FileSamples.Clear();
            var isFirst = true;
            SeverityFileSampleSelectedItem = null;

            var entriesVerified = true;
            foreach (var scopedFile in LogFiles.OrderBy(f => f.StartDate))
            {
                if (isFirst && string.IsNullOrWhiteSpace(SeverityLine))
                {
                    SeverityLine = LineItem.ContentFromDisk(scopedFile, 0);
                    isFirst = false;
                }

                GetFileSeverities(scopedFile, 10, out var lineSamples);
                var severitiesMissing = lineSamples.All(l => !l.SeverityFound);
                var severities = lineSamples.OrderBy(l => l.SeverityLevel).GroupBy(l => l.SeverityLevel).Select(l => l.First().Severity);
                var fileSample = new SeverityFileSample
                {
                    ScopedFile = scopedFile,
                    FullName = scopedFile.FullName,
                    RelativeFullName = scopedFile.RelativeFullName,
                    Severities = string.Join(",", severities),
                    SeveritiesMissing = severitiesMissing
                };
                entriesVerified = entriesVerified && !severitiesMissing;

                FileSamples.Add(fileSample);

                if (SeverityFileSampleSelectedItem == null) SeverityFileSampleSelectedItem = fileSample;
            }

            EntrySeveritiesVerified = entriesVerified;
            var msg = entriesVerified ? "Matching severities found": "Severities found in the files are not matching";
            EntrySeveritiesVerifiedMessage = $"{msg} (based on 10 first lines of each file)";
        }

        public string EntrySeveritiesVerifiedMessage
        {
            get => _entrySeveritiesVerifiedMessage;
            set => this.RaiseAndSetIfChanged(ref _entrySeveritiesVerifiedMessage, value);
        }

        public bool EntrySeveritiesVerified
        {
            get => _entrySeveritiesVerified;
            set => this.RaiseAndSetIfChanged(ref _entrySeveritiesVerified, value);
        }

        private void GetFileSeverities(ScopedFile scopedFile, uint maxLinesToRead, out ObservableCollection<SeverityLineSample> lineSamples, bool includeContent = false)
        {
            using var fileStream = new FileStream(scopedFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream, scopedFile.Encoding);

            var lines = new ObservableCollection<LineItem>();

            ScopedFile.ReadFileLinePositions(lines, reader, scopedFile, maxLinesToRead: maxLinesToRead);

            lineSamples = new ObservableCollection<SeverityLineSample>();

            foreach (var lineItem in lines)
            {
                var content = includeContent ? lineItem.Content : null;
                var (level, severity) = ScopedFile.ReadFileLineSeverity(reader, SeveritySettings, lineItem.Position);

                var lineSample = new SeverityLineSample()
                {
                    SeverityLevel = level,
                    SeverityFound = level > 0,
                    Severity = severity,
                    Content = content
                };

                lineSamples.Add(lineSample);
            }
        }
        
        public ReactiveCommand<Unit, Unit> ConfigureTimestampCommand { get; set; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }
        public ReactiveCommand<Unit, Unit> OpenCommand { get; set; }

        public ObservableCollection<ScopedFile> LogFiles { get; set; } = new ObservableCollection<ScopedFile>();
        public ObservableCollection<SeverityFileSample> FileSamples { get; set; } = new ObservableCollection<SeverityFileSample>();
        public ObservableCollection<SeverityLineSample> LineSamples { get; set; } = new ObservableCollection<SeverityLineSample>();


        public ConfigureSeverityView View { get; }

        private void LogFilesReady(IChangeSet<ScopedFile, ulong> changes = null)
        {
            if (changes == null) return;

            foreach (var change in changes)
            {
                change.Current.TimestampSettings = TimestampSettings;
                change.Current.SeveritySettings = SeveritySettings;
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
        }

        private void ConfigureTimestampExecute()
        {
            var tuple = new Tuple<ConfigureAction, SeveritySettings>(ConfigureAction.Back, SeveritySettings);
            View.Close(tuple);
        }

        private void CancelExecute()
        {
            var tuple = new Tuple<ConfigureAction, SeveritySettings>(ConfigureAction.Cancel, null);
            View.Close(tuple);
        }

        private void OpenExecute()
        {
            var tuple = new Tuple<ConfigureAction, SeveritySettings>(ConfigureAction.Continue, SeveritySettings);
            View.Close(tuple);
        }

        #region Severity

        private string _severityLine = string.Empty;
        private int _severityLineSelectionStart;
        private int _severityLineSelectionEnd;
        private string _severityName1 = string.Empty; // "VERBOSE";
        private string _severityName2 = string.Empty; // "DEBUG";
        private string _severityName3 = string.Empty; // "INFO";
        private string _severityName4 = string.Empty; // "WARN";
        private string _severityName5 = string.Empty; // "ERROR";
        private string _severityName6 = string.Empty; // "FATAL";

        private bool _entrySeveritiesVerified;

        private string _entrySeveritiesVerifiedMessage;

        private SeveritySettings _severitySettings;
        //private int _severityStart = 25;


        public string SeverityLine
        {
            get => _severityLine;
            set => this.RaiseAndSetIfChanged(ref _severityLine, value);
        }
        public int SeverityLineSelectionStart
        {
            get
            {
                Logger.Debug("_severityLineSelectionStart is {SeverityLineSelectionStart}", _severityLineSelectionStart);
                return _severityLineSelectionStart;
            }
            set
            {
                Logger.Debug("Changing _severityLineSelectionStart from {OldSeverityLineSelectionStart} to {NewSeverityLineSelectionStart}", _severityLineSelectionStart, value);
                this.RaiseAndSetIfChanged(ref _severityLineSelectionStart, value);
            }
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
            _severitySubscription2?.Dispose();
            _fileMonitorSubscription?.Dispose();
            _parseFilesSubscription?.Dispose();
            _scopedFileMonitor?.Dispose();
            _lineSampleSubscription?.Dispose();
        }

        #endregion Implementation of IDisposable
    }

    public class SeverityFileSample: ReactiveObject
    {
        public ScopedFile ScopedFile { get; set; }
        public string RelativeFullName { get; set; }
        public string FullName { get; set; }
        public string Severities { get; set; }
        public bool SeveritiesMissing { get; set; }
    }

    public class SeverityLineSample: ReactiveObject
    {
        public string Severity { get; set; }
        public string Content { get; set; }
        public bool SeverityFound { get; set; }
        public byte SeverityLevel { get; set; }
    }
}
