using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicData;
using Serilog;

namespace LogExpress.Services
{
    /// <summary>
    ///     Maintain a list of actual files on disk that matched the passed constructor parameters.
    /// </summary>
    public class ScopedFilesMonitor : IDisposable
    {
        private static readonly ILogger Logger = Log.ForContext<ScopedFilesMonitor>();

        private readonly string _basePath;
        private readonly List<string> _filters;
        private readonly bool _includeSubdirectories;

        private readonly Dictionary<string, FileSystemWatcher> _watchers = new Dictionary<string, FileSystemWatcher>();
        private FileSystemWatcher _parentWatcher;
        
        private readonly SourceCache<FileInfo, string> _files = new SourceCache<FileInfo, string>(fi => fi.FullName);

        /// <summary>
        ///     Used to connect to a stream of changes in the list of scoped files.
        ///     (Using ReactiveUI DynamicData pattern)
        /// </summary>
        /// <returns>An observable ChangeSet of FileInfo</returns>
        public IObservable<IChangeSet<FileInfo, string>> Connect() => _files.Connect();

        /// <summary>
        ///     Creates an instance that monitors the filesystem for files that matches the basePath and filter.
        /// </summary>
        /// <param name="basePath">The base folder where the files are expected to be</param>
        /// <param name="filters">The wildcard patterns that defines which files to monitor</param>
        /// <param name="includeSubdirectories">Whether or not to look for the same patterns also in sub-folders of the basePath</param>
        public ScopedFilesMonitor(string basePath, List<string> filters, bool includeSubdirectories)
        {
            Logger.Debug("Creating instance");
            _basePath = basePath;
            _filters = filters;
            _includeSubdirectories = includeSubdirectories;

            Initialize();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) Cleanup();
        }

        private void Cleanup()
        {
            _files.Clear();

            // Remove any existing parentWatchers
            if (_parentWatcher != null)
            {
                _parentWatcher.EnableRaisingEvents = false;
                _parentWatcher.Created -= OnParentFolderCreated;
                _parentWatcher.Error -= OnError;
                _parentWatcher.Dispose();
                _parentWatcher = null;
            }

            // Remove any existing watchers
            foreach (var fileSystemWatcher in _watchers)
            {
                fileSystemWatcher.Deconstruct(out var key, out var watcher);
                watcher.EnableRaisingEvents = false;
                watcher.Created -= OnFileCreated;
                watcher.Deleted -= OnFileDeleted;
                watcher.Error -= OnError;
                watcher.Dispose();
                _watchers.Remove(key);
            }
        }

        private void Initialize()
        {
            Cleanup();

            // Setup new watchers
            if (Directory.Exists(_basePath))
            {
                foreach (var filter in _filters)
                {
                    Logger.Debug(
                        "Setting up watcher for basePath={basePath} and filter={filter} with includeSubDirectories={includeSubDirectories}",
                        _basePath, filter, _includeSubdirectories);
                    SetupWatcher(filter);

                    var alreadyExistingFiles = Directory.GetFiles(_basePath, filter, new EnumerationOptions{ RecurseSubdirectories = _includeSubdirectories});
                    foreach (var file in alreadyExistingFiles)
                    {
                        var fi = new FileInfo(file);
                        _files.AddOrUpdate(fi);
                        Logger.Debug("Added file {FileName}. _files.Length={FileCount}", fi.FullName, _files.Count);
                    }
                }
            }
            else
            {
                Logger.Debug(
                    "The basePath '{basePath}' does not exist. Setting up watcher for first existing parentFolder",
                    _basePath);
                SetupParentWatcher();
            }
        }

        private void OnError(object _, ErrorEventArgs e)
        {
            Logger.Error("System error while watching files.", e.GetException());
            Initialize();
        }

        private void OnFileCreated(object _, FileSystemEventArgs e)
        {
            Logger.Debug("File ChangeType={ChangeType}: Name={Name} FullPath={FullPath}", e.ChangeType,
                e.Name, e.FullPath);
            var fi = new FileInfo(e.FullPath);
            _files.AddOrUpdate(fi);
            Logger.Debug("Added file {FileName}. _files.Length={FileCount}", fi.FullName, _files.Count);
        }

        private void OnFileDeleted(object _, FileSystemEventArgs e)
        {
            Logger.Debug("File {ChangeType}: Name={Name} FullPath={FullPath}", e.ChangeType,
                e.Name, e.FullPath);
            var fi = new FileInfo(e.FullPath);
            _files.Remove(fi.FullName);
            Logger.Debug("Removed file {FileName}. _files.Length={FileCount}", fi.FullName, _files.Count);
        }

        private void OnParentFolderCreated(object sender, FileSystemEventArgs e)
        {
            Logger.Debug("ParentFolder {ChangeType}: Name={Name} FullPath={FullPath}", e.ChangeType,
                e.Name, e.FullPath);
            Initialize();
        }

        private void SetupParentWatcher()
        {
            var directories = _basePath.Split(Path.DirectorySeparatorChar);
            for (var i = directories.Length - 1; i >= 0; i--)
            {
                var parentFolder = Path.Combine(directories.Take(i).ToArray());
                if (Directory.Exists(parentFolder))
                {
                    var lookForFolder = directories[i];
                    _parentWatcher = new FileSystemWatcher(parentFolder, lookForFolder);
                    //_parentWatcher.NotifyFilter = NotifyFilters.DirectoryName;
                    _parentWatcher.Created += OnParentFolderCreated;
                    _parentWatcher.Error += OnError;
                    _parentWatcher.EnableRaisingEvents = true;
                    break;
                }
            }
        }

        private void SetupWatcher(string filter)
        {
            var watcher = new FileSystemWatcher(_basePath, filter);
            watcher.IncludeSubdirectories = _includeSubdirectories;
            //watcher.NotifyFilter = NotifyFilters.Size; // | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            watcher.Created += OnFileCreated;
            watcher.Deleted += OnFileDeleted;
            watcher.Deleted += OnFileDeleted;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(filter, watcher);
        }
    }
}