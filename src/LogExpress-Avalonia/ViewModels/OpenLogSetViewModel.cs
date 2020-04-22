using LogExpress.Views;
using ReactiveUI;
using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using Tmds.DBus;

namespace LogExpress.ViewModels
{
    public class OpenLogSetViewModel : ViewModelBase
    {
        public OpenLogSetViewModel(OpenLogSetView view, string folder, string pattern, bool recursive)
        {
            View = view;
            Folder = folder;
            Pattern = pattern;
            Recursive = recursive;

            SelectFolderCommand = ReactiveCommand.Create(SelectFolderExecute);
            CancelCommand = ReactiveCommand.Create(CancelExecute);
            OpenCommand = ReactiveCommand.Create(OpenExecute);
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
        
        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }

        public ReactiveCommand<Unit, Unit> OpenCommand { get; set; }

        public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; set; }

        public OpenLogSetView View { get; }

        private void CancelExecute()
        {
            View.Close();
        }

        private async void OpenExecute()
        {
            View.Close(new Result {Folder = Folder, Pattern = Pattern, Recursive = Recursive});
        }

        private async void SelectFolderExecute()
        {
            var openFolderDialog = new OpenFolderDialog()
            {
                Directory = Folder,
                Title = "Select log-folder"
            };

            var result = await openFolderDialog.ShowAsync(this.View);
            if (!string.IsNullOrWhiteSpace(result)) Folder = result;
        }

        public class Result
        {
            public string Folder;
            public string Pattern;
            public bool Recursive;
        }
    }

}
