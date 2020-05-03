using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LogExpress.Views;

namespace LogExpress
{
    public class App : Application
    {
        public static Window MainWindow;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            base.OnFrameworkInitializationCompleted();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                MainWindow = desktop.MainWindow = new MainWindow();
            }

        }

        public static string DataFolder
        {
            get
            {
                var userPath = Environment.GetEnvironmentVariable(
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "LOCALAPPDATA" : "Home");

                var assembly = Assembly.GetEntryAssembly();
                var companyObj = assembly?.GetCustomAttributes<AssemblyCompanyAttribute>().FirstOrDefault();
                var prodObj = assembly?.GetCustomAttributes<AssemblyProductAttribute>().FirstOrDefault();
                return Path.Combine(userPath, companyObj?.Company, prodObj?.Product);
            }
        }

        public static string TitleWithVersion
        {
            get
            {
                var assembly = Assembly.GetEntryAssembly();
                var prodObj = assembly?.GetCustomAttributes<AssemblyProductAttribute>().FirstOrDefault();
                var versionObj = assembly?.GetCustomAttributes<AssemblyInformationalVersionAttribute>().FirstOrDefault();
                return $"{prodObj.Product}-{versionObj.InformationalVersion}";
            }
        }

    }
}