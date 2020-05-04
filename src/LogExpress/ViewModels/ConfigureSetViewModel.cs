using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using DynamicData;
using DynamicData.Binding;
using LogExpress.Models;
using LogExpress.Services;
using LogExpress.Views;
using ReactiveUI;

namespace LogExpress.ViewModels
{
    public class ConfigureSetViewModel : ViewModelBase, IDisposable
    {
        private readonly IDisposable _fileMonitorSubscription;
        private readonly IDisposable _parseFilesSubscription;
        private readonly ScopedFileMonitor _scopedFileMonitor;
        private readonly IDisposable _severitySubscription;
        private readonly IDisposable _timestampSubscription;

        public ConfigureSetViewModel(ConfigureSetView view, string folder, string pattern, bool recursive)
        {
            View = view;

            CancelCommand = ReactiveCommand.Create(CancelExecute);
            OpenCommand = ReactiveCommand.Create(OpenExecute);

            _timestampSubscription = this.WhenAnyValue(
                    x => x.TimestampStart,
                    x => x.TimestampLength,
                    x => x.TimestampFormat)
                .Subscribe(_ => UpdateLayout());

            _severitySubscription = this.WhenAnyValue(
                    x => x.SeverityStart,
                    x => x.SeverityName1,
                    x => x.SeverityName2,
                    x => x.SeverityName3,
                    x => x.SeverityName4,
                    x => x.SeverityName5,
                    x => x.SeverityName6)
                .Subscribe(_ => UpdateLayout());

            _scopedFileMonitor = new ScopedFileMonitor(folder, new List<string> {pattern}, recursive);

            _fileMonitorSubscription?.Dispose();
            _fileMonitorSubscription = _scopedFileMonitor.Connect()
                .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                .Subscribe(LogFilesReady);

            _parseFilesSubscription = this.WhenAnyValue(x => x.LogFiles, x => x.Layout)
                .Subscribe(_ => UpdateParseSamples());
        }

        private void UpdateLayout()
        {
            Layout = new Layout
            {
                TimestampStart = TimestampStart,
                TimestampLength = TimestampLength,
                TimestampFormat = TimestampFormat,
                SeverityStart = SeverityStart,
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

        private void UpdateParseSamples()
        {
            var parseSamples = new ObservableCollection<Sample>();
            foreach (var scopedFile in LogFiles)
            {
                scopedFile.Layout = Layout;

                parseSamples.Add(new Sample()
                {
                    RelativeFullName = scopedFile.RelativeFullName,
                    CreationTime = $"{scopedFile.CreationTime}",
                    StartDate = $"{scopedFile.StartDate}",
                    EndDate = $"{scopedFile.EndDate}",
                    Severities = ScopedFile.SampleSeverities
                });
            }

            ParseSamples = parseSamples;
        }

        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }

        public ObservableCollection<ScopedFile> LogFiles { get; set; } = new ObservableCollection<ScopedFile>();

        public ReactiveCommand<Unit, Unit> OpenCommand { get; set; }

        public ObservableCollection<Sample> ParseSamples { get; set; } = new ObservableCollection<Sample>();

        public ConfigureSetView View { get; }

        private void CancelExecute()
        {
            View.Close();
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
            View.Close(new ConfigureSetResult());
        }

        public class ConfigureSetResult
        {
            public Dictionary<byte, string> Severities;
            public int SeverityStart;
            public string TimestampFormat;
            public int TimestampLength;
            public int TimestampStart;
        }

        private Layout _layout;
        public Layout Layout { 
            get => _layout; 
            set => this.RaiseAndSetIfChanged(ref _layout, value);
        }

        #region Timestamp

        private string _timestampFormat = string.Empty;
        private int _timestampLength = 23;
        private int _timestampStart = 1;

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

        private string _severityName1 = "TRC"; // "TRACE";
        private string _severityName2 = "DBG"; // "DEBUG";
        private string _severityName3 = "INF"; // "INFO";
        private string _severityName4 = "WRN"; // "WARN";
        private string _severityName5 = "ERR"; // "ERROR";
        private string _severityName6 = "FTL"; // "FATAL";
        private int _severityStart = 25;

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

        ~ConfigureSetViewModel()
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

    public class Sample
    {
        public string RelativeFullName { get; set; }
        public string CreationTime { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string Severities { get; set; }
    }
}
