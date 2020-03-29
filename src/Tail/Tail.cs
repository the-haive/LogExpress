using System;

namespace Flexy
{
    public class Tail
    {
        public void Execute(Options options)
        {
            Console.WriteLine("TODO:");
            Console.WriteLine("- Setup Serilog, set verbosity");
            Console.WriteLine("- Instantiate LogReader class, set chunk-size");
            Console.WriteLine("- Implement tail num lines from end, one-file");
            Console.WriteLine("- Implement tail num bytes from end, one-file");
            Console.WriteLine("- Implement tail all lines from position, one-file");
            Console.WriteLine("- Implement tail all bytes from position, one-file");
            Console.WriteLine("- Implement option follow");
            Console.WriteLine("- Implement detect file moved/truncated. Reopen the expected file (as in not the archived rollover file)");
            Console.WriteLine("- Implement use of headers (multi-file)");
            Console.WriteLine("- Implement use of quiet");
            Console.WriteLine("- Implement tail num lines from end, many-files");
            Console.WriteLine("- Implement tail num bytes from end, many-files");
            Console.WriteLine("- Implement tail all lines from position, many-files");
            Console.WriteLine("- Implement tail all bytes from position, many-files");
            Console.WriteLine("- Implement use of sleep in LogReader");
        }
    }
}