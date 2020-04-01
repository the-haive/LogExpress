using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using ByteSizeLib;
using Serilog;

namespace Flexy
{
    public class Tail
    {
        public static Dictionary<string, TailFile> TailFiles { get; set; } = new Dictionary<string, TailFile>();

        private static int Main(string[] args)
        {
            const string outputTemplate =
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff}|{Level:u3}|{FilePath}> {Message:lj}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: outputTemplate)
                .CreateLogger().ForContext("FilePath", "CORE");

            // FOLLOW
            var followOption = new Option(new[] {"--follow", "-f"},
                "This option will cause tail will loop forever, checking for new data at the end of the file(s). When new data appears, it will be printed. " +
                "If you follow more than one file, a header will be printed to indicate which file's data is being printed. " +
                "If a file shrinks instead of grows, tail will let you know with a message. " +
                "If a file doesn't exist then it will start operating on it when/if it is created. "
            );

            // NUM
            var numOption = new Option(new[] {"--num", "-n"}, "Specify the number of lines to show. Default is 10. " +
                                                              "If the --bytes option is used then this number of bytes will be shown. " +
                                                              "If the --position option is used then the number represents the 'position' " +
                                                              "in the file to start showing lines or bytes from instead. ")
            {
                Argument = new Argument<long>(arg =>
                    {
                        // If not specified, set default value
                        if (arg.Tokens.Count == 0) return 10;

                        long num;
                        if (ByteSize.TryParse(arg.Tokens[0].Value, out var size) && (num = Convert.ToInt64(size.Bytes)) >= 0) return num;
                        if (long.TryParse(arg.Tokens[0].Value, out num) && num >= 0) return num;

                        arg.ErrorMessage =
                            "Failed to parse the --num option. " +
                            "Please use either a number without a unit, " +
                            "or units as described here: https://en.wikipedia.org/wiki/Binary_prefix";
                        
                        return -1;

                    }, true)
                    {Arity = ArgumentArity.ExactlyOne}
            };

            // POSITION
            var positionOption = new Option(new[] {"--position", "-p"},
                "Change the value from the --num option to be the position in the file " +
                "instead of the length from the end. All file content from that position will be output. ");
            // BYTES
            var bytesOption = new Option(new[] {"--bytes", "-b"},
                "Change the output to display the last n bytes of the file(s), instead of lines. ");

            // VERBOSITY
            var verbosityOption = new Option(new[] {"--verbosity", "-v"},
                "This option allows you to define which internal (log)messages to show in the console. " +
                "The argument supplied defines the minimum level of messages to show. Values, in increasing order: None/0 Verbose/1 Debug/2 Information/3 Warning/4 Error/5 Fatal/6. " +
                "Note that you can use either the number or the name for the specific log-level to set. " +
                "Default: Information/3 (which means that Information, Warning, Error and Fatal messages are shown."
            );
            var verbosityArgument = new Argument<int>(arg =>
                {
                    // If not specified, set default value
                    if (arg.Tokens.Count == 0) return 3;

                    var input = arg.Tokens[0].Value;
                    if (!int.TryParse(input, out var result))
                    {
                        if (input.ToUpperInvariant().Equals("NONE")) return 0;

                        switch (input.ToUpperInvariant())
                        {
                            case "VERBOSE":
                                result = 1;
                                break;

                            case "DEBUG":
                                result = 2;
                                break;

                            case "INFORMATION":
                                result = 3;
                                break;

                            case "WARNING":
                                result = 4;
                                break;

                            case "ERROR":
                                result = 5;
                                break;

                            case "FATAL":
                                result = 6;
                                break;

                            default:
                                arg.ErrorMessage =
                                    $"Bad verbosity level ({input}). Please use --help to see more details on the allowed --verbosity levels. ";
                                //return null;
                                break;
                        }
                    }

                    if (result < 0 || result > 6)
                        arg.ErrorMessage =
                            $"Bad verbosity level ({result}). Please use --help to see more details on the allowed --verbosity levels. ";
                    return result;
                }, true)
                {Arity = ArgumentArity.ExactlyOne};
            verbosityOption.Argument = verbosityArgument;

            // QUIET
            var quietOption = new Option(new[] {"--quiet", "-q"},
                "This option allows you to suppress rendering of any log-messages or file-headers during operation. " +
                "Good for when the output should contain file-info only. "
            );

            // SLEEP
            var sleepOption = new Option(new[] {"--sleep", "-s"},
                "This option allows you to define how often a file is checked for changes. " +
                "Typically used to slow down the checking of file events or file accessibility, either to " +
                "reduce system stress or when the real-time aspect is not that important. " +
                "By default the delay is 500ms. Note that the argument value is in *seconds* and the smallest allowed value is 1 " +
                "(meaning that you can only use this option to slow down the event-handling). "
            );
            var sleepArgument = new Argument<int>(arg =>
                {
                    // If not specified, set default value
                    if (arg.Tokens.Count == 0) return 500;

                    if (!int.TryParse(arg.Tokens[0].Value, out var result))
                    {
                        arg.ErrorMessage = "Unable to parse --sleep argument. Must be a positive integer.";
                        return 0;
                    }

                    // Convert to milliseconds
                    return result * 1000;
                }, true)
                {Arity = ArgumentArity.ExactlyOne};
            sleepOption.Argument = sleepArgument;

            // CHUNK_SIZE
            var chunkSizeOption = new Option(new[] {"--chunk-size", "-c"},
                "This option will allow you to set the size of chunks read from the file (in order to produce bytes or lines). " +
                "A multiplier suffix can be used after num to specify units (https://en.wikipedia.org/wiki/Binary_prefix)."
            );
            var chunkSizeArgument = new Argument<int>(arg =>
                {
                    // If not specified, set default value
                    if (arg.Tokens.Count == 0)
                        return 4096;

                    if (ByteSize.TryParse(arg.Tokens[0].Value, out var size)) return Convert.ToInt32(size.Bytes);

                    arg.ErrorMessage =
                        "Failed to parse the --chunk-size option. Please use units as described here: https://en.wikipedia.org/wiki/Binary_prefix";
                    return -1;
                }, true)
                {Arity = ArgumentArity.ExactlyOne};
            chunkSizeOption.Argument = chunkSizeArgument;

            // FILES
            var filesArgument = new Argument<List<string>>("files"
/*                , parse: arg =>
                {
                    return arg.Tokens.Select(t => t.Value).Distinct().ToArray();
                }
*/                )
            {
                Arity = ArgumentArity.OneOrMore,
                Description =
                    "The files to operate on. " +
                    "If more than one file is specified then each output-line is prepended with a header, " +
                    "showing enough of the characters in the filename to distinguish them from each other. "
            };

            // ROOT
            var rootCommand = new Command("tail", "Show the last n lines from the given file(s). " +
                                                  "Via options you can show last bytes instead of lines, specify " +
                                                  "number of lines/bytes to show or position to start from, ++. ");
            rootCommand.AddOption(followOption);
            rootCommand.AddOption(numOption);
            rootCommand.AddOption(positionOption);
            rootCommand.AddOption(bytesOption);
            rootCommand.AddOption(verbosityOption);
            rootCommand.AddOption(quietOption);
            rootCommand.AddOption(sleepOption);
            rootCommand.AddOption(chunkSizeOption);
            rootCommand.AddArgument(filesArgument);

            rootCommand.Handler = CommandHandler.Create<TailOptions>(o =>
            {
                PrintTodo(o);

                PrintOptions(o);

                var lastUniqueIdx = -1;
                if (o.Files.Count > 1)
                {
                    var shortestIdx = o.Files.Min(s => s.Length);
                    for (var i = 1; i <= shortestIdx; i++)
                    {
                        var i1 = i;
                        var results = o.Files.Select(f => f[^i1]).Distinct();
                        if (results.Count() > 1)
                        {
                            // The file-paths differ at this character
                            lastUniqueIdx = -i;
                            break;
                        }
                    }
                }

                var displayNameLength = Math.Max(lastUniqueIdx, 10);

                foreach (var file in o.Files)
                {
                    var displayName = file.PadLeft(displayNameLength).Substring(file.Length - displayNameLength);
                    var tailFileOptions = new TailFileOptions(o, file, displayName);
                    TailFiles.Add(file, new TailFile(tailFileOptions));
                }

                foreach (var tailFile in TailFiles) tailFile.Value.Run();
            });

            // Parse the incoming args and invoke the handler
            return rootCommand.InvokeAsync(args).Result;
        }

        private static void PrintOptions(TailOptions o)
        {
            Log.Logger.Information("TailOptions:" + Environment.NewLine +
                                   $"\t--follow: {o.Follow}" + Environment.NewLine +
                                   $"\t--num: {o.Num}" + Environment.NewLine +
                                   $"\t--position: {o.Position}" + Environment.NewLine +
                                   $"\t--bytes: {o.Bytes}" + Environment.NewLine +
                                   $"\t--verbosity: {o.Verbosity}" + Environment.NewLine +
                                   $"\t--quiet: {o.Quiet}" + Environment.NewLine +
                                   $"\t--sleep: {o.Sleep}" + Environment.NewLine +
                                   $"\t--chunk-size: {o.ChunkSize}" + Environment.NewLine +
                                   $"\t--files: '{string.Join("', '", o.Files)}'");
        }

        private static void PrintTodo(TailOptions o)
        {
            Log.Logger.Information("TODO: " + Environment.NewLine +
                                   "\t* Setup Serilog, set verbosity" + Environment.NewLine +
                                   "\t* Instantiate LogReader class, set chunk-size" + Environment.NewLine +
                                   "\t* Implement tail num lines from end, one-file" + Environment.NewLine +
                                   "\t* Implement tail num bytes from end, one-file" + Environment.NewLine +
                                   "\t* Implement tail all lines from position, one-file" + Environment.NewLine +
                                   "\t* Implement tail all bytes from position, one-file" + Environment.NewLine +
                                   "\t* Implement option follow" + Environment.NewLine +
                                   "\t* Implement detect file moved/truncated. Reopen the expected file (as in not the archived rollover file)" +
                                   Environment.NewLine +
                                   "\t* Implement use of headers (multi-file)" + Environment.NewLine +
                                   "\t* Implement use of quiet" + Environment.NewLine +
                                   "\t* Implement tail num lines from end, many-files" + Environment.NewLine +
                                   "\t* Implement tail num bytes from end, many-files" + Environment.NewLine +
                                   "\t* Implement tail all lines from position, many-files" + Environment.NewLine +
                                   "\t* Implement tail all bytes from position, many-files" + Environment.NewLine +
                                   "\t* Implement use of sleep in LogReader");
        }
    }
}
