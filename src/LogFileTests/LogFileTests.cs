using NUnit.Framework;
using Flexy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Serilog;

namespace Flexy.Tests
{
    [TestFixture()]
    public class LogFileTests
    {
        private string _testFilePath;


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff}|{Level:u3}|{ThreadId}:{SourceContext}({FilePath})|{Message:lj}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                //.Enrich.WithProperty(ThreadNameEnricher.ThreadNamePropertyName, "Default")
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: outputTemplate)
                .WriteTo.File(
                    @"D:\src\FLEXY\LogExpress\src\LogFileTests\LogFileTests.log", 
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: outputTemplate
                )
                .CreateLogger();
        }

        [TestCase("", 500, 100, 0, "Line 1", "Line 3", 3)]
        [TestCase("", 500, 100, 0, "Line 1", "Line 10", 10)]
        [TestCase("", 500, 100, 0, "Line 1", "Line 100", 100)]

        [TestCase("", 500, 1000, 1, "Line 3", "Line 5", 3)]
        [TestCase("", 500, 100, 1, "Line 3", "Line 12", 10)]
        [TestCase("", 500, 100, 1, "Line 1", "Line 100", 100)]
        
        [TestCase("", 500, 100, 46, "Line 48", "Line 50", 3)]
        [TestCase("", 500, 100, 46, "Line 48", "Line 57", 10)]
        [TestCase("", 500, 100, 46, "Line 1", "Line 100", 100)]
        
        [TestCase("", 500, 100, 88, "Line 90", "Line 99", 10)]
        [TestCase("", 500, 100, 89, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 90, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 91, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 92, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 93, "Line 91", "Line 100", 10)]
        
        [TestCase("", 500, 100, 94, "Line 96", "Line 98", 3)]
        [TestCase("", 500, 100, 94, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 94, "Line 1", "Line 100", 100)]
        
        [TestCase("", 500, 100, 95, "Line 97", "Line 99", 3)]
        [TestCase("", 500, 100, 95, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 95, "Line 1", "Line 100", 100)]
        
        [TestCase("", 500, 100, 96, "Line 98", "Line 100", 3)]
        [TestCase("", 500, 100, 96, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 96, "Line 1", "Line 100", 100)]
        
        [TestCase("", 500, 100, 97, "Line 98", "Line 100", 3)]
        [TestCase("", 500, 100, 97, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 97, "Line 1", "Line 100", 100)]
        
        [TestCase("", 500, 100, 98, "Line 98", "Line 100", 3)]
        [TestCase("", 500, 100, 98, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 98, "Line 1", "Line 100", 100)]
        
        [TestCase("", 500, 100, 99, "Line 98", "Line 100", 3)]
        [TestCase("", 500, 100, 99, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 99, "Line 1", "Line 100", 100)]
        
        [TestCase("", 500, 100, 100, "Line 98", "Line 100", 3)]
        [TestCase("", 500, 100, 100, "Line 91", "Line 100", 10)]
        [TestCase("", 500, 100, 100, "Line 1", "Line 100", 100)]
        [TestCase("", 500, 100_000_000, 100, "Line 99999801", "Line 100000000", 200)]
        public void Read_From_Offset_10_Lines_From_FilePath_Test(string description, int blockSize, long numLinesToGenerate, int fromPosPct, string firstLine, string lastLine, long count)
        {
            // Create a log-file with dummy content
            var tempFolder = Path.GetTempPath();
            var fileName = Path.GetRandomFileName();
            _testFilePath = Path.Combine(tempFolder, fileName);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                using (var sw = File.CreateText(_testFilePath))
                {
                    for (var i = 1; i < numLinesToGenerate + 1; i++)
                    {
                        sw.WriteLine($"Line {i}");
                    }
                }
                stopwatch.Stop();
                var fileLength = new FileInfo(_testFilePath).Length;
                Log.Logger.Information("Time to create testFile with {NumLines} lines -> Size: {Size:n0} bytes> {Duration}", numLinesToGenerate, fileLength, stopwatch.Elapsed);

                stopwatch = Stopwatch.StartNew();
                var logFile = new LogFile(_testFilePath, blockSize: blockSize);
                var startPos = (long) (fromPosPct / 100.0 * fileLength);
                var lines = logFile.GetLines(startPos);
                stopwatch.Stop();

                Log.Logger.Information("Time to find lines> {Duration}", stopwatch.Elapsed);
                
                Assert.That(lines.Count, Is.EqualTo(count));
                if (count == 0) return;
                Assert.That(lines[0].Content, Is.EqualTo(firstLine));
                Assert.That(lines[lines.Count].Content, Is.EqualTo(lastLine));
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