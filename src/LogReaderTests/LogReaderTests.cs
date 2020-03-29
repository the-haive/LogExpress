using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Serilog;
using Serilog.Enrichers;
using Flexy;
using Polly;

namespace LogReaderTests
{
    [TestFixture()]
    public class LogReaderTests
    {
        private string _testFilePath;

        private static readonly Policy WaitAndRetry = Policy
            .Handle<System.IO.IOException>()
            .WaitAndRetry(new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(3)
            });


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}|{SourceContext}({FilePath})|{Level:u3}|{ThreadId}:{ThreadName}|{Message:lj}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                //.Enrich.WithProperty(ThreadNameEnricher.ThreadNamePropertyName, "Default")
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: outputTemplate)
                .WriteTo.File(
                    @"D:\src\FLEXY\LogExpress\src\LogReaderTests\LogReaderTests.log", 
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: outputTemplate
                    )
                .CreateLogger();
        }

        [TestCase(100, 0, 3, "Line 1", "Line 3", 3)]
        [TestCase(100, 0, 10, "Line 1", "Line 10", 10)]
        [TestCase(100, 0, 200, "Line 1", "Line 100", 100)]

        [TestCase(100, 1, 3, "Line 3", "Line 5", 3)]
        [TestCase(100, 1, 10, "Line 3", "Line 12", 10)]
        [TestCase(100, 1, 200, "Line 1", "Line 100", 100)]
        
        [TestCase(100, 46, 3, "Line 48", "Line 50", 3)]
        [TestCase(100, 46, 10, "Line 48", "Line 57", 10)]
        [TestCase(100, 46, 200, "Line 1", "Line 100", 100)]
        
        [TestCase(100, 88, 10, "Line 90", "Line 99", 10)]
        [TestCase(100, 89, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 90, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 91, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 92, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 93, 10, "Line 91", "Line 100", 10)]
        
        [TestCase(100, 94, 3, "Line 96", "Line 98", 3)]
        [TestCase(100, 94, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 94, 200, "Line 1", "Line 100", 100)]
        
        [TestCase(100, 95, 3, "Line 97", "Line 99", 3)]
        [TestCase(100, 95, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 95, 200, "Line 1", "Line 100", 100)]
        
        [TestCase(100, 96, 3, "Line 98", "Line 100", 3)]
        [TestCase(100, 96, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 96, 200, "Line 1", "Line 100", 100)]
        
        [TestCase(100, 97, 3, "Line 98", "Line 100", 3)]
        [TestCase(100, 97, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 97, 200, "Line 1", "Line 100", 100)]
        
        [TestCase(100, 98, 3, "Line 98", "Line 100", 3)]
        [TestCase(100, 98, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 98, 200, "Line 1", "Line 100", 100)]
        
        [TestCase(100, 99, 3, "Line 98", "Line 100", 3)]
        [TestCase(100, 99, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 99, 200, "Line 1", "Line 100", 100)]
        
        [TestCase(100, 100, 3, "Line 98", "Line 100", 3)]
        [TestCase(100, 100, 10, "Line 91", "Line 100", 10)]
        [TestCase(100, 100, 200, "Line 1", "Line 100", 100)]
        [TestCase(100_000_000, 100, 200, "Line 99999801", "Line 100000000", 200)]
        public void Read_From_Offset_10_Lines_From_FilePath_Test(long numLinesToGenerate, int fromPosPct, long numLines, string firstLine, string lastLine, long count)
        {
            // Create a log-file with dummy content
            var tempFolder = Path.GetTempPath();
            var fileName = Path.GetRandomFileName();
            _testFilePath = Path.Combine(tempFolder, fileName);
            try
            {
                using (var sw = File.CreateText(_testFilePath))
                {
                    for (var i = 1; i < numLinesToGenerate + 1; i++)
                    {
                        sw.WriteLine($"Line {i}");
                    }
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                var logReader = new LogReader(_testFilePath);
                var lines = logReader.LoadSegment(fromPosPct, numLines);
                stopwatch.Stop();
                Log.Logger.Information("Time to find lines: {Duration}", stopwatch.Elapsed);
                Assert.That(lines.Count, Is.EqualTo(count));
                if (count == 0) return;
                Assert.That(lines.First.Value.Content, Is.EqualTo(firstLine));
                Assert.That(lines.Last.Value.Content, Is.EqualTo(lastLine));
            }
            catch (Exception ex)
            {
                Assert.Fail("Should not throw exception. Exception:" + ex);
            }
            finally
            {
                File.Delete(_testFilePath);
            }
        }

    }
}