using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using ByteSizeLib;
using LogExpress.Services;
using LogExpress.Utils;
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

        private string _folder = string.Empty;

        private string _pattern = string.Empty;
        private string _appTitle = "LogExpress";

        private FilterViewModel _filterViewModel;

        private string _infoBarScope;

        private ObservableAsPropertyHelper<string> _infoBarByteSize;
        private ObservableAsPropertyHelper<string> _infoBarLineCount;
        private string _infoBarLogLevels;
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

        private LogView _logView;

        public MainWindowViewModel(MainWindow mainWindow)
        {
            AppTitle = App.TitleWithVersion;
            Logger.Information("Application started");

            _mainWindow = mainWindow;
            _logView = _mainWindow.LogView;

            OpenLogFileCommand = ReactiveCommand.Create(OpenLogFileExecute);
            OpenLogSetCommand = ReactiveCommand.Create(OpenLogSetExecute);
            ToggleFilterPaneCommand = ReactiveCommand.Create(ToggleFilterPaneExecute);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleThemeExecute);
            ExitCommand = ReactiveCommand.Create(ExitApplication);

            // Reactive trigger for when the basedir, filters and recursive has changed
            _logViewSubscription = this
                .WhenAnyValue(x => x.Folder, x => x.Pattern, x => x.Recursive)
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

            mainWindow.AddHandler(DragDrop.DragOverEvent, DragOver);
            mainWindow.AddHandler(DragDrop.DropEvent, Drop);
        }

        private void DragOver(object sender, DragEventArgs e)
        {
            // Only allow Copy or Link as Drop Operations.
            e.DragEffects = e.DragEffects & (DragDropEffects.Copy | DragDropEffects.Link);

            // Only allow if the dragged data contains text or filenames.
            if (!e.Data.Contains(DataFormats.Text) && !e.Data.Contains(DataFormats.FileNames))
                e.DragEffects = DragDropEffects.None;
        }

        private void Drop(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Text))
            {
                Logger.Information("Text dropped in application: {Text}", e.Data.GetText());
            }
            else if (e.Data.Contains(DataFormats.FileNames))
            {
                Logger.Information("Files(s) dropped in application: {Filenames}", string.Join(", ", e.Data.GetFileNames()));
            }
        }


        private void ToggleThemeExecute()
        {
            // NB! Depends on the first Window.Styles StyleInclude to be the theme, and the third App.Styles to be the theme
            if (_mainWindow.ThemeControl.IsChecked != null && _mainWindow.ThemeControl.IsChecked.Value)
            {
                _mainWindow.Styles[0] = Application.Current.Styles[2] = _darkTheme;
            }
            else
            {
                _mainWindow.Styles[0] = Application.Current.Styles[2] = _lightTheme;
            }

            Logger.Verbose("Switched theme to '{Theme}'",
                _mainWindow.ThemeControl.IsChecked != null && _mainWindow.ThemeControl.IsChecked.Value
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

        public string Folder
        {
            get => _folder;
            set => this.RaiseAndSetIfChanged(ref _folder, value);
        }

        public ReactiveCommand<Unit, Unit> ExitCommand { get; }

        public string Pattern
        {
            get => _pattern;
            set => this.RaiseAndSetIfChanged(ref _pattern, value);
        }

        public string AppTitle
        {
            get => _appTitle;
            set => this.RaiseAndSetIfChanged(ref _appTitle, value);
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

        public string InfoBarByteSize => _infoBarByteSize?.Value;
        public string InfoBarLineCount => _infoBarLineCount?.Value;
        public string InfoBarLogLevels
        {
            get => _infoBarLogLevels;
            set => this.RaiseAndSetIfChanged(ref _infoBarLogLevels, value);
        }

        public LogViewModel LogViewModel
        {
            get => _logViewModel;
            set => this.RaiseAndSetIfChanged(ref _logViewModel, value);
        }

        public ReactiveCommand<Unit, Unit> OpenLogFileCommand { get; }

        public ReactiveCommand<Unit, Unit> OpenLogSetCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
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
                Directory = Folder
            };

            var file = await openFileDialog.ShowAsync(App.MainWindow);

            if (file?.Length <= 0)
            {
                Logger.Information("No file selected");
                return;
            }

            var fileName = file.First();
            OpenFile(fileName);
        }

        private void OpenFile(string fileName)
        {
            Logger.Information("Selected file {LogFile}", fileName);
            var fileInfo = new FileInfo(fileName);
            var dirInfo = Directory.GetParent(fileInfo.FullName);
            Folder = dirInfo.FullName;
            Pattern = fileInfo.Name;
            Recursive = false;
        }

        private async void OpenLogSetExecute()
        {

            var openLogSetView = new OpenLogSetView();
            openLogSetView.DataContext = new OpenLogSetViewModel(openLogSetView, _folder);
            var result = await openLogSetView.ShowDialog<OpenLogSetViewModel.Result>(App.MainWindow);
            if (result == null) return;

            if (result.SelectedFile != null)
            {
                OpenFile(result.SelectedFile.FullName);
            }
            else
            {
                Folder = result.Folder;
                Pattern = result.Pattern;
                Recursive = result.Recursive;
            }
        }


        private void ParseArgs(List<string> args)
        {
            var optDebugger = args.Contains("-d");
            var optWaitForDebugger = args.Contains("-w");

            if (!Debugger.IsAttached && optDebugger)
            {
                if (optWaitForDebugger)
                {
                    Logger.Information("Waiting for debugger to attach. Process ID: {ProcessID}...", Process.GetCurrentProcess().Id);
                    while (!Debugger.IsAttached)
                    {
                        Thread.Sleep(100);
                    }
                }
                else
                {
                    Debugger.Launch();
                }
            }

            Recursive = args.Contains("-r");

            args = args.Where(a => !new List<string>{"-r", "-d", "-w"}.Contains(a)).ToList();

            if (args.Count > 0)
            {
                Debugger.Launch();
                if (File.Exists(args[0]))
                {
                    var dirInfo = Directory.GetParent(args[0]);
                    Folder = dirInfo.FullName;
                    Pattern = new FileInfo(args[0]).Name;
                }
                else
                {
                    Folder = args[0];
                    Pattern = args.Count > 1 ? args[1] : "*.log";
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
            LogViewModel = new LogViewModel(baseDir, filter, recursive, _logView);

            // Set the InfoBar property for the scope selected
            InfoBarScope = $"Scope: {Folder}{Path.DirectorySeparatorChar}{Pattern} {(Recursive ? "(recursive)" : "")}";

            // TODO: Add info on number of files in the set

/*
            // Setup subscription for showing the selected line info in the InfoBar
            _infoBarSelectedLine = _logViewModel.WhenAnyValue(x => x.LineSelected, x => x.LogFiles)
                .Where(((LineItem lineSelected, ReadOnlyObservableCollection<ScopedFile> logFiles) tuple) =>
                {
                    var (lineSelected, logFiles) = tuple;
                    return lineSelected != null && logFiles != null && logFiles.Any();
                })
                .Select(((LineItem lineSelected, ReadOnlyObservableCollection<ScopedFile> logFiles) tuple) =>
                {
                    var (lineSelected, _) = tuple;
                    var fileInfo = lineSelected.LogFile;
                    if (fileInfo == null) return "Error finding the file based on selected line";
                    return $"Selected: [Line {lineSelected.LineNumber:n0}] [Pos {lineSelected.Position:n0}] [File {fileInfo.Name}] [Path {fileInfo.DirectoryName}] [LogLevel {lineSelected.LogLevel}]";
                })
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.InfoBarSelectedLine);
*/

            // Setup subscription for showing the total size in the InfoBar
            // TODO: Implement filters in VirtualLogFile, and show both the filtered and total size, i.e. 'Size: 4kb/21Mb'
            _infoBarByteSize = LogViewModel.VirtualLogFile.WhenAnyValue(x => x.TotalSize)
                .Select(x => ByteSize.FromBytes(x).ToString())
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.InfoBarByteSize);

            // TODO: Add info on oldest and newest date in the log-file

            // TODO: Implement filters in VirtualLogFile, and show both the filtered and total no of lines, i.e. 'Lines: 12/931'
            _infoBarLineCount = LogViewModel.VirtualLogFile.WhenAnyValue(x => x.FilteredLines.Count)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Select(x => x.ToString("N0"))
                .ToProperty(this, x => x.InfoBarLineCount);

            // TODO: Implement filters in VirtualLogFile, and show both the filtered and total count, i.e. 'Trace: 4/19'
            LogViewModel.VirtualLogFile
                .WhenAnyValue(x => x.LogLevelStats)
                .Where(x => x != null)
                .Subscribe(obs =>
                {
                    var logStatsChanged =
                        Observable.FromEvent<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                            handler =>
                            {
                                NotifyCollectionChangedEventHandler chHandler = (sender, e) => { handler(e); };
                                return chHandler;
                            },
                            chHandler => obs.CollectionChanged += chHandler,
                            chHandler => obs.CollectionChanged -= chHandler);
                    logStatsChanged
                        .DistinctUntilChanged()
                        .Subscribe(_ =>
                    {
                        var logLevelStats = LogViewModel.VirtualLogFile.LogLevelStats;
                        if (logLevelStats == null || !logLevelStats.Any()) return;

                        foreach (var (level, count) in logLevelStats)
                        {
                            if (level == 0)
                            {
                                InfoBarLogLevel0 = $"No log-level: {count}";
                            }
                            else if (level == 1)
                            {
                                InfoBarLogLevel1 = $"Trace: {count}";
                            }
                            else if (level == 2)
                            {
                                InfoBarLogLevel2 = $"Debug: {count}";
                            }
                            else if (level == 3)
                            {
                                InfoBarLogLevel3 = $"Info: {count}";
                            }
                            else if (level == 4)
                            {
                                InfoBarLogLevel4 = $"Warn: {count}";
                            }
                            else if (level == 5)
                            {
                                InfoBarLogLevel5 = $"Error: {count}";
                            }
                            else if (level == 6)
                            {
                                InfoBarLogLevel6 = $"Fatal: {count}";
                            }
                        }
                    });
                });
        }

        private string _infoBarLogLevel0;

        public string InfoBarLogLevel0
        {
            get => _infoBarLogLevel0;
            set => this.RaiseAndSetIfChanged(ref _infoBarLogLevel0, value);
        }

        private string _infoBarLogLevel1;

        public string InfoBarLogLevel1
        {
            get => _infoBarLogLevel1;
            set => this.RaiseAndSetIfChanged(ref _infoBarLogLevel1, value);
        }

        private string _infoBarLogLevel2;

        public string InfoBarLogLevel2
        {
            get => _infoBarLogLevel2;
            set => this.RaiseAndSetIfChanged(ref _infoBarLogLevel2, value);
        }

        private string _infoBarLogLevel3;

        public string InfoBarLogLevel3
        {
            get => _infoBarLogLevel3;
            set => this.RaiseAndSetIfChanged(ref _infoBarLogLevel3, value);
        }

        private string _infoBarLogLevel4;

        public string InfoBarLogLevel4
        {
            get => _infoBarLogLevel4;
            set => this.RaiseAndSetIfChanged(ref _infoBarLogLevel4, value);
        }

        private string _infoBarLogLevel5;

        public string InfoBarLogLevel5
        {
            get => _infoBarLogLevel5;
            set => this.RaiseAndSetIfChanged(ref _infoBarLogLevel5, value);
        }

        private string _infoBarLogLevel6;

        public string InfoBarLogLevel6 { 
            get => _infoBarLogLevel6;
            set => this.RaiseAndSetIfChanged(ref _infoBarLogLevel6, value);
        }
    }
}
