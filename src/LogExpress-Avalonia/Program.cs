using Avalonia;
using Avalonia.Logging;
using Avalonia.Logging.Serilog;
using Avalonia.ReactiveUI;
using Serilog;
using Serilog.Filters;

namespace LogExpress
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static void Main(string[] args)
        {
            SerilogLogger.Initialize(new LoggerConfiguration()
                .Filter.ByIncludingOnly(Matching.WithProperty("Area", LogArea.Layout))
                .MinimumLevel.Verbose()
                .WriteTo.Trace(outputTemplate: "{Area}: {Message}")
                .CreateLogger());
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToDebug()
                .UseReactiveUI();
    }
}
