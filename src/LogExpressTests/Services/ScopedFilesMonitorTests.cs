using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using DynamicData;
using LogExpress.Models;
using LogExpress.Services;
using NUnit.Framework;
using Polly;
using Serilog;

namespace LogExpressTests.Services
{
    [TestFixture]
    public class ScopedFileMonitorTests
    {
        private static readonly Policy WaitAndRetry = Policy
            .Handle<IOException>()
            .WaitAndRetry(new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(3)
            });

        private string _tempFolder;

        private ReadOnlyObservableCollection<ScopedFile> _files;
        private ReadOnlyObservableCollection<ScopedFile> Files => _files;

        #region Helpers

        private static void CreateRandomFiles(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var rndFileName = Path.GetRandomFileName();
                if (rndFileName.StartsWith("test")) continue;
                File.WriteAllText(rndFileName, "Any data...");
            }
        }

        #endregion

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            const string outputTemplate =
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}|{SourceContext}({FilePath})|{Level:u3}|{ThreadId,4}|{Message:lj}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: outputTemplate)
                .CreateLogger();
        }

        [SetUp]
        public void Setup()
        {
            var userTempFolder = Path.GetTempPath();
            var tempFolderName = Path.GetRandomFileName();
            _tempFolder = Path.Combine(userTempFolder, tempFolderName);
        }

        [TearDown]
        public void TearDown()
        {
            WaitAndRetry.Execute(() =>
            {
                if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, true);
            });
            _files = null;
        }

        #region Found files

        [Test]
        public void Files_Found_Missing_ParentFolder_Then_Created()
        {
            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);

            Directory.CreateDirectory(_tempFolder);
         
            File.WriteAllText(Path.Combine(_tempFolder, "test.log"), "Log content");

            Thread.Sleep(300);

            using var binding = scopedFilesMonitor
                .Connect()
                .OnItemAdded(i => { Console.WriteLine($"Added {i}", i.Name);})
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files.Count, Is.EqualTo(1));
        }

        [Test]
        public void Files_Found_Missing_ParentFolder_Then_Created_With_Random_Files_Too()
        {
            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);

            Directory.CreateDirectory(_tempFolder);
            File.WriteAllText(Path.Combine(_tempFolder, "test.1.log"), "Log content");
            File.WriteAllText(Path.Combine(_tempFolder, "test.2.log"), "Log content");
            CreateRandomFiles(10);

            Thread.Sleep(300);

            using var binding = scopedFilesMonitor
                .Connect()
                .OnItemAdded(i => { Console.WriteLine($"Added {i}", i.Name);})
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files.Count, Is.EqualTo(2));
        }

        [Test]
        public void Files_Found_ParentFolder_Exist_Then_Add_1()
        {
            Directory.CreateDirectory(_tempFolder);
            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);

            File.WriteAllText(Path.Combine(_tempFolder, "test.log"), "Log content");

            Thread.Sleep(300);

            using var binding = scopedFilesMonitor
                .Connect()
                .OnItemAdded(i => { Console.WriteLine($"Added {i}", i.Name);})
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files.Count, Is.EqualTo(1));
        }

        [Test]
        public void Files_Found_ParentFolder_Exist_With_1_Then_Add_1()
        {
            Directory.CreateDirectory(_tempFolder);
            File.WriteAllText(Path.Combine(_tempFolder, "test.1.log"), "Log content");

            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);

            File.WriteAllText(Path.Combine(_tempFolder, "test.2.log"), "Log content");

            Thread.Sleep(300);

            using var binding = scopedFilesMonitor
                .Connect()
                .OnItemAdded(i => { Console.WriteLine($"Added {i}", i.Name);})
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files.Count, Is.EqualTo(2));
        }

        [Test]
        public void Files_Found_ParentFolder_Exist_With_2_Then_Remove_1()
        {
            Directory.CreateDirectory(_tempFolder);
            File.WriteAllText(Path.Combine(_tempFolder, "test.1.log"), "Log content");
            File.WriteAllText(Path.Combine(_tempFolder, "test.2.log"), "Log content");

            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);

            File.Delete(Path.Combine(_tempFolder, "test.1.log"));

            Thread.Sleep(300);

            using var binding = scopedFilesMonitor
                .Connect()
                .OnItemAdded(i => { Console.WriteLine($"Added {i}", i.Name);})
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files.Count, Is.EqualTo(1));
        }

        #endregion

        #region NoFiles

        [Test]
        public void No_Files_Missing_ParentFolder()
        {
            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);
            using var binding = scopedFilesMonitor
                .Connect()
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files, Is.Empty);
        }

        [Test]
        public void No_Files_Missing_ParentFolder_Then_Created()
        {
            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);

            Directory.CreateDirectory(_tempFolder);

            Thread.Sleep(300);
            
            using var binding = scopedFilesMonitor
                .Connect()
                .Bind(out _files)
                .Subscribe();
            
            Assert.That(Files, Is.Empty);
        }

        [Test]
        public void No_Files_ParentFolder_Exist_But_Empty()
        {
            Directory.CreateDirectory(_tempFolder);

            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);

            Thread.Sleep(300);
            
            using var binding = scopedFilesMonitor
                .Connect()
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files, Is.Empty);
        }

        [Test]
        public void No_Files_ParentFolder_Exist_Random_Files_Created_Afterwards()
        {
            Directory.CreateDirectory(_tempFolder);

            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);
            
            CreateRandomFiles(10);
            Thread.Sleep(300);

            using var binding = scopedFilesMonitor
                .Connect()
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files, Is.Empty);
        }

        [Test]
        public void No_Files_ParentFolder_Exist_With_Random_Files_()
        {
            Directory.CreateDirectory(_tempFolder);
            CreateRandomFiles(10);
            Thread.Sleep(300);

            using var scopedFilesMonitor = new ScopedFileMonitor(_tempFolder, new List<string> { "test*.log" }, true);

            using var binding = scopedFilesMonitor
                .Connect()
                .Bind(out _files)
                .Subscribe();

            Assert.That(Files, Is.Empty);
        }

        #endregion
    }
}