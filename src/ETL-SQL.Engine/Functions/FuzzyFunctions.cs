using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Fuzzy matching functions: NORMALIZE, SIMILARITY, LEVENSHTEIN, SOUNDEX, METAPHONE,
    /// DMETAPHONE, DMETAPHONE_ALT (Phases 1–2) and NGRAMS / NGRAM_TOKENS (Phase 3).
    /// Phase 4 (FUZZY JOIN syntax) is implemented in FuzzyJoinEngine.cs and the parser.
    ///</summary>
    public static class FuzzyFunctions
    {
        public static void Register(IFunctionRegistry registry)
        {
            // Phase 1 — Normalization
            registry.RegisterWithHelp("NORMALIZE",
                (args, ctx) => Task.FromResult(Normalize(args, ctx)),
                "NORMALIZE(str[, preset]): Normalize a string for fuzzy matching. Presets: COMPANY, PERSON, ADDRESS, PHONE, EMAIL.");

            // Phase 2 — Similarity & phonetics
            registry.RegisterWithHelp("SIMILARITY",
                (args, ctx) => Task.FromResult(Similarity(args, ctx)),
                "SIMILARITY(a, b[, algorithm]): Similarity score 0–1. Algorithms: JAROWINKLER (default), LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT.");

            registry.RegisterWithHelp("LEVENSHTEIN",
                (args, ctx) => Task.FromResult(LevenshteinFn(args, ctx)),
                "LEVENSHTEIN(a, b): Raw edit distance (integer) between two strings.");

            registry.RegisterWithHelp("SOUNDEX",
                (args, ctx) => Task.FromResult(SoundexFn(args, ctx)),
                "SOUNDEX(str): Returns the 4-character Soundex code (e.g. 'S532').");

            registry.RegisterWithHelp("DIFFERENCE",
                (args, ctx) => Task.FromResult(DifferenceFn(args, ctx)),
                "DIFFERENCE(s1, s2): Soundex similarity score 0-4 (4 = identical Soundex codes, 0 = none match).");

            registry.RegisterWithHelp("METAPHONE",
                (args, ctx) => Task.FromResult(MetaphoneFn(args, ctx)),
                "METAPHONE(str): Returns the original Metaphone phonetic encoding for English words.");

            registry.RegisterWithHelp("DMETAPHONE",
                (args, ctx) => Task.FromResult(DMetaphoneFn(args, ctx)),
                "DMETAPHONE(str): Returns the primary Double Metaphone code (handles many European name origins).");

            registry.RegisterWithHelp("DMETAPHONE_ALT",
                (args, ctx) => Task.FromResult(DMetaphoneAltFn(args, ctx)),
                "DMETAPHONE_ALT(str): Returns the alternate Double Metaphone code (for joins: match either primary or alternate).");

            // Phase 3 — Blocking utilities
            registry.RegisterWithHelp("NGRAMS",
                NgramsFn,
                "NGRAMS(str, n): Returns a table of n-character grams of str. Use with UNNEST for inverted-index blocking.");

            registry.RegisterWithHelp("NGRAM_TOKENS",
                NgramTokensFn,
                "NGRAM_TOKENS(str): Returns a table of 3-character grams (lowercased, space-padded) suitable for trigram blocking.");
        }

        // ── Phase 1 — NORMALIZE ───────────────────────────────────────────────────

        private static object? Normalize(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string s = args[0]!.ToString()!;
            string preset = args.Count >= 2 ? (args[1]?.ToString() ?? "").ToUpperInvariant().Trim() : "";
            return preset switch
            {
                "COMPANY" => NormalizeCompany(s),
                "PERSON" => NormalizePerson(s),
                "ADDRESS" => NormalizeAddress(s),
                "PHONE" => NormalizePhone(s),
                "EMAIL" => NormalizeEmail(s),
                _ => NormalizeBase(s)
            };
        }

        private static string NormalizeBase(string s)
        {
            s = s.Normalize(NormalizationForm.FormC);          // NFC
            s = s.ToLowerInvariant();
            s = Regex.Replace(s, @"[\p{Cc}\p{Cf}]", "");      // strip control/format chars
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static readonly Regex _legalSuffixes = new(
            @"\b(llc|inc|corp|ltd|co|plc|llp|gmbh|sa|nv|ag|bv|oy|as|ab|srl|spa|sas)\b\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly (string From, string To)[] _companyExpansions =
        {
            (@"\bint['l]?\b", "international"),
            (@"\bmfg\b",      "manufacturing"),
            (@"\bhldgs?\b",   "holdings"),
            (@"\bsvc?s?\b",   "services"),
            (@"\btech\b",     "technology"),
            (@"\bdept\b",     "department"),
            (@"&",            "and"),
        };

        private static string NormalizeCompany(string s)
        {
            s = NormalizeBase(s);
            // Remove leading articles
            s = Regex.Replace(s, @"^(the|a|an)\s+", "", RegexOptions.IgnoreCase);
            // Expand abbreviations
            foreach (var (from, to) in _companyExpansions)
                s = Regex.Replace(s, from, to, RegexOptions.IgnoreCase);
            // Remove legal suffixes
            s = _legalSuffixes.Replace(s, "");
            // Strip punctuation except hyphens within words
            s = Regex.Replace(s, @"(?<!\w)-|-(?!\w)|[^\w\s-]", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static readonly Regex _personTitles = new(
            @"\b(mr|mrs|ms|miss|dr|prof|rev|hon|sr|jr|ii|iii|iv|md|phd|dds|esq)\b\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string NormalizePerson(string s)
        {
            s = NormalizeBase(s);
            s = _personTitles.Replace(s, "");
            // Normalize hyphens in hyphenated names to space
            s = Regex.Replace(s, @"(\w)-(\w)", "$1 $2");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static readonly (string Pattern, string Replacement)[] _addressExpansions =
        {
            (@"\bst\.?\b",   "street"),
            (@"\bave\.?\b",  "avenue"),
            (@"\bblvd\.?\b", "boulevard"),
            (@"\bdr\.?\b",   "drive"),
            (@"\brd\.?\b",   "road"),
            (@"\bln\.?\b",   "lane"),
            (@"\bct\.?\b",   "court"),
            (@"\bpl\.?\b",   "place"),
            (@"\bhwy\.?\b",  "highway"),
            (@"\bpkwy\.?\b", "parkway"),
            (@"\bcir\.?\b",  "circle"),
            (@"\bsq\.?\b",   "square"),
            (@"\b(?<!\w)n\.?\b",  "north"),
            (@"\b(?<!\w)s\.?\b",  "south"),
            (@"\b(?<!\w)e\.?\b",  "east"),
            (@"\b(?<!\w)w\.?\b",  "west"),
            (@"\bne\.?\b",   "northeast"),
            (@"\bnw\.?\b",   "northwest"),
            (@"\bse\.?\b",   "southeast"),
            (@"\bsw\.?\b",   "southwest"),
        };

        private static string NormalizeAddress(string s)
        {
            s = NormalizeBase(s);
            // Remove unit designators: Apt 4B, Suite 200, Unit #3, #12
            s = Regex.Replace(s, @"\b(apt|ste|suite|unit|#)\s*\w+", "", RegexOptions.IgnoreCase);
            // Normalize PO Box variants
            s = Regex.Replace(s, @"\bpo\.?\s*box\b", "po box", RegexOptions.IgnoreCase);
            // Expand abbreviations
            foreach (var (pattern, replacement) in _addressExpansions)
                s = Regex.Replace(s, pattern, replacement, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static string NormalizePhone(string s)
        {
            s = Regex.Replace(s, @"[^\d]", "");
            // Remove leading country code 1 if 11 digits starting with 1
            if (s.Length == 11 && s[0] == '1') s = s[1..];
            return s;
        }

        private static string NormalizeEmail(string s) => s.ToLowerInvariant().Trim();

        // ── Phase 2 — SIMILARITY & LEVENSHTEIN ───────────────────────────────────

        private static object? Similarity(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string a = args[0]?.ToString() ?? "";
            string b = args[1]?.ToString() ?? "";
            string algo = args.Count >= 3 ? (args[2]?.ToString() ?? "").ToUpperInvariant().Trim() : "JAROWINKLER";

            double score = algo switch
            {
                "LEVENSHTEIN" => ComputeLevenshteinNormalized(a, b),
                "TRIGRAM" => ComputeTrigram(a, b),
                "JACCARD" => ComputeJaccard(a, b),
                "TOKENSORT" => ComputeTokenSort(a, b),
                _ => ComputeJaroWinkler(a, b)  // JAROWINKLER (default)
            };

            return (decimal)Math.Round(score, 6);
        }

        private static object? LevenshteinFn(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string a = args[0]?.ToString() ?? "";
            string b = args[1]?.ToString() ?? "";
            return (decimal)ComputeLevenshtein(a, b);
        }

        internal static int ComputeLevenshtein(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            // Two-row DP to keep memory O(min(m,n))
            if (a.Length > b.Length) (a, b) = (b, a);
            var prev = new int[a.Length + 1];
            var curr = new int[a.Length + 1];
            for (int i = 0; i <= a.Length; i++) prev[i] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                curr[0] = j;
                for (int i = 1; i <= a.Length; i++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[i] = Math.Min(Math.Min(curr[i - 1] + 1, prev[i] + 1), prev[i - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[a.Length];
        }

        private static double ComputeLevenshteinNormalized(string a, string b)
        {
            if (a == b) return 1.0;
            int maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1.0;
            return 1.0 - (double)ComputeLevenshtein(a, b) / maxLen;
        }

        internal static double ComputeJaro(string s1, string s2)
        {
            if (s1 == s2) return 1.0;
            if (s1.Length == 0 || s2.Length == 0) return 0.0;

            int matchRange = Math.Max(s1.Length, s2.Length) / 2 - 1;
            if (matchRange < 0) matchRange = 0;

            bool[] s1m = new bool[s1.Length];
            bool[] s2m = new bool[s2.Length];
            int matches = 0;

            for (int i = 0; i < s1.Length; i++)
            {
                int lo = Math.Max(0, i - matchRange);
                int hi = Math.Min(i + matchRange + 1, s2.Length);
                for (int j = lo; j < hi; j++)
                {
                    if (s2m[j] || s1[i] != s2[j]) continue;
                    s1m[i] = s2m[j] = true;
                    matches++;
                    break;
                }
            }

            if (matches == 0) return 0.0;

            int transpositions = 0, k = 0;
            for (int i = 0; i < s1.Length; i++)
            {
                if (!s1m[i]) continue;
                while (!s2m[k]) k++;
                if (s1[i] != s2[k]) transpositions++;
                k++;
            }

            return (matches / (double)s1.Length
                  + matches / (double)s2.Length
                  + (matches - transpositions / 2.0) / matches) / 3.0;
        }

        internal static double ComputeJaroWinkler(string s1, string s2)
        {
            if (s1 == s2) return 1.0;
            double jaro = ComputeJaro(s1, s2);
            int prefix = 0;
            int maxPrefix = Math.Min(4, Math.Min(s1.Length, s2.Length));
            while (prefix < maxPrefix && s1[prefix] == s2[prefix]) prefix++;
            return jaro + prefix * 0.1 * (1.0 - jaro);
        }

        private static List<string> GetTrigrams(string s)
        {
            s = " " + s.ToLowerInvariant() + " ";
            var grams = new List<string>(Math.Max(0, s.Length - 2));
            for (int i = 0; i <= s.Length - 3; i++)
                grams.Add(s.Substring(i, 3));
            return grams;
        }

        internal static double ComputeTrigram(string a, string b)
        {
            var ta = GetTrigrams(a);
            var tb = GetTrigrams(b);
            if (ta.Count == 0 && tb.Count == 0) return 1.0;
            if (ta.Count == 0 || tb.Count == 0) return 0.0;
            // Sørensen-Dice on trigram multisets
            var tbCopy = new List<string>(tb);
            int inter = 0;
            foreach (var g in ta)
            {
                int idx = tbCopy.IndexOf(g);
                if (idx < 0) continue;
                inter++;
                tbCopy.RemoveAt(idx);
            }
            return 2.0 * inter / (ta.Count + tb.Count);
        }

        internal static double ComputeJaccard(string a, string b)
        {
            var wa = new HashSet<string>(
                a.ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
            var wb = new HashSet<string>(
                b.ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
            if (wa.Count == 0 && wb.Count == 0) return 1.0;
            if (wa.Count == 0 || wb.Count == 0) return 0.0;
            int inter = wa.Count(w => wb.Contains(w));
            int union = wa.Count + wb.Count - inter;
            return (double)inter / union;
        }

        private static double ComputeTokenSort(string a, string b)
        {
            static string Sort(string s) =>
                string.Join(" ", s.ToLowerInvariant()
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .OrderBy(t => t));
            return ComputeJaroWinkler(Sort(a), Sort(b));
        }

        // ── Phase 2 — SOUNDEX ─────────────────────────────────────────────────────

        private static object? SoundexFn(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return ComputeSoundex(args[0]!.ToString() ?? "");
        }

        /// <summary>
        /// DIFFERENCE(s1, s2): returns 0-4 by comparing the two 4-character Soundex codes position by
        /// position (4 = identical codes, 0 = none match). NULL if either argument is NULL.
        /// </summary>
        private static object? DifferenceFn(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string a = ComputeSoundex(args[0]!.ToString() ?? "");
            string b = ComputeSoundex(args[1]!.ToString() ?? "");
            int score = 0;
            for (int i = 0; i < 4; i++)
                if (a[i] == b[i]) score++;
            return (decimal)score;
        }

        internal static string ComputeSoundex(string s)
        {
            var match = Regex.Match(s.Trim(), @"^[A-Za-z]+");
            if (!match.Success) return "0000";

            string word = match.Value.ToUpperInvariant();
            if (word.Length == 0) return "0000";

            char first = word[0];
            var sb = new StringBuilder();
            sb.Append(first);
            char prevCode = SoundexCode(first);

            for (int i = 1; i < word.Length && sb.Length < 4; i++)
            {
                char code = SoundexCode(word[i]);
                if (code == '0') { prevCode = code; continue; } // vowels reset adjacency
                if (code != prevCode) sb.Append(code);
                prevCode = code;
            }

            while (sb.Length < 4) sb.Append('0');
            return sb.ToString();
        }

        private static char SoundexCode(char c) => c switch
        {
            'B' or 'F' or 'P' or 'V' => '1',
            'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
            'D' or 'T' => '3',
            'L' => '4',
            'M' or 'N' => '5',
            'R' => '6',
            _ => '0'
        };

        // ── Phase 2 — METAPHONE (original 1990) ───────────────────────────────────

        private static object? MetaphoneFn(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return ComputeMetaphone(args[0]!.ToString() ?? "");
        }

        internal static string ComputeMetaphone(string s)
        {
            s = Regex.Replace(s.ToUpperInvariant(), @"[^A-Z]", "");
            if (s.Length == 0) return "";

            // Initial pair transformations
            if (s.Length >= 2 && s[..2] is "AE" or "GN" or "KN" or "PN" or "WR")
                s = s[1..];
            if (s.Length >= 2 && s[..2] == "WH") s = "W" + s[2..];

            static bool IsVowel(char c) => "AEIOU".Contains(c);

            var result = new StringBuilder();
            int i = 0;

            while (i < s.Length && result.Length < 6)
            {
                char c = s[i];

                // Skip duplicate adjacent consonants (except C)
                if (c != 'C' && i > 0 && s[i - 1] == c) { i++; continue; }

                switch (c)
                {
                    case 'A':
                    case 'E':
                    case 'I':
                    case 'O':
                    case 'U':
                        if (i == 0) result.Append(c);
                        i++; break;

                    case 'B':
                        // Silent after M at end of word
                        if (!(i == s.Length - 1 && i > 0 && s[i - 1] == 'M'))
                            result.Append('B');
                        i++; break;

                    case 'C':
                        if (Next(s, i, "IA") || Next(s, i, "H"))
                        { result.Append('X'); i += 2; }
                        else if (i + 1 < s.Length && s[i + 1] == 'K')
                        { result.Append('K'); i += 2; }
                        else if (i + 1 < s.Length && "IEY".Contains(s[i + 1]))
                        { result.Append('S'); i++; }
                        else
                        { result.Append('K'); i++; }
                        break;

                    case 'D':
                        if (i + 2 < s.Length && s[i + 1] == 'G' && "EIY".Contains(s[i + 2]))
                        { result.Append('J'); i += 3; }
                        else
                        { result.Append('T'); i++; }
                        break;

                    case 'F': result.Append('F'); i++; break;

                    case 'G':
                        if (i + 1 < s.Length && s[i + 1] == 'H')
                        {
                            if (i == 0 && i + 2 < s.Length && IsVowel(s[i + 2]))
                                result.Append('K');
                            // else silent
                            i += 2;
                        }
                        else if (i + 1 < s.Length && s[i + 1] == 'N')
                        { i++; }   // GN → silent G
                        else if (i + 1 < s.Length && "EIY".Contains(s[i + 1]))
                        { result.Append('J'); i++; }
                        else if (i + 1 < s.Length && IsVowel(s[i + 1]))
                        { result.Append('K'); i++; }
                        else
                        { result.Append('K'); i++; }
                        break;

                    case 'H':
                        if (i + 1 < s.Length && IsVowel(s[i + 1]) &&
                            (i == 0 || !IsVowel(s[i - 1])))
                            result.Append('H');
                        i++; break;

                    case 'J': result.Append('J'); i++; break;

                    case 'K':
                        if (i > 0 && s[i - 1] == 'C') { i++; break; }
                        result.Append('K'); i++; break;

                    case 'L': result.Append('L'); i++; break;
                    case 'M': result.Append('M'); i++; break;
                    case 'N': result.Append('N'); i++; break;

                    case 'P':
                        if (i + 1 < s.Length && s[i + 1] == 'H')
                        { result.Append('F'); i += 2; }
                        else
                        { result.Append('P'); i++; }
                        break;

                    case 'Q': result.Append('K'); i++; break;
                    case 'R': result.Append('R'); i++; break;

                    case 'S':
                        if (i + 1 < s.Length && s[i + 1] == 'H')
                        { result.Append('X'); i += 2; }
                        else if (i + 2 < s.Length && s[i + 1] == 'I' && "AO".Contains(s[i + 2]))
                        { result.Append('X'); i++; }
                        else
                        { result.Append('S'); i++; }
                        break;

                    case 'T':
                        if (i + 1 < s.Length && s[i + 1] == 'H')
                        { result.Append('0'); i += 2; }
                        else if (i + 2 < s.Length && s[i + 1] == 'I' && "AO".Contains(s[i + 2]))
                        { result.Append('X'); i++; }
                        else
                        { result.Append('T'); i++; }
                        break;

                    case 'V': result.Append('F'); i++; break;

                    case 'W':
                        if (i + 1 < s.Length && IsVowel(s[i + 1]))
                            result.Append('W');
                        i++; break;

                    case 'X': result.Append("KS"); i++; break;

                    case 'Y':
                        if (i + 1 < s.Length && IsVowel(s[i + 1]))
                            result.Append('Y');
                        i++; break;

                    case 'Z': result.Append('S'); i++; break;

                    default: i++; break;
                }
            }
            return result.ToString();
        }

        private static bool Next(string s, int pos, string check)
        {
            if (pos + 1 + check.Length > s.Length) return false;
            return s.AsSpan(pos + 1, check.Length).SequenceEqual(check.AsSpan());
        }

        // ── Phase 2 — DOUBLE METAPHONE ────────────────────────────────────────────

        private static object? DMetaphoneFn(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return DoubleMetaphone(args[0]!.ToString() ?? "").Primary;
        }

        private static object? DMetaphoneAltFn(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return DoubleMetaphone(args[0]!.ToString() ?? "").Alternate;
        }

        internal readonly record struct DmResult(string Primary, string Alternate);

        // Double Metaphone — translated from the Lawrence Philips reference (2000).
        // Handles English, German, French, Italian, Spanish, Slavic, and Greek patterns.
        internal static DmResult DoubleMetaphone(string input)
        {
            if (string.IsNullOrEmpty(input)) return new DmResult("", "");

            // Normalise: strip non-alpha, uppercase
            var raw = new StringBuilder(input.Length);
            foreach (char c in input) if (char.IsLetter(c)) raw.Append(c);
            string s = raw.ToString().ToUpperInvariant();
            if (s.Length == 0) return new DmResult("", "");

            int length = s.Length;
            var primary = new StringBuilder(8);
            var secondary = new StringBuilder(8);
            int current = 0;
            bool slavoGermanic = s.Contains('W') || s.Contains('K') || s.Contains("CZ") || s.Contains("WITZ");

            // Initial transformations
            if (StartsWith(s, "GN", "KN", "PN", "AE", "WR")) current++;

            bool initialVowel = IsVowel(s[0]);
            if (initialVowel) { Add(primary, secondary, "A"); current++; }

            static bool IsVowel(char c) => "AEIOUY".Contains(c);
            static bool StringAt(string str, int start, int len, params string[] values)
            {
                if (start < 0 || start + len > str.Length) return false;
                var span = str.AsSpan(start, len);
                foreach (var v in values) if (span.SequenceEqual(v.AsSpan())) return true;
                return false;
            }
            static void Add(StringBuilder p, StringBuilder s, string val) { p.Append(val); s.Append(val); }
            static void AddBoth(StringBuilder p, StringBuilder s, string pv, string sv) { p.Append(pv); s.Append(sv); }
            static bool StartsWith(string str, params string[] pfx)
            {
                foreach (var p in pfx) if (str.StartsWith(p, StringComparison.Ordinal)) return true;
                return false;
            }

            while (current < length && (primary.Length < 4 || secondary.Length < 4))
            {
                char c = s[current];
                switch (c)
                {
                    case 'A':
                    case 'E':
                    case 'I':
                    case 'O':
                    case 'U':
                    case 'Y':
                        if (current == 0) Add(primary, secondary, "A");
                        current++; break;

                    case 'B':
                        Add(primary, secondary, "P");
                        current += current + 1 < length && s[current + 1] == 'B' ? 2 : 1;
                        break;

                    case 'Ç': Add(primary, secondary, "S"); current++; break;

                    case 'C':
                        if (current > 1 && !IsVowel(s[current - 2]) && StringAt(s, current - 1, 3, "ACH") &&
                            !StringAt(s, current + 2, 1, "I") &&
                            (!StringAt(s, current + 2, 1, "E") || StringAt(s, current - 2, 6, "BACHER", "MACHER")))
                        { Add(primary, secondary, "K"); current += 2; break; }

                        if (current == 0 && StringAt(s, current, 6, "CAESAR"))
                        { Add(primary, secondary, "S"); current += 2; break; }

                        if (StringAt(s, current, 4, "CHIA")) { Add(primary, secondary, "K"); current += 2; break; }

                        if (StringAt(s, current, 2, "CH"))
                        {
                            if (current > 0 && StringAt(s, current, 4, "CHAE"))
                            { AddBoth(primary, secondary, "K", "X"); current += 2; break; }

                            if (current == 0 &&
                                (StringAt(s, current + 1, 5, "HARAC", "HARIS") || StringAt(s, current + 1, 3, "HOR", "HYM", "HIA", "HEM")) &&
                                !StringAt(s, 0, 5, "CHORE"))
                            { Add(primary, secondary, "K"); current += 2; break; }

                            if (StringAt(s, 0, 4, "VAN ", "VON ") || StringAt(s, 0, 3, "SCH") ||
                                StringAt(s, current - 2, 6, "ORCHES", "ARCHIT", "ORCHID") ||
                                StringAt(s, current + 2, 1, "T", "S") ||
                                (StringAt(s, current - 1, 1, "A", "O", "U", "E") && current == 0 ||
                                 StringAt(s, current - 1, 1, "A", "O", "U", "E")) &&
                                StringAt(s, current + 2, 1, "L", "R", "N", "M", "B", "H", "F", "V", "W"))
                            { Add(primary, secondary, "K"); }
                            else if (current > 0)
                            { AddBoth(primary, secondary, s[0] == 'M' ? "K" : "X", "K"); }
                            else
                            { Add(primary, secondary, "X"); }

                            current += 2; break;
                        }

                        if (StringAt(s, current, 2, "CZ") && !StringAt(s, current - 2, 4, "WICZ"))
                        { AddBoth(primary, secondary, "S", "X"); current += 2; break; }

                        if (StringAt(s, current + 1, 3, "CIA"))
                        { Add(primary, secondary, "X"); current += 3; break; }

                        if (StringAt(s, current, 2, "CC") && !(current == 1 && s[0] == 'M'))
                        {
                            if (StringAt(s, current + 2, 1, "I", "E", "H"))
                            { AddBoth(primary, secondary, "X", "K"); current += 3; break; }
                            Add(primary, secondary, "K"); current += 2; break;
                        }

                        if (StringAt(s, current, 2, "CK", "CG", "CQ"))
                        { Add(primary, secondary, "K"); current += 2; break; }

                        if (StringAt(s, current, 2, "CI", "CE", "CY"))
                        { AddBoth(primary, secondary, slavoGermanic ? "S" : "S", "S"); current += 2; break; }

                        Add(primary, secondary, "K");
                        if (StringAt(s, current + 1, 2, " C", " Q", " G")) current += 3;
                        else current += StringAt(s, current + 1, 1, "C", "K", "Q") &&
                                        !StringAt(s, current + 1, 2, "CE", "CI") ? 2 : 1;
                        break;

                    case 'D':
                        if (StringAt(s, current, 2, "DG"))
                        {
                            if (StringAt(s, current + 2, 1, "I", "E", "Y"))
                            { Add(primary, secondary, "J"); current += 3; }
                            else { Add(primary, secondary, "TK"); current += 2; }
                            break;
                        }
                        Add(primary, secondary, "T");
                        current += StringAt(s, current, 2, "DT", "DD") ? 2 : 1;
                        break;

                    case 'F':
                        Add(primary, secondary, "F");
                        current += current + 1 < length && s[current + 1] == 'F' ? 2 : 1;
                        break;

                    case 'G':
                        if (current + 1 < length && s[current + 1] == 'H')
                        {
                            if (current > 0 && !IsVowel(s[current - 1]))
                            { Add(primary, secondary, "K"); current += 2; break; }
                            if (current == 0)
                            {
                                if (current + 2 < length && IsVowel(s[current + 2]))
                                    Add(primary, secondary, "K");
                                else
                                    Add(primary, secondary, "K");
                                current += 2; break;
                            }
                            if ((current > 1 && StringAt(s, current - 2, 1, "B", "H", "D")) ||
                                (current > 2 && StringAt(s, current - 3, 1, "B", "H", "D")) ||
                                (current > 3 && StringAt(s, current - 4, 1, "B", "H")))
                            { current += 2; break; }

                            if (current > 2 && s[current - 1] == 'U' &&
                                StringAt(s, current - 3, 1, "C", "G", "L", "R", "T"))
                            { Add(primary, secondary, "F"); current += 2; break; }

                            if (current > 0 && s[current - 1] != 'I')
                                Add(primary, secondary, "K");
                            current += 2; break;
                        }

                        if (current + 1 < length && s[current + 1] == 'N')
                        {
                            if (current == 1 && IsVowel(s[0]) && !slavoGermanic)
                                AddBoth(primary, secondary, "KN", "N");
                            else if (!StringAt(s, current + 2, 2, "EY") && s[current + 1] != 'Y' && !slavoGermanic)
                                AddBoth(primary, secondary, "N", "KN");
                            else
                                Add(primary, secondary, "KN");
                            current += 2; break;
                        }

                        if (StringAt(s, current + 1, 2, "LI") && !slavoGermanic)
                        { AddBoth(primary, secondary, "KL", "L"); current += 2; break; }

                        if (current == 0 &&
                            (s[current + 1] == 'Y' || StringAt(s, current + 1, 2,
                                "ES", "EP", "EB", "EL", "EY", "IB", "IL", "IN", "IE", "EI", "ER")))
                        { AddBoth(primary, secondary, "K", "J"); current += 2; break; }

                        if ((StringAt(s, current + 1, 2, "ER") || s[current + 1] == 'Y') &&
                            !StringAt(s, 0, 6, "DANGER", "RANGER", "MANGER") &&
                            !StringAt(s, current - 1, 1, "E", "I") &&
                            !StringAt(s, current - 1, 3, "RGY", "OGY"))
                        { AddBoth(primary, secondary, "K", "J"); current += 2; break; }

                        if (StringAt(s, current + 1, 1, "E", "I", "Y") || StringAt(s, current - 1, 4, "AGGI", "OGGI"))
                        {
                            if (StringAt(s, 0, 4, "VAN ", "VON ") || StringAt(s, 0, 3, "SCH") || StringAt(s, current + 1, 2, "ET"))
                                Add(primary, secondary, "K");
                            else if (StringAt(s, current + 1, 4, "IER "))
                                Add(primary, secondary, "J");
                            else
                                AddBoth(primary, secondary, "J", "K");
                            current += 2; break;
                        }

                        Add(primary, secondary, "K");
                        current += current + 1 < length && s[current + 1] == 'G' ? 2 : 1;
                        break;

                    case 'H':
                        if ((current == 0 || IsVowel(s[current - 1])) && current + 1 < length && IsVowel(s[current + 1]))
                        {
                            Add(primary, secondary, "H");
                            current += 2;
                        }
                        else
                        {
                            current++;
                        }
                        break;

                    case 'J':
                        if (StringAt(s, current, 4, "JOSE") || StringAt(s, 0, 4, "SAN "))
                        {
                            if ((current == 0 && (current + 4 >= length || s[current + 4] == ' ')) ||
                                StringAt(s, 0, 4, "SAN "))
                                Add(primary, secondary, "H");
                            else
                                AddBoth(primary, secondary, "J", "H");
                            current++; break;
                        }
                        if (current == 0 && !StringAt(s, current, 4, "JOSE"))
                            AddBoth(primary, secondary, "J", "A");
                        else if (IsVowel(s[current - 1]) && !slavoGermanic && (s[current + (current + 1 < length ? 1 : 0)] == 'A' || s[current + (current + 1 < length ? 1 : 0)] == 'O'))
                            AddBoth(primary, secondary, "J", "H");
                        else if (current == length - 1)
                            AddBoth(primary, secondary, "J", "");
                        else if (!StringAt(s, current + 1, 1, "L", "T", "K", "S", "N", "M", "B", "Z") && !StringAt(s, current - 1, 1, "S", "K", "L"))
                            Add(primary, secondary, "J");
                        current += current + 1 < length && s[current + 1] == 'J' ? 2 : 1;
                        break;

                    case 'K':
                        Add(primary, secondary, "K");
                        current += current + 1 < length && s[current + 1] == 'K' ? 2 : 1;
                        break;

                    case 'L':
                        if (current + 1 < length && s[current + 1] == 'L')
                        {
                            if ((current == length - 3 && StringAt(s, current - 1, 4, "ILLO", "ILLA", "ALLE")) ||
                                ((StringAt(s, length - 2, 2, "AS", "OS") || StringAt(s, length - 1, 1, "A", "O")) &&
                                 StringAt(s, current - 1, 4, "ALLE")))
                            { AddBoth(primary, secondary, "L", ""); current += 2; break; }
                            current += 2;
                        }
                        else current++;
                        Add(primary, secondary, "L");
                        break;

                    case 'M':
                        if ((StringAt(s, current - 1, 3, "UMB") && (current + 1 == length || StringAt(s, current + 2, 2, "ER"))) ||
                            (current + 1 < length && s[current + 1] == 'M'))
                        { current += 2; }
                        else current++;
                        Add(primary, secondary, "M");
                        break;

                    case 'N':
                        Add(primary, secondary, "N");
                        current += current + 1 < length && s[current + 1] == 'N' ? 2 : 1;
                        break;

                    case 'Ñ': Add(primary, secondary, "N"); current++; break;

                    case 'P':
                        if (current + 1 < length && s[current + 1] == 'H')
                        { Add(primary, secondary, "F"); current += 2; break; }
                        Add(primary, secondary, "P");
                        current += current + 1 < length && s[current + 1] == 'P' ? 2 : 1;
                        break;

                    case 'Q':
                        Add(primary, secondary, "K");
                        current += current + 1 < length && s[current + 1] == 'Q' ? 2 : 1;
                        break;

                    case 'R':
                        if (current == length - 1 && !slavoGermanic && StringAt(s, current - 2, 2, "IE") &&
                            !StringAt(s, current - 4, 2, "ME", "MA"))
                            AddBoth(primary, secondary, "", "R");
                        else
                            Add(primary, secondary, "R");
                        current += current + 1 < length && s[current + 1] == 'R' ? 2 : 1;
                        break;

                    case 'S':
                        if (StringAt(s, current - 1, 3, "ISL", "YSL")) { current++; break; }
                        if (current == 0 && StringAt(s, current, 5, "SUGAR")) { AddBoth(primary, secondary, "X", "S"); current++; break; }
                        if (StringAt(s, current, 2, "SH"))
                        { Add(primary, secondary, "X"); current += 2; break; }
                        if (StringAt(s, current, 3, "SIO", "SIA"))
                        { AddBoth(primary, secondary, slavoGermanic ? "S" : "X", "S"); current += 3; break; }
                        if ((current == 0 && StringAt(s, current + 1, 1, "M", "N", "L", "W")) || StringAt(s, current + 1, 1, "Z"))
                        { AddBoth(primary, secondary, "S", "X"); current += StringAt(s, current + 1, 1, "Z") ? 2 : 1; break; }
                        if (StringAt(s, current, 2, "SC"))
                        {
                            if (s[current + 2] == 'H')
                            {
                                if (StringAt(s, current + 3, 2, "OO", "ER", "EN", "UY", "ED", "EM"))
                                    AddBoth(primary, secondary, "SK", "SK");
                                else if (current == 0 && !IsVowel(s[3]) && s[3] != 'W')
                                    AddBoth(primary, secondary, "X", "S");
                                else
                                    Add(primary, secondary, "X");
                                current += 3; break;
                            }
                            if (StringAt(s, current + 2, 1, "I", "E", "Y"))
                            { Add(primary, secondary, "S"); current += 3; break; }
                            Add(primary, secondary, "SK"); current += 3; break;
                        }
                        if (current == length - 1 && StringAt(s, current - 2, 2, "AI", "OI"))
                            AddBoth(primary, secondary, "", "S");
                        else
                            Add(primary, secondary, "S");
                        current += current + 1 < length && s[current + 1] == 'S' ? 2 : 1;
                        break;

                    case 'T':
                        if (StringAt(s, current, 4, "TION") || StringAt(s, current, 3, "TIA", "TCH"))
                        { Add(primary, secondary, "X"); current += 3; break; }
                        if (StringAt(s, current, 2, "TH") || StringAt(s, current, 3, "TTH"))
                        {
                            if (StringAt(s, current + 2, 2, "OM", "AM") ||
                                StringAt(s, 0, 4, "VAN ", "VON ") || StringAt(s, 0, 3, "SCH"))
                                Add(primary, secondary, "T");
                            else
                                AddBoth(primary, secondary, "0", "T");
                            current += 2; break;
                        }
                        Add(primary, secondary, "T");
                        current += current + 1 < length && s[current + 1] is 'T' or 'D' ? 2 : 1;
                        break;

                    case 'V':
                        Add(primary, secondary, "F");
                        current += current + 1 < length && s[current + 1] == 'V' ? 2 : 1;
                        break;

                    case 'W':
                        if (StringAt(s, current, 2, "WR")) { Add(primary, secondary, "R"); current += 2; break; }
                        if (current == 0 && (IsVowel(current + 1 < length ? s[current + 1] : ' ') || StringAt(s, current, 2, "WH")))
                            AddBoth(primary, secondary, IsVowel(current + 1 < length ? s[current + 1] : ' ') ? "A" : "A", "F");
                        if ((current == length - 1 && IsVowel(s[current - 1])) ||
                            StringAt(s, current - 1, 5, "EWSKI", "EWSKY", "OWSKI", "OWSKY") ||
                            StringAt(s, 0, 3, "SCH"))
                        { AddBoth(primary, secondary, "", "F"); current++; break; }
                        if (StringAt(s, current, 4, "WICZ", "WITZ"))
                        { AddBoth(primary, secondary, "TS", "FX"); current += 4; break; }
                        current++; break;

                    case 'X':
                        if (!(current == length - 1 && (StringAt(s, current - 3, 3, "IAU", "EAU") || StringAt(s, current - 2, 2, "AU", "OU"))))
                            Add(primary, secondary, "KS");
                        current += current + 1 < length && s[current + 1] is 'C' or 'X' ? 2 : 1;
                        break;

                    case 'Z':
                        if (current + 1 < length && s[current + 1] == 'H')
                        { Add(primary, secondary, "J"); current += 2; break; }
                        if (StringAt(s, current + 1, 2, "ZO", "ZI", "ZA") ||
                            (slavoGermanic && current > 0 && s[current - 1] != 'T'))
                            AddBoth(primary, secondary, "S", "TS");
                        else
                            Add(primary, secondary, "S");
                        current += current + 1 < length && s[current + 1] == 'Z' ? 2 : 1;
                        break;

                    default: current++; break;
                }
            }

            string p = primary.ToString();
            string a = secondary.ToString();
            return new DmResult(p, p == a ? p : a);
        }

        // ── Phase 3 — NGRAMS / NGRAM_TOKENS ──────────────────────────────────────

        private static async Task<object?> NgramsFn(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null) return new DataTable();
            string s = args[0]!.ToString()!;
            int n = Math.Max(1, Convert.ToInt32(args[1] ?? 3));

            var dt = new DataTable();
            dt.SetColumns(new[] { "Value" });
            for (int i = 0; i <= s.Length - n; i++)
                await dt.AddRowAsync(new Row { ["Value"] = s.Substring(i, n) });
            return dt;
        }

        private static async Task<object?> NgramTokensFn(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return new DataTable();
            string s = " " + (args[0]!.ToString() ?? "").ToLowerInvariant() + " ";
            var dt = new DataTable();
            dt.SetColumns(new[] { "Value" });
            for (int i = 0; i <= s.Length - 3; i++)
                await dt.AddRowAsync(new Row { ["Value"] = s.Substring(i, 3) });
            return dt;
        }
    }
}
