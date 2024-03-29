﻿using System;
using System.IO;
using Avalonia;
using Avalonia.Dialogs;
using Avalonia.Logging.Serilog;
using Avalonia.ReactiveUI;
using LogExpress.Views;
using Serilog;

namespace LogExpress
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff}|{Level:u3}|{ThreadId}:{SourceContext}({FilePath})|{Message:lj}{NewLine}{Exception}";
            //SerilogLogger.Initialize(new LoggerConfiguration()
            Log.Logger = new LoggerConfiguration()
                //.Filter.ByIncludingOnly(Matching.WithProperty("Area", LogArea.Layout))
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .MinimumLevel.Verbose()
                //.WriteTo.Trace(outputTemplate: "{Area}: {Message}")
                //.WriteTo.Console(outputTemplate: outputTemplate)
                .WriteTo.File(
                    Path.Combine(App.DataFolder, "log",  @"LogExpress.log"), 
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: outputTemplate
                )
                .CreateLogger();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder
                .Configure<App>()
                .UsePlatformDetect()
                //.With(new X11PlatformOptions {EnableMultiTouch = false, UseDBusMenu = true})
                //.With(new Win32PlatformOptions {EnableMultitouch = false, AllowEglInitialization = true})
                //.UseSkia()                
                .LogToDebug()
                .UseReactiveUI()
                //.UseManagedSystemDialogs()
                ;
    }
}
