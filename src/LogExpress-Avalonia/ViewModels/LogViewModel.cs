using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
using ByteSizeLib;
using DynamicData;
using LogExpress.Models;
using LogExpress.Services;
using ReactiveUI;
using Serilog;

namespace LogExpress.ViewModels
{
    public class LogViewModel : ViewModelBase
    {
        public static readonly ILogger Logger = Log.ForContext<LogViewModel>();

        private string _basePath;
        private string _filter;
        private readonly ObservableAsPropertyHelper<bool> _isAnalyzed;
        private readonly ReadOnlyObservableCollection<LineItem> _lines;
        private bool _recursive;
        private bool _tail;
        private readonly ObservableAsPropertyHelper<long> _totalSize;
        private readonly ObservableAsPropertyHelper<string> _humanTotalSize;
        private LineItem _lineSelected;

        public readonly VirtualLogFile VirtualLogFile;
        private ObservableAsPropertyHelper<string> _logListHeader;

        public LogViewModel(string basePath, string filter, in bool recursive)
        {
            BasePath = basePath;
            Filter = filter;
            Recursive = recursive;
            Tail = true;

            VirtualLogFile = new VirtualLogFile(BasePath, Filter, Recursive);

            LogFiles = VirtualLogFile.LogFiles;

            VirtualLogFile.Connect()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _lines)
                .Subscribe();

            _totalSize = VirtualLogFile.WhenAnyValue(x => x.TotalSize)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.TotalSize);

            _humanTotalSize = VirtualLogFile.WhenAnyValue(x => x.TotalSize)
                .Select(x => ByteSize.FromBytes(x).ToString())
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.HumanTotalSize);

            _isAnalyzed = VirtualLogFile.WhenAnyValue(x => x.IsAnalyzed)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.IsAnalyzed);

            _logListHeader = this.WhenAnyValue(x => x.LogFiles.Count)
                .DistinctUntilChanged()
                .Select(x => $"{x} scoped log-files (click to toggle)")
                .ToProperty(this, x => x.LogListHeader);

        }

        ~LogViewModel()
        {
            _isAnalyzed?.Dispose();
            _totalSize?.Dispose();
            VirtualLogFile?.Dispose();
        }

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

        public bool IsAnalyzed => _isAnalyzed != null && _isAnalyzed.Value;
        public ReadOnlyObservableCollection<LineItem> Lines => _lines;
        public ReadOnlyObservableCollection<FileInfo> LogFiles { get; private set; }

        public bool Recursive
        {
            get => _recursive;
            set => this.RaiseAndSetIfChanged(ref _recursive, value);
        }

        public bool Tail
        {
            get => _tail;
            set => this.RaiseAndSetIfChanged(ref _tail, value);
        }

        public long TotalSize => _totalSize.Value;
        public string HumanTotalSize => _humanTotalSize.Value;
        public string LogListHeader => _logListHeader.Value;

        public LineItem LineSelected
        {
            get => _lineSelected;
            set => this.RaiseAndSetIfChanged(ref _lineSelected, value);
        }
    }
}