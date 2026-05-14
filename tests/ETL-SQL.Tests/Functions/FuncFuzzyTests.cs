using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Data;
using ETL_SQL.Engine.Functions;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Functions
{
    public class FuzzyFunctionTests
    {
        private readonly Evaluator _ev;
        public FuzzyFunctionTests()
            => _ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private async Task<object?> Eval(string expr)
        {
            var script = new Parser(new Lexer($"SELECT {expr} AS v").Tokenize()).Parse();
            var results = new List<DataTable>();
            await foreach (var b in _ev.ExecuteQuery(script.Statements[0])) results.Add(b);
            return results[0].Rows[0]["v"];
        }

        // ── Phase 1: NORMALIZE ────────────────────────────────────────────────────

        [Fact]
        public async Task Normalize_Base_LowercasesAndCollapsesSpace()
        {
            var v = (string?)await Eval("NORMALIZE('  Hello   World  ')");
            Assert.Equal("hello world", v);
        }

        [Fact]
        public async Task Normalize_Company_RemovesLegalSuffixAndArticle()
        {
            var v = (string?)await Eval("NORMALIZE('The Acme Corp.', 'COMPANY')");
            Assert.Equal("acme", v);
        }

        [Fact]
        public async Task Normalize_Company_ExpandsAmpersand()
        {
            var v = (string?)await Eval("NORMALIZE('Smith & Jones LLC', 'COMPANY')");
            Assert.Equal("smith and jones", v);
        }

        [Fact]
        public async Task Normalize_Person_RemovesTitleAndSuffix()
        {
            var v = (string?)await Eval("NORMALIZE('Dr. Jane Smith Jr.', 'PERSON')");
            Assert.Equal("jane smith", v);
        }

        [Fact]
        public async Task Normalize_Phone_DigitsOnly()
        {
            var v = (string?)await Eval("NORMALIZE('(555) 867-5309', 'PHONE')");
            Assert.Equal("5558675309", v);
        }

        [Fact]
        public async Task Normalize_Phone_StripLeadingCountryCode()
        {
            var v = (string?)await Eval("NORMALIZE('15558675309', 'PHONE')");
            Assert.Equal("5558675309", v);
        }

        [Fact]
        public async Task Normalize_Email_LowercaseAndTrim()
        {
            var v = (string?)await Eval("NORMALIZE('  User@Example.COM  ', 'EMAIL')");
            Assert.Equal("user@example.com", v);
        }

        [Fact]
        public async Task Normalize_Null_ReturnsNull()
        {
            var v = await Eval("NORMALIZE(NULL)");
            Assert.Null(v);
        }

        // ── Phase 2: SIMILARITY ───────────────────────────────────────────────────

        [Fact]
        public async Task Similarity_IdenticalStrings_Returns1()
        {
            var v = Convert.ToDecimal(await Eval("SIMILARITY('hello', 'hello')"));
            Assert.Equal(1m, v);
        }

        [Fact]
        public async Task Similarity_EmptyVsEmpty_Returns1()
        {
            var v = Convert.ToDecimal(await Eval("SIMILARITY('', '')"));
            Assert.Equal(1m, v);
        }

        [Fact]
        public async Task Similarity_JaroWinkler_CloseNames()
        {
            var v = Convert.ToDecimal(await Eval("SIMILARITY('MARTHA', 'MARHTA', 'JAROWINKLER')"));
            Assert.True(v > 0.9m, $"Expected > 0.9 but got {v}");
        }

        [Fact]
        public async Task Similarity_Levenshtein_Normalized()
        {
            var v = Convert.ToDecimal(await Eval("SIMILARITY('kitten', 'sitting', 'LEVENSHTEIN')"));
            Assert.True(v > 0m && v < 1m);
        }

        [Fact]
        public async Task Similarity_Trigram_ExactMatch()
        {
            var v = Convert.ToDecimal(await Eval("SIMILARITY('hello world', 'hello world', 'TRIGRAM')"));
            Assert.Equal(1m, v);
        }

        [Fact]
        public async Task Similarity_Jaccard_DisjointSets_Returns0()
        {
            var v = Convert.ToDecimal(await Eval("SIMILARITY('abc', 'xyz', 'JACCARD')"));
            Assert.Equal(0m, v);
        }

        [Fact]
        public async Task Similarity_TokenSort_ReorderInsensitive()
        {
            var ordered = Convert.ToDecimal(await Eval("SIMILARITY('alice bob', 'alice bob', 'TOKENSORT')"));
            var reordered = Convert.ToDecimal(await Eval("SIMILARITY('alice bob', 'bob alice', 'TOKENSORT')"));
            Assert.Equal(1m, ordered);
            Assert.Equal(1m, reordered);
        }

        // ── Phase 2: LEVENSHTEIN ─────────────────────────────────────────────────

        [Fact]
        public async Task Levenshtein_SameString_Returns0()
        {
            var v = Convert.ToDecimal(await Eval("LEVENSHTEIN('abc', 'abc')"));
            Assert.Equal(0m, v);
        }

        [Fact]
        public async Task Levenshtein_KittenSitting_Returns3()
        {
            var v = Convert.ToDecimal(await Eval("LEVENSHTEIN('kitten', 'sitting')"));
            Assert.Equal(3m, v);
        }

        [Fact]
        public async Task Levenshtein_EmptyVsWord()
        {
            var v = Convert.ToDecimal(await Eval("LEVENSHTEIN('', 'abc')"));
            Assert.Equal(3m, v);
        }

        // ── Phase 2: SOUNDEX ─────────────────────────────────────────────────────

        [Fact]
        public async Task Soundex_Robert_Returns_R163()
        {
            var v = (string?)await Eval("SOUNDEX('Robert')");
            Assert.Equal("R163", v);
        }

        [Fact]
        public async Task Soundex_Rupert_SameAs_Robert()
        {
            var robert = (string?)await Eval("SOUNDEX('Robert')");
            var rupert = (string?)await Eval("SOUNDEX('Rupert')");
            Assert.Equal(robert, rupert);
        }

        [Fact]
        public async Task Soundex_EmptyString_Returns_0000()
        {
            var v = (string?)await Eval("SOUNDEX('')");
            Assert.Equal("0000", v);
        }

        [Fact]
        public async Task Soundex_Null_ReturnsNull()
        {
            var v = await Eval("SOUNDEX(NULL)");
            Assert.Null(v);
        }

        // ── Phase 2: METAPHONE ───────────────────────────────────────────────────

        [Fact]
        public async Task Metaphone_Smith_Returns_SM0()
        {
            var v = (string?)await Eval("METAPHONE('Smith')");
            Assert.Equal("SM0", v);
        }

        [Fact]
        public async Task Metaphone_SmithVsSmythe_SameCode()
        {
            var a = (string?)await Eval("METAPHONE('Smith')");
            var b = (string?)await Eval("METAPHONE('Smythe')");
            Assert.Equal(a, b);
        }

        [Fact]
        public async Task Metaphone_Null_ReturnsNull()
        {
            var v = await Eval("METAPHONE(NULL)");
            Assert.Null(v);
        }

        // ── Phase 2: DMETAPHONE / DMETAPHONE_ALT ─────────────────────────────────

        [Fact]
        public async Task DMetaphone_Smith_Returns_SM0()
        {
            var v = (string?)await Eval("DMETAPHONE('Smith')");
            Assert.Equal("SM0", v);
        }

        [Fact]
        public async Task DMetaphone_Thompson_NotEmpty()
        {
            var v = (string?)await Eval("DMETAPHONE('Thompson')");
            Assert.NotNull(v);
            Assert.NotEmpty(v!);
        }

        [Fact]
        public async Task DMetaphoneAlt_ReturnsString()
        {
            var v = await Eval("DMETAPHONE_ALT('Schmidt')");
            Assert.NotNull(v);
        }

        [Fact]
        public async Task DMetaphone_Null_ReturnsNull()
        {
            var v = await Eval("DMETAPHONE(NULL)");
            Assert.Null(v);
        }

        // ── Phase 3: NGRAMS ──────────────────────────────────────────────────────

        [Fact]
        public async Task Ngrams_Returns3Grams()
        {
            var script = new Parser(new Lexer("SELECT * FROM NGRAMS('hello', 3)").Tokenize()).Parse();
            var results = new List<DataTable>();
            await foreach (var b in _ev.ExecuteQuery(script.Statements[0])) results.Add(b);
            var values = results.SelectMany(t => t.Rows).Select(r => r["Value"]?.ToString()).ToList();
            Assert.Contains("hel", values);
            Assert.Contains("ell", values);
            Assert.Contains("llo", values);
            Assert.Equal(3, values.Count);
        }

        [Fact]
        public async Task Ngrams_StringShorterThanN_ReturnsEmpty()
        {
            var script = new Parser(new Lexer("SELECT * FROM NGRAMS('hi', 5)").Tokenize()).Parse();
            var results = new List<DataTable>();
            await foreach (var b in _ev.ExecuteQuery(script.Statements[0])) results.Add(b);
            var count = results.Sum(t => t.Rows.Count);
            Assert.Equal(0, count);
        }

        // ── Phase 3: NGRAM_TOKENS ────────────────────────────────────────────────

        [Fact]
        public async Task NgramTokens_SpacePaddedTrigrams()
        {
            var script = new Parser(new Lexer("SELECT * FROM NGRAM_TOKENS('cat')").Tokenize()).Parse();
            var results = new List<DataTable>();
            await foreach (var b in _ev.ExecuteQuery(script.Statements[0])) results.Add(b);
            var values = results.SelectMany(t => t.Rows).Select(r => r["Value"]?.ToString()).ToList();
            // space-padded: " ca", "cat", "at "
            Assert.Contains(" ca", values);
            Assert.Contains("cat", values);
            Assert.Contains("at ", values);
        }

        [Fact]
        public async Task NgramTokens_Lowercased()
        {
            var script = new Parser(new Lexer("SELECT * FROM NGRAM_TOKENS('CAT')").Tokenize()).Parse();
            var results = new List<DataTable>();
            await foreach (var b in _ev.ExecuteQuery(script.Statements[0])) results.Add(b);
            var values = results.SelectMany(t => t.Rows).Select(r => r["Value"]?.ToString()).ToList();
            Assert.Contains("cat", values);
        }

        // ── Internal unit tests (ComputeLevenshtein) ──────────────────────────────

        [Theory]
        [InlineData("", "", 0)]
        [InlineData("a", "", 1)]
        [InlineData("", "a", 1)]
        [InlineData("abc", "abc", 0)]
        [InlineData("kitten", "sitting", 3)]
        [InlineData("Saturday", "Sunday", 3)]
        public void ComputeLevenshtein_KnownValues(string a, string b, int expected)
            => Assert.Equal(expected, FuzzyFunctions.ComputeLevenshtein(a, b));

        [Theory]
        [InlineData("Smith", "SM0")]
        [InlineData("Knight", "NT")]
        [InlineData("Pneumatic", "NMTK")]
        public void ComputeMetaphone_KnownWords(string input, string expected)
            => Assert.Equal(expected, FuzzyFunctions.ComputeMetaphone(input));
    }
}
