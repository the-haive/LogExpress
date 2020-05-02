using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using LogExpress.Views;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using DynamicData;
using DynamicData.Binding;
using LogExpress.Models;
using LogExpress.Services;

namespace LogExpress.ViewModels
{
    public class OpenLogSetViewModel : ViewModelBase
    {
        private IDisposable _scopedFileMonitorSubscription;
        private ScopedFileMonitor _scopedFileMonitor;
        private IDisposable _fileMonitorSubscription;

        public OpenLogSetViewModel(OpenLogSetView view, string folder)
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
                    (string basePath, string filter, bool includeSubDirectories) = observer;
                    _scopedFileMonitor?.Dispose();
                    _scopedFileMonitor = new ScopedFileMonitor(basePath, new List<string> {filter}, includeSubDirectories);

                    _fileMonitorSubscription?.Dispose();
                    _fileMonitorSubscription = _scopedFileMonitor.Connect()
                        .Sort(SortExpressionComparer<ScopedFile>.Ascending(t => t.CreationTime))
                        .Subscribe(LogFilesReady);
                });
        }

        private void LogFilesReady(IChangeSet<ScopedFile, ulong> changes = null)
        {
            if (changes == null) return;
            foreach (var change in changes)
            {
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

        private ScopedFile _selectedLogFile;

        public ScopedFile SelectedLogFile
        {
            get => _selectedLogFile;
            set => this.RaiseAndSetIfChanged(ref _selectedLogFile, value);
        }

        private string _folder;

        public string Folder
        {
            get => _folder;
            set => this.RaiseAndSetIfChanged(ref _folder, value);
        }

        private string _pattern;

        public string Pattern
        {
            get => _pattern;
            set => this.RaiseAndSetIfChanged(ref _pattern, value);
        }

        private bool _recursive = true;

        public bool Recursive
        {
            get => _recursive;
            set => this.RaiseAndSetIfChanged(ref _recursive, value);
        }
        
        public ObservableCollection<ScopedFile> LogFiles { get; set; } = new ObservableCollection<ScopedFile>();

        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }

        public ReactiveCommand<Unit, Unit> OpenCommand { get; set; }
        public ReactiveCommand<Unit, Unit> OpenSelectedCommand { get; set; }

        public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; set; }

        public OpenLogSetView View { get; }

        private void CancelExecute()
        {
            View.Close();
        }

        private void OpenSelectedExecute()
        {
            View.Close(new Result {SelectedFile = SelectedLogFile, Folder = Folder, Pattern = Pattern, Recursive = Recursive});
        }

        private void OpenExecute()
        {
            View.Close(new Result {SelectedFile = null, Folder = Folder, Pattern = Pattern, Recursive = Recursive});
        }

        private async void SelectFolderExecute()
        {
            var openFolderDialog = new OpenFolderDialog()
            {
                Directory = Folder,
                Title = "Select log-folder",
            };

            var result = await openFolderDialog.ShowAsync(this.View);
            if (!string.IsNullOrWhiteSpace(result)) Folder = result;
        }

        public class Result
        {
            public ScopedFile SelectedFile;
            public string Folder;
            public string Pattern;
            public bool Recursive;
        }
    }

}
