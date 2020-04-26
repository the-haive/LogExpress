using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ByteSizeLib;
using LogExpress.Models;
using LogExpress.Services;
using LogExpress.Views;
using ReactiveUI;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = Avalonia.Controls.Image;

namespace LogExpress.ViewModels
{
    public class LogViewModel : ViewModelBase
    {
        public static readonly ILogger Logger = Log.ForContext<LogViewModel>();
        private readonly ObservableAsPropertyHelper<bool> _hasLogLevelMap;
        private readonly ObservableAsPropertyHelper<string> _humanTotalSize;
        private readonly ObservableAsPropertyHelper<bool> _isAnalyzed;
        private readonly ObservableAsPropertyHelper<long> _totalSize;

        public readonly VirtualLogFile VirtualLogFile;
        private string _basePath;
        private string _filter;
        public ObservableCollection<LineItem> _lines;
        private LineItem _lineSelected;
        private Image<Rgba32> _logLevelMap;
        private string _logLevelMapFile;
        private readonly ObservableAsPropertyHelper<string> _logListHeader;
        private readonly LogView _logView;
        private bool _tail;

        public LogViewModel(string basePath, string filter, in bool recursive, LogView logView)
        {
            BasePath = basePath;
            Filter = filter;
            Tail = true;
            _logView = logView;

            VirtualLogFile = new VirtualLogFile(BasePath, Filter, recursive);
            LogFiles = VirtualLogFile.LogFiles;
            /*
                        VirtualLogFile.Connect()
                            .ObserveOn(RxApp.MainThreadScheduler)
                            .Bind(out _lines)
                            .Subscribe();
            */

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
/*
            _hasLogLevelMap = VirtualLogFile.WhenAnyValue(x => x.LogLevelMapFiles)
                .Where(imgFileNames => imgFileNames.Any())
                .Select(x => true)
                .ToProperty(this, x => x.HasLogLevelMap);
*/
/*
            VirtualLogFile.WhenAnyValue(x => x.LogLevelMap)
                .Where(x => x != null)
                .Subscribe(bitmap =>
                {
                    //_logView.LogLevelMapImageControl.Source = bitmap;

                });
*/

            VirtualLogFile.WhenAnyValue(x => x.LogLevelMapFiles)
                .Where(imgFileNames =>
                {
                    if (imgFileNames != null && imgFileNames.Any()) return true;
                    return false;
                })
                .Subscribe(imgInfos =>
                {
                    var totalHeight = imgInfos.Sum(i => i.Value);
                    var mapContainer = _logView.LogLevelMaps;

                    mapContainer.ColumnDefinitions = new ColumnDefinitions
                        {new ColumnDefinition {Width = new GridLength(10, GridUnitType.Auto)}};

                    mapContainer.RowDefinitions = new RowDefinitions();
                    var rowNo = 0;
                    foreach (var imgInfo in imgInfos)
                    {
                        imgInfo.Deconstruct(out var fileName, out var height);
                        
                        // TODO: Pass in the height of the image, in order to set the wanted proportions
                        var rowHeight = height / (double) totalHeight;
                        mapContainer.RowDefinitions.Add(new RowDefinition
                            {Height = new GridLength(rowHeight, GridUnitType.Star)});

                        var imgCtrl = new Image()
                        {
                            Width = 10,
                            Stretch = Stretch.Fill,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            Name = fileName
                        };
                        var path = Path.Combine(Environment.CurrentDirectory, fileName);
                        var bitmap = new Bitmap(path);
                        imgCtrl.Source = bitmap;

                        Grid.SetColumn(imgCtrl,0);
                        Grid.SetRow(imgCtrl, rowNo);
                        mapContainer.Children.Add(imgCtrl);
                        rowNo++;
                    }
                });

            _logListHeader = this.WhenAnyValue(x => x.LogFiles.Count)
                .DistinctUntilChanged()
                .Select(x => $"{x} scoped log-files (click to toggle)")
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.LogListHeader);

            this.WhenAnyValue(x => x.IsAnalyzed).Where(x => x).Subscribe(_ => { Lines = VirtualLogFile.Lines; });
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

        public bool HasLogLevelMap => _hasLogLevelMap != null && _hasLogLevelMap.Value;
        public string HumanTotalSize => _humanTotalSize.Value;
        public bool IsAnalyzed => _isAnalyzed != null && _isAnalyzed.Value;

        public ObservableCollection<LineItem> Lines
        {
            get => _lines;
            set => this.RaiseAndSetIfChanged(ref _lines, value);
        }

        public LineItem LineSelected
        {
            get => _lineSelected;
            set => this.RaiseAndSetIfChanged(ref _lineSelected, value);
        }

        public ReadOnlyObservableCollection<ScopedFile> LogFiles { get; }

        public Image<Rgba32> LogLevelMap
        {
            get => _logLevelMap;
            set => this.RaiseAndSetIfChanged(ref _logLevelMap, value);
        }

        public string LogLevelMapFile
        {
            get => _logLevelMapFile;
            set => this.RaiseAndSetIfChanged(ref _logLevelMapFile, value);
        }

        public string LogListHeader => _logListHeader.Value;

        public bool Tail
        {
            get => _tail;
            set => this.RaiseAndSetIfChanged(ref _tail, value);
        }

        public long TotalSize => _totalSize.Value;

        ~LogViewModel()
        {
            _isAnalyzed?.Dispose();
            _totalSize?.Dispose();
            VirtualLogFile?.Dispose();
        }
    }
}
