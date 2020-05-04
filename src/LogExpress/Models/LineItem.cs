using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media;
using LogExpress.Services;
using Serilog;

namespace LogExpress.Models
{
    public static class LogColors {
        public static readonly Brush OrangeRedOpaque = new SolidColorBrush(Colors.OrangeRed, 0.5);
        public static readonly Brush SalmonOpaque = new SolidColorBrush(Colors.Salmon, 0.5);
        public static readonly Brush GoldOpaque = new SolidColorBrush(Colors.Gold, 0.5);
        public static readonly Brush BeigeOpaque = new SolidColorBrush(Colors.Beige, 0.5);
        public static readonly Brush WheatOpaque = new SolidColorBrush(Colors.Wheat, 0.5);
        public static readonly Brush TanOpaque = new SolidColorBrush(Colors.Tan, 0.5);
        public static readonly Brush FloralWhiteOpaque = new SolidColorBrush(Colors.FloralWhite, 0.5);
        public static readonly Brush Black = new SolidColorBrush(Colors.Black);
        public static readonly Brush Gray = new SolidColorBrush(Colors.Gray);
    }

    /// <summary>
    ///     When scanning the log-files we generate a LineItem for each line we find in all the scoped log-files.
    ///     The LineItem instances are kept in a reactive dynamic data collection.
    ///     It is important that the LineItems are as small as possible, as there could be a *lot* of these instances in that
    ///     list.
    ///     The dynamic data collection requires a (unique) Key, and we are combining the file's creation-time with the
    ///     line-number
    ///     to generate a unique key. But, the file CreationTime is a DateTime, which is a long in itself. The creation-time
    ///     has a
    ///     detail-level that is way higher than needed. For the VirtualLogFile we would only need a second of resolution. It
    ///     would
    ///     be very uncommon that log-files, within the same virtual scope, are created more often than every second. There are
    ///     some
    ///     scenarios that will be challenging, but all these require manually copying in files, or by setting a scope that
    ///     tries to
    ///     combine log-files that aren't really in the same scope. This will be dealt with in different ways, as it is more
    ///     important
    ///     to reduce the memory footprint of the application - for the normal real life actual use.
    ///     The uint Position and byte LogLevel are kept as separate variables.
    /// </summary>
    /// <remarks>
    ///     We only need 36 bits to store a DateTime with 1 second resolution. This leaves 28 bits for the line-number, while
    ///     preserving them in the same Key.
    /// 
    ///     We use 36 bits for the datetime and multiply with 10_000_000 (to get back to original resolution).
    ///     This yields a max-date of: `new DateTime((long) Math.Pow(2,36)-1 * 10_000_000) => [20.08.2178 07.32.15]`
    /// 
    ///     We then use the remaining 64-36 => 28 bits for the lineNumber.
    ///     Which yields max `Math.Pow(2,28)-1 => 268_435_455` lines per log-file
    /// 
    /// <![CDATA[
    ///         6         5         4         3         2         1
    ///     4321098765432109876543210987654321098765432109876543210987654321
    ///                                  = 68_719_476_735 => new DateTime(68_719_476_735 * 10_000_000) => [20.08.2178 07.32.15] maxDate
    ///                                         1111111111111111111111111111 = 268_435_455 lines
    /// ]]>
    /// 
    ///     The Position is a long in the various file APIs, but for our application an uint (32 bits) would probably suffice.
    ///     This means that the largest supported log-file is 4_294_967_295 (~4 GB). Log-files really should be smaller than
    ///     that, so this should not pose any problems.
    /// 
    ///     The LogLevel is normally 6 different levels. We create a byte for it, but it will use only 3 bits of those. This
    ///     allows us 5 bits for potential future purposes, without having to use more memory.
    ///
    ///     ----------------------------
    ///     Total entry size calculation
    ///     ----------------------------
    ///        64 bits (Key)
    ///     +  32 bits (Position)
    ///     +   8 bits (LogLevel)
    ///     ----------------------------
    ///     = 104 bits = 13 bytes
    ///     ============================
    /// </remarks>
    public class LineItem
    {
        public const int TicksPerSec = 10_000_000;
        public const byte LineNumberBitLength = 28;
        public const ulong LineNumberMaxAndMask = 0b1111111111111111111111111111;
        public const long CreationDateTimeTicksMax = 0b111111111111111111111111111111111111 * TicksPerSec;
        public static ReadOnlyObservableCollection<ScopedFile> LogFiles;
        private static readonly ILogger Logger = Log.ForContext<LineItem>();

        /// <summary>
        /// Creates a CreationTime long with reduced resolution, made to fit comparisons with the stored CreationTimeTicks property
        /// </summary>
        /// <param name="fileInfo">The files FileInfo to be used for creating the key</param>
        /// <returns>A long value that is reduced in resolution and bit left-shifted appropriately</returns>
        public static ulong GetCreationTimeTicks(ScopedFile fileInfo)
        {
            if (fileInfo != null) return (ulong) (fileInfo.CreationTime.Ticks / TicksPerSec);
            return 0;
        }

        /// <summary>
        ///     Constructs a new LineItem
        /// </summary>
        /// <param name="fileInfo">The file that the line was found in</param>
        /// <param name="lineNum">The lineNumber for this position</param>
        /// <param name="position">The actual position within the file (NB! Restricted to uint.MaxValue)</param>
        /// <param name="logLevel">The detected LogLevel, if any (0 = None)</param>
        public LineItem(ScopedFile fileInfo, int lineNum, uint position, byte logLevel)
        {
            Debug.Assert((ulong) lineNum <= LineNumberMaxAndMask);
            Debug.Assert(fileInfo.CreationTime.Ticks <= CreationDateTimeTicksMax);
            Key = (GetCreationTimeTicks(fileInfo) << LineNumberBitLength) + (ulong) lineNum;
            Position = position;
            LogLevel = logLevel;
        }


        /// <summary>
        ///     Gets the content for the current LineItem instance
        /// </summary>
        public string Content => ReadLineFromFilePosition(LogFile, Position);

        public ScopedFile LogFile => LogFiles.FirstOrDefault(logFile => logFile.LinesListCreationTime == CreationTimeTicks);

        /// <summary>
        ///     Gets the file creation-time from the LineItem instance
        /// </summary>
        public ulong CreationTimeTicks => Key >> LineNumberBitLength;

        /// <summary>
        ///     64 bits for the key:
        ///     36 leftmost bits for the date, with a max-resolution of 1 second
        ///     28 rightmost bits for the lineNumber
        ///     See the static GenerateKey() method for a helper on how to create the key to set here.
        /// </summary>
        public ulong Key { get; }

        /// <summary>
        ///     Gets the LineNumber from the LineItem instance
        /// </summary>
        public int LineNumber => (int) (Key & LineNumberMaxAndMask);

        /// <summary>
        ///     Log-levels. Only need 3 bits, but since we have no other data that we want to add then we just assign
        ///     this with ordinary log-level values.
        /// </summary>
        public byte LogLevel { get; }

        /// <summary>
        ///     Log-colors as given by the LogLevel
        /// 
        ///   Trace" foregroundColor="DarkGray"/>
        ///   Debug" foregroundColor="Gray"/>
        ///   Info" foregroundColor="White"/>
        ///   Warn" foregroundColor="Yellow"/>
        ///   Error" foregroundColor="Red"/>
        ///   Fatal" foregroundColor="Red" backgroundColor="White"/>
        /// </summary>
        public Brush LogFgColor {
            get
            {
                return LogLevel switch
                {
                    6 => LogColors.Black,
                    5 => LogColors.Black,
                    4 => LogColors.Black,
                    3 => LogColors.Black,
                    2 => LogColors.Black,
                    1 => LogColors.Black,
                    _ => LogColors.Gray
                };
            }
        }
        /// <summary>
        ///     Log-colors as given by the LogLevel
        /// 
        ///   Trace" foregroundColor="DarkGray"/>
        ///   Debug" foregroundColor="Gray"/>
        ///   Info" foregroundColor="White"/>
        ///   Warn" foregroundColor="Yellow"/>
        ///   Error" foregroundColor="Red"/>
        ///   Fatal" foregroundColor="Red" backgroundColor="White"/>
        /// </summary>
        public Brush LogBgColor {
            get
            {
                return LogLevel switch
                {
                    6 => LogColors.OrangeRedOpaque,
                    5 => LogColors.SalmonOpaque,
                    4 => LogColors.GoldOpaque,
                    3 => LogColors.BeigeOpaque,
                    2 => LogColors.WheatOpaque,
                    1 => LogColors.TanOpaque,
                    _ => LogColors.FloralWhiteOpaque
                };
            }
        }

        public bool ShowNewFileSeparator => LineNumber == 1;

        /// <summary>
        ///     File-positions in general are long values, but we want to enforce it to be max 4GB, by requiring
        ///     this to be an uint (32 bits)
        /// </summary>
        public uint Position { get; }

        /// <summary>
        /// Reads the date for the line from disk and parses it to a datetime, if a date was found
        /// </summary>
        public DateTime? Timestamp => ReadDateFromFilePosition(LogFile, Position);

        private static DateTime? ReadDateFromFilePosition(ScopedFile file, long position)
        {
            using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var reader = new StreamReader(fileStream);
            reader.BaseStream.Seek(position, SeekOrigin.Begin);
            
            // TODO: Make the date-length configurable
            var buffer = new Span<char>(new char[23]);

            var numRead = reader.Read(buffer);
            if (numRead == -1) return null;
            if (DateTime.TryParse(buffer, out var dateTime)) return dateTime;
            return null;
        }

        private static string ReadLineFromFilePosition(ScopedFile file, long position)
        {
            using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var reader = new StreamReader(fileStream);
            reader.BaseStream.Seek(position, SeekOrigin.Begin);
            reader.DiscardBufferedData();
            var buffer = new Span<char>(new char[1]);
            var line = new StringBuilder(300);

            while (!reader.EndOfStream)
            {
                var numRead = reader.Read(buffer);
                if (numRead == -1) continue; // End of stream
                if (buffer[0] == '\r' || buffer[0] == '\n') break;
                // TODO: Use proper encoding for creating the line
                line.Append(buffer);
            }

            return line.ToString();
        }
    }
}
