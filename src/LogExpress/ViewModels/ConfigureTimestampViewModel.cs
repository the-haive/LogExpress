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
                .Where(obs =>
                {
                    var (start, end, format) = obs;
                    return start > -1 && end > start;
                } )
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
                .Subscribe(_ => UpdateVerificationTable());
        }

        private void UpdateTimestampSettings()
        {
            var tsStart = Math.Min(TimestampLineSelectionStart, TimestampLineSelectionEnd);
            var tsEnd = Math.Max(TimestampLineSelectionStart, TimestampLineSelectionEnd);
            var tsLength = tsEnd - tsStart;

            TimestampSettings.TimestampStart = tsStart;
            TimestampSettings.TimestampLength = tsLength;
            TimestampSettings.TimestampFormat = TimestampFormat;
        }

        private void UpdateVerificationTable()
        {
            //LineItem.LogFiles = new ReadOnlyObservableCollection<ScopedFile>(LogFiles);

            ParseSamples.Clear();
            var isFirst = true;
            var lastDate = DateTime.MinValue;

            foreach (var scopedFile in LogFiles)
            {
                scopedFile.TimestampSettings = TimestampSettings;
                string content = null;
                DateTime? timestamp = null;
                DateTime startDate = default;
                DateTime endDate = default;
                try
                {
                    content = LineItem.ContentFromDisk(scopedFile, 0);
                    timestamp = LineItem.TimestampFromDisk(scopedFile, 0);
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

                ParseSamples.Add(sample);

                if (isFirst)
                {
                    TimestampLine = content;
                    isFirst = false;
                }

                lastDate = endDate;
            }
        }

        public ReactiveCommand<Unit, Unit> ConfigureScopeCommand { get; set; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }
        public ReactiveCommand<Unit, Unit> ConfigureSeverityCommand { get; set; }

        public ObservableCollection<ScopedFile> LogFiles { get; set; } = new ObservableCollection<ScopedFile>();
        
        public ObservableCollection<TimestampSample> ParseSamples{ get; set; } = new ObservableCollection<TimestampSample>();

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

        public string SequenceErrorDetails{ get; set; } = string.Empty;

        public string TimestampErrorDetails{ get; set; } = string.Empty;

    }

    public enum ConfigureAction
    {
        Back,
        Cancel,
        Continue
    }
}
