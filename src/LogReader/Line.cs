namespace Flexy
{
    /// <summary>
    /// A class that represents a singular line, read from a file.
    /// </summary>
    public class Line
    {
        /// <summary>
        /// The byte-position of this line within the file
        /// </summary>
        public long Pos { get; set; }
        /// <summary>
        /// The line-number for this content in the file
        /// </summary>
        public long Num { get; set; }
        /// <summary>
        /// The content of the line
        /// </summary>
        public string Content { get; set; }
    }
}