using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog;

namespace Flexy.Tests
{
    public static class StringAlgorithms
    {
        /// <summary>
        ///     Shortens each string to become as short as possible, while keeping each entry unique.
        /// </summary>
        /// <param name="paths">The list of input paths</param>
        /// <param name="fromRight">Whether the comparison is to be done from the left or right side.</param>
        /// <param name="forceKeep">
        ///     Number of characters to not compare during comparison. This is always according to the
        ///     fromRight value. If fromRight, then forceKeep will keep the X rightmost characters in the result, even if they are
        ///     equal.
        /// </param>
        /// <param name="padChar">
        ///     The character to use for padding. If fromRight is true, then padding is on the left - and vice
        ///     versa
        /// </param>
        /// <param name="truncChar">The character to use as a truncation character.</param>
        /// <param name="deltaContext">How many characters around a difference to keep to show context (5 means 2 before and 2 after)</param>
        /// <returns>A tuple with the Result list of paths, plus a boolean Success indicator</returns>
        public static (List<string> Result, bool Success) AbbreviatedUniquePath(IReadOnlyList<string> paths, bool fromRight = true, int forceKeep = 10, char padChar = ' ', char truncChar = '…', int deltaContext = 5)
        {
            var largestInputLength = paths.Max(s => s.Length);

            forceKeep = Math.Min(forceKeep, largestInputLength);

            var fillChar = '▢';
            var paddedInput = (fromRight
                    ? paths.Select(s => s.PadLeft(largestInputLength, fillChar))
                    : paths.Select(s => s.PadRight(largestInputLength, fillChar))
                ).ToList();

            var forceKeepList = (forceKeep > 0
                    ? fromRight
                        ? paddedInput.Select(s => s.Substring(largestInputLength - forceKeep))
                        : paddedInput.Select(s => s.Substring(0, forceKeep))
                    : Enumerable.Repeat("", paths.Count)
                ).ToList();

            if (forceKeep > 0 && forceKeepList.Distinct().Count() == paths.Count)
            {
                // The forcePartList itself is enough to distinguish the input paths
                if (forceKeep < largestInputLength)
                {
                    var result = forceKeepList.Select(f => $"{truncChar}{f}");
                    return (result.ToList(), true);
                }

                return (forceKeepList, true);
            }

            var equalityMap = Enumerable.Repeat(0, largestInputLength - forceKeep).ToArray();
            var i = 0;
            while (i < largestInputLength - forceKeep)
            {
                var j = fromRight ? largestInputLength - forceKeep - i - 1 : i;
                var chars = paddedInput.Select(s => s[j]);
                equalityMap[j] = chars.Distinct().Count();
                i++;
            }

            var equalityMapList = equalityMap.ToList();

            // Find sorted distinct equality-levels
            var sortedEqualityMap = equalityMap.Distinct().ToList();
            sortedEqualityMap.Sort();
            sortedEqualityMap.Reverse();

            // Start with trying to replace use the most different characters only
            var testedResults = new List<string>(paths.Count);
            testedResults.AddRange(Enumerable.Repeat(new string('▢', largestInputLength - forceKeep), paths.Count));
            foreach (var eqLevel in sortedEqualityMap)
            {
                var eqLevelPos = largestInputLength - forceKeep - 1;
                do
                {
                    eqLevelPos = equalityMapList.FindLastIndex(eqLevelPos, e => e == eqLevel);
                    for (var index = 0; index < testedResults.Count; index++)
                        // Special treatment for path-dividers
                        if (new[] {'/', '\\'}.Contains(paddedInput[index][eqLevelPos]))
                        {
                            // Insert at least two chars before and after to get context
                            var start = Math.Max(0, eqLevelPos - deltaContext / 2);
                            var length = Math.Min(paddedInput[index].Length - forceKeep, deltaContext);
                            var toInsert = paddedInput[index].Substring(start, length);
                            var before = testedResults[index].Substring(0, start);
                            var after = testedResults[index].Substring(start + length);
                            testedResults[index] = $"{before}{toInsert}{after}";
                            eqLevelPos = start;
                        }
                        else
                        {
                            var tmpCharArray = testedResults[index].ToCharArray();
                            tmpCharArray[eqLevelPos] = paddedInput[index][eqLevelPos];
                            testedResults[index] = new string(tmpCharArray);
                        }

                    // Test if all strings are now different
                    if (eqLevel == paths.Count || testedResults.Distinct().Count() == paths.Count)
                    {
                        // All are different
                        for (var k = 0; k < paddedInput.Count; k++)
                            // Append the forceKeepList strings
                            testedResults[k] = $"{testedResults[k]}{forceKeepList[k]}";

                        var abbreviatedResults = testedResults.Select(t =>
                        {
                            var regex = new Regex($"{fillChar}+");
                            var result = regex.Replace(t, truncChar.ToString());
                            return result;
                        }).ToList();

                        var longestPaddedResult = abbreviatedResults.Max(t => t.Length);

                        var paddedResults = (fromRight
                                ? abbreviatedResults.Select(s => s.PadLeft(longestPaddedResult, padChar))
                                : abbreviatedResults.Select(s => s.PadRight(longestPaddedResult, padChar))
                            ).ToList();

                        return (paddedResults, true);
                    }
                } while (eqLevelPos >= 0);
            }

            // Did not find a way to differentiate all string.
            return (testedResults, false);
        }
    }
}