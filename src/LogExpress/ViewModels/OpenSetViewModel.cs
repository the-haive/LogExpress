using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using DynamicData;
using DynamicData.Binding;
using LogExpress.Models;
using LogExpress.Services;
using LogExpress.Views;
using ReactiveUI;

namespace LogExpress.ViewModels
{
    public class OpenSetViewModel : ViewModelBase, IDisposable
    {
        private IDisposable _fileMonitorSubscription;
        private string _folder;
        private string _pattern;
        private bool _recursive = true;
        private ScopedFileMonitor _scopedFileMonitor;
        private readonly IDisposable _scopedFileMonitorSubscription;
        private ScopedFile _selectedLogFile;

        public OpenSetViewModel(OpenSetView view, string folder)
        {
            View = view;
            Folder = folder;
            Pattern = "*.log";
            Recursive = false;

            SelectFolderCommand = ReactiveCommand.Create(SelectFolderExecute);
            CancelCommand = ReactiveCommand.Create(CancelExecute);
            OpenCommand = ReactiveCommand.Create(OpenExecute);
            OpenSelectedCommand = ReactiveCommand.Create(OpenSelectedExecute);

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
        }

        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }

        public string Folder
        {
            get => _folder;
            set => this.RaiseAndSetIfChanged(ref _folder, value);
        }

        public ObservableCollection<ScopedFile> LogFiles { get; set; } = new ObservableCollection<ScopedFile>();

        public ReactiveCommand<Unit, Unit> OpenCommand { get; set; }

        public ReactiveCommand<Unit, Unit> OpenSelectedCommand { get; set; }

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

        public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; set; }

        public OpenSetView View { get; }

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
            View.Close(new OpenSetResult
                {SelectedFile = null, Folder = Folder, Pattern = Pattern, Recursive = Recursive});
        }

        private void OpenSelectedExecute()
        {
            View.Close(new OpenSetResult
                {SelectedFile = SelectedLogFile, Folder = Folder, Pattern = Pattern, Recursive = Recursive});
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

        public class OpenSetResult
        {
            public string Folder;
            public string Pattern;
            public bool Recursive;
            public ScopedFile SelectedFile;
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
