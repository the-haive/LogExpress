using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using Avalonia.Controls;
using DynamicData;
using DynamicData.Binding;
using JetBrains.Annotations;
using LogExpress.Models;
using LogExpress.Services;
using LogExpress.Views;
using ReactiveUI;

namespace LogExpress.ViewModels
{
    public class OpenSetViewModel : ViewModelBase, IDisposable
    {
        private IDisposable _fileMonitorSubscription;
        private string _folder = string.Empty;
        private string _pattern = "*.log";
        private bool _recursive;
        private ScopedFileMonitor _scopedFileMonitor;
        private readonly IDisposable _scopedFileMonitorSubscription;
        private ScopedFile _selectedLogFile;

        public OpenSetViewModel(OpenSetView view, ScopeSettings settings)
        {
            View = view;
            if (settings != null)
            {
                Folder = settings.Folder ?? string.Empty;
                Pattern = settings.Pattern ?? "*.log";
                Recursive = settings.Recursive;
            }

            SelectFolderCommand = ReactiveCommand.Create(SelectFolderExecute);
            CancelCommand = ReactiveCommand.Create(CancelExecute);
            ConfigureSetCommand = ReactiveCommand.Create(ConfigureSetExecute);
            ConfigureFileCommand = ReactiveCommand.Create(ConfigureFileExecute);

            _scopedFileMonitorSubscription = this.WhenAnyValue(x => x.Folder, x => x.Pattern, x => x.Recursive)
                .Subscribe(observer =>
                {
                    var (basePath, filter, includeSubDirectories) = observer;
                    _scopedFileMonitor?.Dispose();
                    _scopedFileMonitor =
                        new ScopedFileMonitor(basePath, new List<string> {filter}, includeSubDirectories);

                    _fileMonitorSubscription?.Dispose();
                    _fileMonitorSubscription = _scopedFileMonitor.Connect()
                        .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                        .Subscribe(LogFilesReady);
                });

            this.RaisePropertyChanged(nameof(Folder));
            this.RaisePropertyChanged(nameof(Pattern));
            this.RaisePropertyChanged(nameof(Recursive));
        }

        public ObservableCollection<ScopedFile> LogFiles { get; } = new ObservableCollection<ScopedFile>();

        public ReactiveCommand<Unit, Unit> CancelCommand { [UsedImplicitly] get; }
        
        public ReactiveCommand<Unit, Unit> ConfigureSetCommand { [UsedImplicitly] get; }

        public ReactiveCommand<Unit, Unit> ConfigureFileCommand { [UsedImplicitly] get; }

        public string Folder
        {
            get => _folder;
            set => this.RaiseAndSetIfChanged(ref _folder, value);
        }

        public string Pattern
        {
            get => _pattern;
            set => this.RaiseAndSetIfChanged(ref _pattern, value);
        }

        public bool Recursive
        {
            get => _recursive;
            set => this.RaiseAndSetIfChanged(ref _recursive, value);
        }

        public ScopedFile SelectedLogFile
        {
            get => _selectedLogFile;
            set => this.RaiseAndSetIfChanged(ref _selectedLogFile, value);
        }

        public ReactiveCommand<Unit, Unit> SelectFolderCommand { [UsedImplicitly] get; }

        public OpenSetView View { get; }

        private void CancelExecute()
        {
            View.Close();
        }

        private void LogFilesReady(IChangeSet<ScopedFile, ulong> changes = null)
        {
            if (changes == null) return;
            foreach (var change in changes){
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
/*
                        var logFile = LogFiles.FirstOrDefault(l => l == change.Current);
                        logFile = change.Current;
*/
                        break;

                    case ChangeReason.Moved:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void ConfigureSetExecute()
        {
            var hasFiles = LogFiles.Count > 0;
            View.Close((new ScopeSettings
            {
                Folder = Folder, 
                Pattern = Pattern, 
                Recursive = Recursive
            }, hasFiles));
        }

        private void ConfigureFileExecute()
        {
            var hasFiles = LogFiles.Count > 0;
            var fileInfo = new FileInfo(SelectedLogFile.FullName);
            var dirInfo = Directory.GetParent(fileInfo.FullName);
            View.Close((new ScopeSettings
            {
                Folder = dirInfo.FullName, 
                Pattern = fileInfo.Name, 
                Recursive = false
            }, hasFiles));
        }

        private async void SelectFolderExecute()
        {
            var openFolderDialog = new OpenFolderDialog
            {
                Directory = Folder,
                Title = "Select log-folder"
            };

            var result = await openFolderDialog.ShowAsync(View);
            if (!string.IsNullOrWhiteSpace(result)) Folder = result;
        }

        #region Implementation of IDisposable

        ~OpenSetViewModel()
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

            _fileMonitorSubscription?.Dispose();
            _scopedFileMonitorSubscription?.Dispose();
            _scopedFileMonitor?.Dispose();
        }

        #endregion Implementation of IDisposable
    }
}
