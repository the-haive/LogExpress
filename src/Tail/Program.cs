using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using ByteSizeLib;

namespace Flexy
{
    public class Program
    {
        private static int Main(string[] args)
        {
            // FOLLOW
            var followOption = new Option(new[] {"--follow", "-f"},
                "This option will cause tail will loop forever, checking for new data at the end of the file(s). When new data appears, it will be printed. " +
                "If you follow more than one file, a header will be printed to indicate which file's data is being printed. " +
                "If a file shrinks instead of grows, tail will let you know with a message. " +
                "If a file doesn't exist then it will start operating on it when/if it is created. "
            );

            var numOption = new Option(new[] {"--num", "-n"}, "Specify the number of lines to show. Default is 10. " +
                                                              "If the --bytes option is used then this number of bytes will be shown. " +
                                                              "If the --position option is used then the number represents the 'position' in the file to start showing from instead. ")
            {
                Argument = new Argument<long>(arg =>
                    {
                        // If not specified, set default value
                        if (arg.Tokens.Count == 0) return 10;

                        if (ByteSize.TryParse(arg.Tokens[0].Value, out var size)) return Convert.ToInt64(size.Bytes);

                        arg.ErrorMessage =
                            "Failed to parse the --num option. Please use units as described here: https://en.wikipedia.org/wiki/Binary_prefix";

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
            var chunkSizeArgument = new Argument<long>(arg =>
                {
                    // If not specified, set default value
                    if (arg.Tokens.Count == 0)
                        return 4096;

                    if (ByteSize.TryParse(arg.Tokens[0].Value, out var size)) return Convert.ToInt64(size.Bytes);

                    arg.ErrorMessage =
                        "Failed to parse the --chunk-size option. Please use units as described here: https://en.wikipedia.org/wiki/Binary_prefix";
                    return -1;
                }, true)
                {Arity = ArgumentArity.ExactlyOne};
            chunkSizeOption.Argument = chunkSizeArgument;

            // FILES
            var filesArgument = new Argument<IReadOnlyList<FileSystemInfo>>("files")
            {
                Arity = ArgumentArity.OneOrMore,
                Description =
                    "The files to operate on. " +
                    "If more than one file is specified then each output-line is prepended with a header, " +
                    "showing enough of the characters in the filename to distinguish them from each other. "
            };

            // ROOT
            var rootCommand = new Command("tail", "Show the last n lines from the given file(s). " +
                                                  "Via options you can show bytes instead of lines, specify number of lines/bytes to show, ++. ");
            rootCommand.AddOption(followOption);
            rootCommand.AddOption(numOption);
            rootCommand.AddOption(positionOption);
            rootCommand.AddOption(bytesOption);
            rootCommand.AddOption(verbosityOption);
            rootCommand.AddOption(quietOption);
            rootCommand.AddOption(sleepOption);
            rootCommand.AddOption(chunkSizeOption);
            rootCommand.AddArgument(filesArgument);

            rootCommand.Handler = CommandHandler.Create<Options>(o =>
            {
                /*
                    Console.WriteLine($"The value for --follow is: {o.Follow}");
                    Console.WriteLine($"The value for --num: {o.Num}");
                    Console.WriteLine($"The value for --position: {o.Position}");
                    Console.WriteLine($"The value for --bytes: {o.Bytes}");
                    Console.WriteLine($"The value for --verbosity is: {o.Verbosity}");
                    Console.WriteLine($"The value for --quiet is: {o.Quiet}");
                    Console.WriteLine($"The value for --sleep is: {o.Sleep}");
                    Console.WriteLine($"The value for --chunk-size: {o.ChunkSize}");
                    Console.WriteLine($"The file argument contains {o.Files.Length} files: ");
                    foreach (var f in o.Files) Console.WriteLine($"\t{f.FullName}");
                */
                new Tail().Execute(o);
            });

            // Parse the incoming args and invoke the handler
            return rootCommand.InvokeAsync(args).Result;
        }
    }
}