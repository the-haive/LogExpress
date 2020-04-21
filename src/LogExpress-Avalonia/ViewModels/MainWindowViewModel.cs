using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using LogExpress.Models;
using LogExpress.Views;
using ReactiveUI;
using Serilog;

namespace LogExpress.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public static readonly ILogger Logger = Log.ForContext<MainWindowViewModel>();

        private static readonly TimeSpan LogArgsChangeThreshold = new TimeSpan(0, 0, 0, 0, 100);

        private static readonly GridLength FilterGridCollapsed = new GridLength(0);
        private static readonly GridLength FilterGridExpanded = new GridLength(1, GridUnitType.Star);

        private readonly IDisposable _logViewSubscription;

        private string _baseDir = string.Empty;

        private string _filter = string.Empty;

        private FilterViewModel _filterViewModel;

        private string _infoBarScope;

        private ObservableAsPropertyHelper<string> _infoBarSelectedLine;

        private ObservableAsPropertyHelper<string> _infoBarTotalSize;

        private LogViewModel _logViewModel;

        private bool _recursive;

        private bool _showFilterPanel;

        private bool _showLogPanel;

        private GridLength _filterPaneHeight = FilterGridCollapsed;
        private GridLength _lastFilterPaneHeight = FilterGridExpanded;
        private readonly MainWindow _mainWindow;

        private readonly StyleInclude _lightTheme = new StyleInclude(new Uri("resm:Styles?assembly=ControlCatalog")) 
        { 
            Source = new Uri("resm:Avalonia.Themes.Default.Accents.BaseLight.xaml?assembly=Avalonia.Themes.Default") 
        }; 
        private readonly StyleInclude _darkTheme = new StyleInclude(new Uri("resm:Styles?assembly=ControlCatalog")) 
        { 
            Source = new Uri("resm:Avalonia.Themes.Default.Accents.BaseDark.xaml?assembly=Avalonia.Themes.Default") 
        }; 

        public MainWindowViewModel(MainWindow mainWindow)
        {
            Logger.Information("Application started");
            _mainWindow = mainWindow;

            OpenLogFileCommand = ReactiveCommand.Create(OpenLogFileExecute);
            OpenLogSetCommand = ReactiveCommand.Create(OpenLogSetExecute);
            ToggleFilterPaneCommand = ReactiveCommand.Create(ToggleFilterPaneExecute);
            ToggleDarkThemeCommand = ReactiveCommand.Create(ToggleDarkThemeExecute);
            ExitCommand = ReactiveCommand.Create(ExitApplication);

            // Reactive trigger for when the basedir, filters and recursive has changed
            _logViewSubscription = this
                .WhenAnyValue(x => x.BaseDir, x => x.Filter, x => x.Recursive)
                .Where(((string baseDir, string filter, bool recursive) observerTuple) =>
                {
                    var (baseDir, filter, _) = observerTuple;
                    return !string.IsNullOrWhiteSpace(baseDir) && !string.IsNullOrWhiteSpace(filter);
                })
                .Throttle(LogArgsChangeThreshold)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .Subscribe(SetupLogView);

            // TODO: Add trigger for open filter button

            // TODO: Add handler for drag and drop of OS file to the main-window

            // Parse args (this constructor should only be called once in the application lifespan
            var args = Environment.GetCommandLineArgs().Skip(1).ToList();
            if (args.Count > 0)
                ParseArgs(args);
            else
            {
                var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow(
                    "No logfile passed as argument",
                    "Use the OpenLog Or OpenSet menus to open log-files."
                );
                messageBoxStandardWindow.ShowDialog(_mainWindow);
            }
        }

        private void ToggleDarkThemeExecute()
        {
            // NB! Depends on the first Window.Styles StyleInclude to be the theme, and the third App.Styles to be the theme
            if (_mainWindow.DarkThemeControl.IsChecked != null && _mainWindow.DarkThemeControl.IsChecked.Value)
            {
                _mainWindow.Styles[0] = Application.Current.Styles[2] = _darkTheme;
            }
            else
            {
                _mainWindow.Styles[0] = Application.Current.Styles[2] = _lightTheme;
            }

            Logger.Verbose("Switched theme to {Theme}",
                _mainWindow.DarkThemeControl.IsChecked != null && _mainWindow.DarkThemeControl.IsChecked.Value
                    ? "Dark"
                    : "Light");
        }

        private void ToggleFilterPaneExecute()
        {
            if (ShowFilterPanel)
            {
                _lastFilterPaneHeight = FilterPaneHeight;
                FilterPaneHeight = FilterGridCollapsed;
            }
            else
            {
                FilterPaneHeight = _lastFilterPaneHeight;
            }

            ShowFilterPanel = !ShowFilterPanel;
        }

        public string BaseDir
        {
            get => _baseDir;
            set => this.RaiseAndSetIfChanged(ref _baseDir, value);
        }

        public ReactiveCommand<Unit, Unit> ExitCommand { get; }

        public string Filter
        {
            get => _filter;
            set => this.RaiseAndSetIfChanged(ref _filter, value);
        }

        public FilterViewModel FilterViewModel
        {
            get => _filterViewModel;
            set => this.RaiseAndSetIfChanged(ref _filterViewModel, value);
        }

        public string InfoBarScope
        {
            get => _infoBarScope;
            set => this.RaiseAndSetIfChanged(ref _infoBarScope, value);
        }

        public string InfoBarSelectedLine => _infoBarSelectedLine?.Value;

        public string InfoBarTotalSize => _infoBarTotalSize?.Value;

        public LogViewModel LogViewModel
        {
            get => _logViewModel;
            set => this.RaiseAndSetIfChanged(ref _logViewModel, value);
        }

        public ReactiveCommand<Unit, Unit> OpenLogFileCommand { get; }

        public ReactiveCommand<Unit, Unit> OpenLogSetCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleDarkThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleFilterPaneCommand { get; }

        public bool Recursive
        {
            get => _recursive;
            set => this.RaiseAndSetIfChanged(ref _recursive, value);
        }

        public bool ShowFilterPanel
        {
            get => _showFilterPanel;
            set => this.RaiseAndSetIfChanged(ref _showFilterPanel, value);
        }

        public bool ShowLogPanel
        {
            get => _showLogPanel;
            set => this.RaiseAndSetIfChanged(ref _showLogPanel, value);
        }
        public GridLength FilterPaneHeight
        {
            get => _filterPaneHeight;
            set => this.RaiseAndSetIfChanged(ref _filterPaneHeight, value);
        }

        private void ExitApplication()
        {
            Logger.Information("Application exited from menu");
            (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }

        ~MainWindowViewModel()
        {
            _logViewSubscription?.Dispose();
            //_listBoxScrollViewerControl.ScrollChanged -= RefreshLineContentForVisibleChildren;
        }

        private async void OpenLogFileExecute()
        {
            Logger.Verbose("OpenLogFile clicked");
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select folder",
                AllowMultiple = false,
                InitialFileName = "*.log",
                Directory = BaseDir
            };

            var file = await openFileDialog.ShowAsync(App.MainWindow);

            if (file.Length <= 0)
            {
                Logger.Information("No file selected");
                return;
            }

            var fileName = file.First();
            Logger.Information("Selected file {LogFile}", fileName);
            var fileInfo = new FileInfo(fileName);
            var dirInfo = Directory.GetParent(fileInfo.FullName);
            BaseDir = dirInfo.FullName;
            Filter = fileInfo.Name;
            Recursive = false;
        }

        private async void OpenLogSetExecute()
        {
            Logger.Verbose("OpenLogSet clicked: Not implemented yet");
            /*
                        var openFolderDialog = new OpenFolderDialog()
                        {
                            Title = "Select folder",
                        };
                        var folder = await openFolderDialog.ShowAsync(App.MainWindow);
            */
        }

        private void ParseArgs(List<string> args)
        {
            var origLength = args.Count;
            args = args.Where(a => a != "-r").ToList();

            Recursive = args.Count < origLength;

            if (args.Count > 0)
            {
                if (File.Exists(args[0]))
                {
                    var dirInfo = Directory.GetParent(args[0]);
                    BaseDir = dirInfo.FullName;
                    Filter = new FileInfo(args[0]).Name;
                }
                else
                {
                    if (Directory.Exists(args[0]))
                    {
                        var dirInfo = new DirectoryInfo(args[0]);
                        BaseDir = dirInfo.FullName;
                        if (args.Count > 1)
                            Filter = args[1];
                        else
                            Filter = "*.log";
                    }
                }
            }
        }

        private void SetFilterViewDataContext()
        {
            FilterViewModel = new FilterViewModel();
        }

        private void SetupLogView((string baseDir, string filter, bool recursive) observerTuple)
        {
            var (baseDir, filter, recursive) = observerTuple;
            Logger.Debug("Scope changed, setting up the LogView DataContext");
            ShowLogPanel = true;
            LogViewModel = new LogViewModel(baseDir, filter, recursive);

            // Set the InfoBar property for the scope selected
            InfoBarScope = $"Scope: {BaseDir}{Path.DirectorySeparatorChar}{Filter} {(Recursive ? "(recursive)" : "")}";

            // Setup subscription for showing the selected line info in the InfoBar
            _infoBarSelectedLine = _logViewModel.WhenAnyValue(x => x.LineSelected, x => x.LogFiles)
                .Where(((LineItem lineSelected, ReadOnlyObservableCollection<FileInfo> logFiles) tuple) =>
                {
                    var (lineSelected, logFiles) = tuple;
                    return lineSelected != null && logFiles != null && logFiles.Any();
                })
                .Select(((LineItem lineSelected, ReadOnlyObservableCollection<FileInfo> logFiles) tuple) =>
                {
                    var (lineSelected, logFiles) = tuple;
                    var fileInfo = logFiles.ElementAt(lineSelected.LogFileIndex);
                    return $"Selected: [Line {lineSelected.LineNum:n0}] [Pos {lineSelected.Position:n0}] [File {fileInfo.Name}] [Path {fileInfo.DirectoryName}]";
                })
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.InfoBarSelectedLine);

            // Setup subscription for showing the total size in the InfoBar
            _infoBarTotalSize = _logViewModel.WhenAnyValue(x => x.HumanTotalSize, x => x.Lines.Count)
                .Where(x => !string.IsNullOrWhiteSpace(x.Item1))
                .Select(x => $"Size: {x.Item1} Lines: {x.Item2}")
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.InfoBarTotalSize);

        }
    }
}
