using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace CodeWalker.OIVInstaller
{
    /// <summary>
    /// XPath 1.0 string equality is case-sensitive, but GTA data names are
    /// case-insensitive (joaat-hashed) and ship in either case depending on game
    /// build — popgroups has "VEH_MID" in some builds and "veh_mid" in others, so a
    /// package xpath like Name[.="veh_utility"] silently misses. Select with the
    /// exact xpath first; when nothing matches, retry with every string-literal
    /// equality comparison rewritten through translate() to ignore case.
    /// </summary>
    internal static class XmlCaseTolerant
    {
        private const string Lower = "abcdefghijklmnopqrstuvwxyz";
        private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static XmlNode SelectSingleNode(XmlNode root, string xpath, out bool usedCaseFallback)
        {
            usedCaseFallback = false;
            var node = root.SelectSingleNode(xpath);
            if (node != null) return node;

            string relaxed = RelaxComparisons(xpath);
            if (relaxed == xpath) return null;
            try
            {
                node = root.SelectSingleNode(relaxed);
            }
            catch
            {
                // Rewrite produced an invalid xpath — keep the original not-found result.
                return null;
            }
            usedCaseFallback = node != null;
            return node;
        }

        // lhs="Value" / lhs='Value' → translate(lhs,'a…z','A…Z')="VALUE".
        // XPath 1.0 has no lower-case(); translate() is the standard workaround.
        internal static string RelaxComparisons(string xpath)
        {
            return Regex.Replace(xpath,
                @"(?<lhs>[^\s\[\]=!<>|,'""]+)\s*=\s*(?:""(?<val>[^""]*)""|'(?<val>[^']*)')",
                m =>
                {
                    string val = m.Groups["val"].Value;
                    if (!val.Any(char.IsLetter)) return m.Value;
                    string quote = val.Contains('"') ? "'" : "\"";
                    return $"translate({m.Groups["lhs"].Value},'{Lower}','{Upper}')={quote}{val.ToUpperInvariant()}{quote}";
                });
        }
    }
}
