using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using ByteSizeLib;
using LogExpress.Models;
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

        private static readonly GridLength FilterGridCollapsed = new GridLength(0);
        private static readonly GridLength FilterGridExpanded = new GridLength(1, GridUnitType.Star);
        private static readonly TimeSpan LogArgsChangeThreshold = new TimeSpan(0, 0, 0, 0, 100);
        private readonly StyleInclude _darkTheme = new StyleInclude(new Uri("resm:Styles?assembly=ControlCatalog"))
        {
            Source = new Uri("resm:Avalonia.Themes.Default.Accents.BaseDark.xaml?assembly=Avalonia.Themes.Default")
        };

        private readonly StyleInclude _lightTheme = new StyleInclude(new Uri("resm:Styles?assembly=ControlCatalog"))
        {
            Source = new Uri("resm:Avalonia.Themes.Default.Accents.BaseLight.xaml?assembly=Avalonia.Themes.Default")
        };

        private IDisposable _logViewSubscription;

        private readonly MainWindow _mainWindow;
        private string _appTitle = "LogExpress";
        private GridLength _filterPaneHeight = FilterGridCollapsed;
        private FilterViewModel _filterViewModel;
        private string _folder = string.Empty;
        private Layout _layout = null;
        private ObservableAsPropertyHelper<string> _infoBarByteSize;
        private ObservableAsPropertyHelper<string> _infoBarByteSizeFilter;
        private ObservableAsPropertyHelper<string> _infoBarLineCount;
        private ObservableAsPropertyHelper<string> _infoBarLineCountFilter;
        private string _infoBarSeverity0;
        private string _infoBarSeverity0Filter;
        private string _infoBarSeverity1;
        private string _infoBarSeverity1Filter;
        private string _infoBarSeverity2;
        private string _infoBarSeverity2Filter;
        private string _infoBarSeverity3;
        private string _infoBarSeverity3Filter;
        private string _infoBarSeverity4;
        private string _infoBarSeverity4Filter;
        private string _infoBarSeverity5;
        private string _infoBarSeverity5Filter;
        private string _infoBarSeverity6;
        private string _infoBarSeverity6Filter;
        private ObservableAsPropertyHelper<string> _infoBarRange;
        private ObservableAsPropertyHelper<string> _infoBarRangeFilter;
        private ObservableAsPropertyHelper<string> _infoBarRangeToolTip;
        private ObservableAsPropertyHelper<string> _infoBarRangeToolTipFilter;
        private string _infoBarScope;
        private string _infoBarScopeFilter;
        private GridLength _lastFilterPaneHeight = FilterGridExpanded;
        private LogView _logView;
        private LogViewModel _logViewModel;
        private string _pattern = string.Empty;
        private bool _recursive;

        private bool _showFilterPanel;

        private bool _showLogPanel;
        public MainWindowViewModel(MainWindow mainWindow)
        {
            AppTitle = App.TitleWithVersion;
            Logger.Information("Application started");

            _mainWindow = mainWindow;
            _logView = _mainWindow.LogView;

            InitCommand = ReactiveCommand.Create(InitExecute);
            OpenFileCommand = ReactiveCommand.Create(OpenFileExecute);
            OpenSetCommand = ReactiveCommand.Create(OpenSetExecute);
            ConfigureSetCommand = ReactiveCommand.Create<(string, string, bool?)>(ConfigureSetExecute);
            ToggleFilterPaneCommand = ReactiveCommand.Create(ToggleFilterPaneExecute);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleThemeExecute);
            ExitCommand = ReactiveCommand.Create(ExitApplicationExecute);
            AboutCommand = ReactiveCommand.Create(AboutExecute);
            
            // Hot-keys
            KeyGotoStartCommand = ReactiveCommand.Create(KeyGotoStartExecute);
            KeyGotoEndCommand = ReactiveCommand.Create(KeyGotoEndExecute);
            KeyFindCommand = ReactiveCommand.Create(KeyFindExecute);
            KeyFindPrevCommand = ReactiveCommand.Create(KeyFindPrevExecute);
            KeyFindNextCommand = ReactiveCommand.Create(KeyFindNextExecute);
            KeyGoPageUpCommand = ReactiveCommand.Create(KeyGoPageUpExecute);
            KeyGoPageDownCommand = ReactiveCommand.Create(KeyGoPageDownExecute);
            KeyGoUpCommand = ReactiveCommand.Create(KeyGoUpExecute);
            KeyGoDownCommand = ReactiveCommand.Create(KeyGoDownExecute);
        }

        public async void Init()
        {
            // Reactive trigger for when the basedir, filters and recursive has changed
            _logViewSubscription = this
                .WhenAnyValue(x => x.Folder, x => x.Pattern, x => x.Recursive, x => x.Layout)
                .Where(((string baseDir, string filter, bool recursive, Layout layout) observerTuple) =>
                {
                    var (baseDir, filter, _, _) = observerTuple;
                    return !string.IsNullOrWhiteSpace(baseDir) && !string.IsNullOrWhiteSpace(filter);
                })
                .Throttle(LogArgsChangeThreshold)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .Subscribe(SetupLogView);

            await InitCommand.Execute();
        }


        ~MainWindowViewModel()
        {
            _logViewSubscription?.Dispose();
            //_listBoxScrollViewerControl.ScrollChanged -= RefreshLineContentForVisibleChildren;
        }

        public string AppTitle
        {
            get => _appTitle;
            set => this.RaiseAndSetIfChanged(ref _appTitle, value);
        }

        public ReactiveCommand<Unit, Unit> InitCommand { get; set; }

        public ReactiveCommand<(string, string, bool?), Unit> ConfigureSetCommand { get; }

        public ReactiveCommand<Unit, Unit> ExitCommand { get; }
        public ReactiveCommand<Unit, Unit> AboutCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyGotoStartCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyGotoEndCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyFindCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyFindPrevCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyFindNextCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyGoPageUpCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyGoPageDownCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyGoUpCommand { get; }
        public ReactiveCommand<Unit, Unit> KeyGoDownCommand { get; }

        public GridLength FilterPaneHeight
        {
            get => _filterPaneHeight;
            set => this.RaiseAndSetIfChanged(ref _filterPaneHeight, value);
        }

        public FilterViewModel FilterViewModel
        {
            get => _filterViewModel;
            set => this.RaiseAndSetIfChanged(ref _filterViewModel, value);
        }

        public string Folder
        {
            get => _folder;
            set => this.RaiseAndSetIfChanged(ref _folder, value);
        }
        public Layout Layout
        {
            get => _layout;
            set => this.RaiseAndSetIfChanged(ref _layout, value);
        }

        public string InfoBarByteSize => _infoBarByteSize?.Value;
        public string InfoBarByteSizeFilter => _infoBarByteSizeFilter?.Value;

        public string InfoBarLineCount => _infoBarLineCount?.Value;
        public string InfoBarLineCountFilter => _infoBarLineCountFilter?.Value;

        public string InfoBarSeverity0
        {
            get => _infoBarSeverity0;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity0, value);
        }

        public string InfoBarSeverity0Filter
        {
            get => _infoBarSeverity0Filter;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity0Filter, value);
        }

        public string InfoBarSeverity1
        {
            get => _infoBarSeverity1;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity1, value);
        }

        public string InfoBarSeverity1Filter
        {
            get => _infoBarSeverity1Filter;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity1Filter, value);
        }

        public string InfoBarSeverity2
        {
            get => _infoBarSeverity2;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity2, value);
        }

        public string InfoBarSeverity2Filter
        {
            get => _infoBarSeverity2Filter;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity2Filter, value);
        }

        public string InfoBarSeverity3
        {
            get => _infoBarSeverity3;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity3, value);
        }

        public string InfoBarSeverity3Filter
        {
            get => _infoBarSeverity3Filter;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity3Filter, value);
        }

        public string InfoBarSeverity4
        {
            get => _infoBarSeverity4;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity4, value);
        }

        public string InfoBarSeverity4Filter
        {
            get => _infoBarSeverity4Filter;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity4Filter, value);
        }

        public string InfoBarSeverity5
        {
            get => _infoBarSeverity5;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity5, value);
        }

        public string InfoBarSeverity5Filter
        {
            get => _infoBarSeverity5Filter;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity5Filter, value);
        }

        public string InfoBarSeverity6
        {
            get => _infoBarSeverity6;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity6, value);
        }

        public string InfoBarSeverity6Filter
        {
            get => _infoBarSeverity6Filter;
            set => this.RaiseAndSetIfChanged(ref _infoBarSeverity6Filter, value);
        }

        public string InfoBarRange => _infoBarRange?.Value;
        public string InfoBarRangeFilter => _infoBarRangeFilter?.Value;

        public string InfoBarRangeToolTip => _infoBarRangeToolTip?.Value;
        public string InfoBarRangeToolTipFilter => _infoBarRangeToolTipFilter?.Value;

        public string InfoBarScope
        {
            get => _infoBarScope;
            set => this.RaiseAndSetIfChanged(ref _infoBarScope, value);
        }

        public string InfoBarScopeFilter
        {
            get => _infoBarScopeFilter;
            set => this.RaiseAndSetIfChanged(ref _infoBarScopeFilter, value);
        }

        public LogViewModel LogViewModel
        {
            get => _logViewModel;
            set => this.RaiseAndSetIfChanged(ref _logViewModel, value);
        }

        public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }

        public ReactiveCommand<Unit, Unit> OpenSetCommand { get; }

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

        public ReactiveCommand<Unit, Unit> ToggleFilterPaneCommand { get; }

        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }

        private async void ConfigureSetExecute((string folder, string pattern, bool? recursive) args)
        {
            var (folder, pattern, recursive) = args;
            folder ??= Folder;
            pattern ??= Pattern;
            recursive ??= Recursive;
            var configureSetView = new ConfigureSetView();
            configureSetView.DataContext = new ConfigureSetViewModel(configureSetView, folder, pattern, recursive.Value, Layout);
            var layout = await configureSetView?.ShowDialog<Layout>(_mainWindow);
            if (layout != null)
            {
                Logger.Information("Layout set: TimestampStart={TimestampStart} TimestampLength={TimestampLength} TimestampLeFormat='{TimestampFormat} SeverityStart={SeverityStart} Severities={Severities}", layout.TimestampStart, layout.TimestampLength, layout.TimestampFormat, layout.SeverityStart, string.Join(", ", layout.Severities.Select(s => $"{s.Key}:{s.Value}")));
                Logger.Information("Opening Folder='{Folder}' Pattern='{Pattern}' Recursive={Recursive}", folder, pattern, recursive.Value);
                Layout = layout;
                Folder = folder;
                Pattern = pattern;
                Recursive = recursive.Value;
            } 
            else
            {
                Logger.Information("Open/configure was cancelled");
                if (string.IsNullOrWhiteSpace(Folder) || string.IsNullOrWhiteSpace(Pattern))
                {
                    _mainWindow.MenuConfigureLayout.IsEnabled = false;

                    var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow(
                        "No logfile or set selected",
                        "Use the Open File Or Open Set menus to open log-files."
                    );
                    await messageBoxStandardWindow.ShowDialog(_mainWindow);
                }
            }
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

        private void KeyGotoStartExecute()
        {
            _logView.LinesCtrl.ScrollIntoView(_logViewModel.VirtualLogFile.FilteredLines.First());
        }

        private void KeyGotoEndExecute()
        {
            _logView.LinesCtrl.ScrollIntoView(_logViewModel.VirtualLogFile.FilteredLines.Last());
        }

        private void KeyFindExecute()
        {
            _logView.SearchQueryCtrl.Focus();
            _logView.SearchQueryCtrl.SelectionStart = 0;
            _logView.SearchQueryCtrl.SelectionEnd = _logView.SearchQueryCtrl.Text?.Length ?? 0;
        }

        private void KeyFindNextExecute()
        {
            _logViewModel.BrowseSearchFrwd();
        }


        private void KeyFindPrevExecute()
        {
            _logViewModel.BrowseSearchBack();
        }

        private void KeyGoPageUpExecute()
        {
            Logger.Information("Go One Page Up ([PageUp]) is not implemented yet");
        }

        private void KeyGoPageDownExecute()
        {
            Logger.Information("Go One Page Down ([PageDown]) is not implemented yet");
        }

        private void KeyGoUpExecute()
        {
            if (_logViewModel.LineSelected == null)
            {
                Logger.Information("No line selected");
                return;
            }
            var current = _logViewModel.VirtualLogFile.FilteredLines.IndexOf(_logViewModel.LineSelected);
            if (current < 0)
            {
                Logger.Information("No line selected");
                return;
            }
            if (current < 1)
            {
                Logger.Information("Already at the top");
                return;
            }
            _logView.LinesCtrl.SelectedIndex = current - 1;
        }

        private void KeyGoDownExecute()
        {
            if (_logViewModel.LineSelected == null)
            {
                Logger.Information("No line selected");
                return;
            }
            var current =_logViewModel.VirtualLogFile.FilteredLines.IndexOf(_logViewModel.LineSelected);
            if (current < 0)
            {
                Logger.Information("No line selected");
                return;
            }
            if (current >= _logViewModel.VirtualLogFile.FilteredLines.Count-1)
            {
                Logger.Information("Already at the bottom");
                return;
            }
            _logView.LinesCtrl.SelectedIndex = current + 1;
        }

        private void ExitApplicationExecute()
        {
            Logger.Information("Application exited from menu");
            (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }

        private void AboutExecute()
        {
            var aboutView = new AboutView();
            aboutView.ShowDialog(_mainWindow);
        }

        private (string, string, bool) DecodeFileName(string fileName)
        {
            Logger.Information("Selected file {LogFile}", fileName);
            var fileInfo = new FileInfo(fileName);
            var dirInfo = Directory.GetParent(fileInfo.FullName);
            return (dirInfo.FullName, fileInfo.Name, false);
        }

        private async void OpenFileExecute()
        {
            Logger.Verbose("OpenLogFile clicked");
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select folder",
                AllowMultiple = false,
                InitialFileName = "*.log",
                Directory = Folder
            };

            var file = await openFileDialog.ShowAsync(_mainWindow);

            if (file?.Length <= 0)
            {
                Logger.Information("OpenFile was cancelled");
                return;
            }

            var fileName = file?.First();
            await ConfigureSetCommand.Execute(DecodeFileName(fileName));
        }

        private async void OpenSetExecute()
        {

            var openLogSetView = new OpenSetView();
            openLogSetView.DataContext = new OpenSetViewModel(openLogSetView, _folder);
            var openSetResult = await openLogSetView.ShowDialog<OpenSetViewModel.OpenSetResult>(_mainWindow);
            if (openSetResult == null)
            {
                Logger.Information("OpenSet was cancelled");
                return;
            }

            // TODO: Check if the file/set opened already has a configuration in the settings.

            var configureArgs = openSetResult.SelectedFile != null
                ? DecodeFileName(openSetResult.SelectedFile.FullName)
                : (openSetResult.Folder, openSetResult.Pattern, openSetResult.Recursive);
            
            await ConfigureSetCommand.Execute(configureArgs);
        }

        private async void InitExecute()
        {
            // Parse args (this constructor should only be called once in the application lifespan
            var args = Environment.GetCommandLineArgs().Skip(1).ToList();
            if (args.Count > 0)
            {
                await ParseArgs(args);
            }
            else
            {
                var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow(
                    "No logfile passed as argument",
                    "Use the OpenLog Or OpenSet menus to open log-files."
                );
                await messageBoxStandardWindow.ShowDialog(_mainWindow);
            }
        }

        private async Task ParseArgs(List<string> args)
        {

            _mainWindow.AddHandler(DragDrop.DragOverEvent, DragOver);
            _mainWindow.AddHandler(DragDrop.DropEvent, Drop);

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

            var recursive = args.Contains("-r");

            args = args.Where(a => !new List<string> { "-r", "-d", "-w" }.Contains(a)).ToList();

            if (args.Count > 0)
            {
                Debugger.Launch();
                string folder;
                string pattern;
                if (File.Exists(args[0]))
                {
                    var dirInfo = Directory.GetParent(args[0]);
                    folder = dirInfo.FullName;
                    pattern = new FileInfo(args[0]).Name;
                }
                else
                {
                    folder = args[0];
                    pattern = args.Count > 1 ? args[1] : "*.log";
                }
                await ConfigureSetCommand.Execute((folder, pattern, recursive));
            }
        }

        private void SetFilterViewDataContext()
        {
            FilterViewModel = new FilterViewModel();
        }

        private void SetupLogView((string baseDir, string filter, bool recursive, Layout layout) observerTuple)
        {
            var (baseDir, filter, recursive, layout) = observerTuple;
            Logger.Debug("Scope changed, setting up the LogView DataContext");
            ShowLogPanel = true;
            LogViewModel = new LogViewModel(baseDir, filter, recursive, layout, _logView);

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
                                return $"Selected: [Line {lineSelected.LineNumber:n0}] [Pos {lineSelected.Position:n0}] [File {fileInfo.Name}] [Path {fileInfo.DirectoryName}] [Severity {lineSelected.Severity}]";
                            })
                            .ObserveOn(RxApp.MainThreadScheduler)
                            .ToProperty(this, x => x.InfoBarSelectedLine);
            */

            // TODO: Add binding HasFilter

            _infoBarRange = LogViewModel.VirtualLogFile.WhenAnyValue(x => x.Range)
                .Select(x =>
                {
                    var (startDate, endDate) = x;
                    return $"Range: {startDate:yyyy MMMM dd} - {endDate:yyyy MMMM dd}";
                })
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.InfoBarRange);

            _infoBarRangeToolTip = LogViewModel.VirtualLogFile.WhenAnyValue(x => x.Range)
                .Select(x =>
                {
                    var (startDate, endDate) = x;
                    return $"{startDate} - {endDate}";
                })
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.InfoBarRangeToolTip);

            // Setup subscription for showing the total size in the InfoBar
            // TODO: Implement filters in VirtualLogFile, and show both the filtered and total size, i.e. 'Size: 4kb/21Mb'
            _infoBarByteSize = LogViewModel.VirtualLogFile.WhenAnyValue(x => x.TotalSize)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Do(size => _mainWindow.MenuConfigureLayout.IsEnabled = size > 0)
                .Select(x => $"Size: {ByteSize.FromBytes(x)}")
                .ToProperty(this, x => x.InfoBarByteSize);

            // TODO: Add info on oldest and newest date in the log-file

            // TODO: Implement filters in VirtualLogFile, and show both the filtered and total no of lines, i.e. 'Lines: 12/931'
            _infoBarLineCount = LogViewModel.VirtualLogFile.WhenAnyValue(x => x.FilteredLines.Count)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Select(x => $"Lines: {x:N0}")
                .ToProperty(this, x => x.InfoBarLineCount);

            // TODO: Implement filters in VirtualLogFile, and show both the filtered and total count, i.e. 'Trace: 4/19'
            LogViewModel.VirtualLogFile
                .WhenAnyValue(x => x.SeverityStats)
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
                        var severityStats = LogViewModel.VirtualLogFile.SeverityStats;
                        if (severityStats == null || !severityStats.Any()) return;

                        foreach (var (level, count) in severityStats)
                        {
                            if (level == 0)
                            {
                                InfoBarSeverity0 = $"No severity: {count}";
                            }
                            else if (level == 1)
                            {
                                InfoBarSeverity1 = $"{_logViewModel.Severity1Name}: {count}";
                            }
                            else if (level == 2)
                            {
                                InfoBarSeverity2 = $"{_logViewModel.Severity2Name}: {count}";
                            }
                            else if (level == 3)
                            {
                                InfoBarSeverity3 = $"{_logViewModel.Severity3Name}: {count}";
                            }
                            else if (level == 4)
                            {
                                InfoBarSeverity4 = $"{_logViewModel.Severity4Name}: {count}";
                            }
                            else if (level == 5)
                            {
                                InfoBarSeverity5 = $"{_logViewModel.Severity5Name}: {count}";
                            }
                            else if (level == 6)
                            {
                                InfoBarSeverity6 = $"{_logViewModel.Severity6Name}: {count}";
                            }
                        }
                    });
                });
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

        private void ToggleThemeExecute()
        {
            // NB! Depends on the first Window.Styles StyleInclude to be the theme, and the third App.Styles to be the theme
            if (_mainWindow.ThemeControl.IsChecked == null || !_mainWindow.ThemeControl.IsChecked.Value)
            {
                _mainWindow.ThemeControl.IsChecked = true;
                _mainWindow.Styles[0] = Application.Current.Styles[2] = _darkTheme;
            }
            else
            {
                _mainWindow.ThemeControl.IsChecked = false;
                _mainWindow.Styles[0] = Application.Current.Styles[2] = _lightTheme;
            }

            Logger.Verbose("Switched theme to '{Theme}'",
                _mainWindow.ThemeControl.IsChecked != null && _mainWindow.ThemeControl.IsChecked.Value
                    ? "Dark"
                    : "Light");
        }
    }
}
