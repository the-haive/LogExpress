using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using LogExpress.Models;
using ReactiveUI;
using Serilog;

namespace LogExpress.Services
{
    /// <summary>
    ///     Using the given fileSpecs, monitor the matching files, either existing, created, deleted or updated.
    ///     Order the monitored file-list by creation date and allows for looking up a given byte position with a given size.
    /// </summary>
    public class VirtualLogFile : ReactiveObject, IDisposable
    {
        private static readonly ILogger Logger = Log.ForContext<VirtualLogFile>();

        private readonly SourceCache<LineItem, long> _lines = new SourceCache<LineItem, long>(l => l.GlobalPosition);
        private bool _analyzeError;
        private readonly ScopedFilesMonitor _fileMonitor;

        private readonly IDisposable _fileMonitorSubscription;
        private bool _isAnalyzed = true;
        private readonly ReadOnlyObservableCollection<FileInfo> _logFiles;
        private long _totalSize;

        /// <summary>
        /// Setup the virtual log-file manager.
        /// </summary>
        /// <param name="basePath"></param>
        /// <param name="filter"></param>
        /// <param name="recursive"></param>
        public VirtualLogFile(string basePath, string filter, bool recursive)
        {
            BasePath = basePath;
            Filter = filter;
            Recursive = recursive;
            LineItem.LogFiles = LogFiles;

            Logger.Debug(
                "Creating instance for basePath={basePath} and filters={filters} with recurse={recurse}",
                basePath, filter, recursive);

            this.WhenAnyValue(x => x.AnalyzeError)
                .Where(x => x)
                .DistinctUntilChanged()
                .Subscribe(_ =>
                {
                    AnalyzeError = false;
                    UpdateCurrentLines();
                });

            this.WhenAnyValue(x => x.IsAnalyzed)
                .Where(x => x)
                .Subscribe(_ => { Logger.Debug("Finished analyzing"); });

            _fileMonitor = new ScopedFilesMonitor(BasePath, new List<string>{Filter}, Recursive);

            _fileMonitorSubscription = _fileMonitor.Connect()
                .Sort(SortExpressionComparer<FileInfo>.Ascending(t => t.CreationTimeUtc))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _logFiles)
                .Subscribe(UpdateCurrentLines);
        }

        public bool AnalyzeError
        {
            get => _analyzeError;
            set => this.RaiseAndSetIfChanged(ref _analyzeError, value);
        }

        public string BasePath { get; set; }

        public string Filter { get; set; }

        public bool IsAnalyzed
        {
            get => _isAnalyzed;
            set => this.RaiseAndSetIfChanged(ref _isAnalyzed, value);
        }

        public ReadOnlyObservableCollection<FileInfo> LogFiles => _logFiles;

        public bool Recursive { get; set; }

        public long TotalSize
        {
            get => _totalSize;
            set => this.RaiseAndSetIfChanged(ref _totalSize, value);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            _fileMonitorSubscription?.Dispose();
            _fileMonitor?.Dispose();
        }

        private async void AnalyzeLinesInLogFile(object arg)
        {
            var (logFiles, changes) =
                ((ReadOnlyObservableCollection<FileInfo>, IChangeSet<FileInfo, string>)) arg;

            // TODO: Optimize by updating only the changed files.
            var iterator = logFiles.GetEnumerator();
            try
            {
                var lines = new List<LineItem>();
                long position = 0;
                var logFileIndex = 0;
                while (iterator.MoveNext())
                {
                    var fileInfo = iterator.Current;
                    lines.AddRange(await ReadFileLinePositions(fileInfo, logFileIndex, position));
                    Debug.Assert(fileInfo != null, nameof(fileInfo) + " != null");
                    position += fileInfo.Length;
                    logFileIndex++;
                }

                iterator.Dispose();

                _lines.AddOrUpdate(lines);
                IsAnalyzed = true;
            }
            catch (Exception ex)
            {
                Logger.Error("Error during analyze", ex);
                AnalyzeError = true;
            }
            finally
            {
                iterator.Dispose();
            }
        }

        /// <summary>
        /// We expose the Connect() since we are interested in a stream of changes.
        /// </summary>
        /// <returns></returns>
        public IObservable<IChangeSet<LineItem, long>> Connect() => _lines.Connect();

        private static async Task<List<LineItem>> ReadFileLinePositions(FileInfo file, int fileIndex, long position)
        {
            if (file.Length <= 0) return new List<LineItem>();
            await using var fileStream =
                new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var reader = new StreamReader(fileStream);

            return FindNewLinePositions();

            List<LineItem> FindNewLinePositions()
            {
                var linePositions = new List<LineItem>();
                var buffer = new Span<char>(new char[1]);

                var stopwatch = Stopwatch.StartNew();
                // All files with length > 0 implicitly starts with a line
                var lineNum = 1;
                var filePosition = 0;
                linePositions.Add(new LineItem {LogFileIndex = fileIndex, Position = filePosition, LineNum = lineNum});
                while (!reader.EndOfStream)
                {
                    var numRead = reader.Read(buffer);
                    if (numRead == -1) continue; // End of stream
                    filePosition++;
                    if (buffer[0] == '\n')
                    {
                        linePositions.Add(new LineItem {LogFileIndex = fileIndex, Position = filePosition, LineNum = ++lineNum});
                    }
                }

                stopwatch.Stop();
                var duration = stopwatch.Elapsed;
                Logger.Debug("Time spent on analyzing lines in {LogFile}: {Duration}", file.FullName, duration);

                return linePositions;
            }
        }

        private void UpdateCurrentLines(IChangeSet<FileInfo, string> changes = null)
        {
            IsAnalyzed = false;
            LineItem.LogFiles = LogFiles;
            TotalSize = LogFiles.Sum(l => l.Length);

            // TODO: Use a normal async task for this instead, or is this ok?
            new Thread(AnalyzeLinesInLogFile)
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            }.Start((LogFiles, changes));
        }
    }
}