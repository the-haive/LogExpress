using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Serilog;
using static Flexy.Tests.StringAlgorithms;

namespace Flexy.Tests
{
    [TestFixture()]
    public class ShortestUniqueTests
    {
        private string _testFilePath;


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff}|{Level:u3}|{ThreadId}:{SourceContext}({FilePath})|{Message:lj}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: outputTemplate)
                .WriteTo.File(
                    @"D:\src\FLEXY\LogExpress\src\LogFileReaderTests\LogFileReaderTests.log", 
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: outputTemplate
                )
                .CreateLogger();
        }

[Test]
public void Find_ShortestUnique_Start_And_Length_Differ_Test()
{
    var input = new[]
    {
        "path/to/a/file.txt",
        "another/path/to/a/file.txt"
    };
    var expected = new[]
    {
        "      …a/file.txt", 
        "…er/pa…a/file.txt"
    };
    AssertShortestUniqueResults(input, expected);
}

[Test]
public void Find_ShortestUnique_Start_Differ_Test()
{
    var input = new[]
    {
        "/c/src/path/to/a/file.txt",
        "/d/src/path/to/a/file.txt"
    };
    var expected = new[]
    {
        "…c…a/file.txt", 
        "…d…a/file.txt"
    };
    AssertShortestUniqueResults(input, expected);
}

[Test]
public void Find_ShortestUnique_Start_Differ_Without_Skip_Test()
{
    var input = new[]
    {
        "/c/src/path/to/a/file.txt",
        "/d/src/path/to/a/file.txt"
    };
    var expected = new[]
    {
        "…c…a/file.txt", 
        "…d…a/file.txt"
    };
    AssertShortestUniqueResults(input, expected);
}

[Test]
public void Find_ShortestUnique_End_Differ_Test()
{
    var input = new[]
    {
        "/c/src/path/to/a/file.txt",
        "/c/src/path/to/a/file.dat"
    };
    var expected = new[]
    {
        "…a/file.txt", 
        "…a/file.dat"
    };
    AssertShortestUniqueResults(input, expected);
}

[Test]
public void Find_ShortestUnique_Middle_Differ_Test()
{
    var input = new[]
    {
        "/c/src/path/to/a/file.txt",
        "/c/src/path/to/b/file.txt"
    };
    var expected = new[]
    {
        "…a/file.txt", 
        "…b/file.txt"
    };
    AssertShortestUniqueResults(input, expected);
}

[Test]
public void Find_ShortestUnique_Start_And_End_Differ_Test()
{
    var input = new[]
    {
        "/c/src/path/to/a/file.txt",
        "/d/src/path/to/a/file.dat"
    };

    var expected = new[]
    {
        "…a/file.txt", 
        "…a/file.dat"  
    };
    AssertShortestUniqueResults(input, expected);
}

[Test]
public void Find_ShortestUnique_Start_Middle_And_End_Differ_Test()
{
    var input = new[]
    {
        "/c/src/path/to/a/file.txt",
        "/d/src/path/to/b/file.dat"
    };
    var expected = new[]
    {
        "…a/file.txt",
        "…b/file.dat"
    };
    AssertShortestUniqueResults(input, expected);
}

private static void AssertShortestUniqueResults(IReadOnlyList<string> input, IReadOnlyList<string> expected)
{
    var (actual, success) = AbbreviatedUniquePath(input);
    Assert.That(success, Is.EqualTo(true));
    Assert.That(actual.Count, Is.EqualTo(expected.Count));
    for (var i = 0; i < expected.Count; i++) Assert.That(actual[i], Is.EqualTo(expected[i]));
}

    }
}