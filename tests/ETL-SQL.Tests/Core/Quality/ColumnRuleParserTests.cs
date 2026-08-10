using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;
using Xunit;

namespace ETL_SQL.Tests.Core.Quality
{
    /// <summary>
    /// The @expect mini-DSL parser: rule forms, top-level comma combination with literal commas
    /// inside MATCHES regexes / IN lists / EXPR calls, tag-layer quote stripping, and
    /// @expect/@fail binding assembly (numbered pairs, WARN default, hard errors).
    /// </summary>
    public class ColumnRuleParserTests
    {
        // ── Individual rule forms ──────────────────────────────────────────

        [Fact]
        public void Parses_NotNull_And_Unique()
        {
            var rules = ColumnRuleParser.Parse("'NOT NULL, UNIQUE'");

            Assert.Collection(rules,
                r => Assert.IsType<NotNullRule>(r),
                r =>
                {
                    var unique = Assert.IsType<UniqueRule>(r);
                    Assert.Equal(UniqueMode.All, unique.Mode);
                    Assert.Null(unique.OrderKey);
                    Assert.Null(unique.CompositeColumns);
                });
        }

        [Fact]
        public void Parses_NotBlank()
        {
            var rules = ColumnRuleParser.Parse("'NOT NULL, NOT BLANK'");

            Assert.Collection(rules,
                r => Assert.IsType<NotNullRule>(r),
                r => Assert.Equal("NOT BLANK", Assert.IsType<NotBlankRule>(r).Text));
        }

        [Theory]
        // Every form lowers onto one inclusive range, so > and < shift the bound by one.
        [InlineData("LENGTH BETWEEN 5 AND 10", 5, 10)]
        [InlineData("LENGTH >= 5", 5, null)]
        [InlineData("LENGTH > 5", 6, null)]
        [InlineData("LENGTH <= 10", 0, 10)]
        [InlineData("LENGTH < 10", 0, 9)]
        [InlineData("LENGTH = 5", 5, 5)]
        [InlineData("length between 0 and 0", 0, 0)]
        public void Parses_Length_OntoAnInclusiveRange(string text, int expectedMin, int? expectedMax)
        {
            var rule = Assert.IsType<LengthRule>(ColumnRuleParser.Parse($"'{text}'").Single());

            Assert.Equal(expectedMin, rule.MinLength);
            Assert.Equal(expectedMax, rule.MaxLength);
            Assert.Equal(text, rule.Text);
        }

        [Theory]
        [InlineData("LENGTH BETWEEN 10 AND 5")]  // no value can satisfy it
        [InlineData("LENGTH < 0")]               // nothing is shorter than zero characters
        [InlineData("LENGTH >= -1")]
        [InlineData("LENGTH >= 2.5")]
        [InlineData("LENGTH BETWEEN 5")]
        [InlineData("LENGTH")]
        public void Length_Unsatisfiable_Or_Malformed_IsHardError(string text)
        {
            Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse($"'{text}'"));
        }

        [Fact]
        public void Parses_Castable_WithAndWithoutADeclaredWidth()
        {
            var date = Assert.IsType<CastableRule>(ColumnRuleParser.Parse("'CASTABLE AS DATE'").Single());
            Assert.Equal("DATE", date.BaseType);
            Assert.Equal("DATE", date.DeclaredType);
            Assert.Null(date.Precision);
            Assert.Null(date.Scale);

            var money = Assert.IsType<CastableRule>(
                ColumnRuleParser.Parse("'CASTABLE AS DECIMAL(18,2)'").Single());
            Assert.Equal("DECIMAL", money.BaseType);
            Assert.Equal("DECIMAL(18,2)", money.DeclaredType);
            Assert.Equal(18, money.Precision);
            Assert.Equal(2, money.Scale);

            var name = Assert.IsType<CastableRule>(
                ColumnRuleParser.Parse("'castable as varchar(50)'").Single());
            Assert.Equal("VARCHAR", name.BaseType);
            Assert.Equal(50, name.Precision);
            Assert.Null(name.Scale);
        }

        [Fact]
        public void Castable_UnknownType_IsHardError()
        {
            // An unregistered type makes the shared cast a no-op, so the rule would accept every
            // value. Catching it at parse time is the difference between a rule and a decoration.
            var ex = Assert.Throws<ColumnRuleParseException>(
                () => ColumnRuleParser.Parse("'CASTABLE AS BANANA'"));

            Assert.Contains("BANANA", ex.Message);
            Assert.Contains("every value", ex.Message);
        }

        [Theory]
        [InlineData("CASTABLE AS DECIMAL(2,5)")]  // more decimals than total digits
        [InlineData("CASTABLE AS VARCHAR(0)")]
        [InlineData("CASTABLE AS")]
        [InlineData("CASTABLE DATE")]
        public void Castable_Malformed_Or_Unsatisfiable_IsHardError(string text)
        {
            Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse($"'{text}'"));
        }

        [Fact]
        public void Parses_UniqueWith_CompositeTuple()
        {
            var rule = Assert.IsType<UniqueRule>(ColumnRuleParser.Parse("'UNIQUE WITH (TenantId, Region)'").Single());

            Assert.Equal(UniqueMode.All, rule.Mode);
            Assert.Equal(new[] { "TenantId", "Region" }, rule.CompositeColumns);
        }

        [Fact]
        public void Parses_UniqueFirst_WithByKey()
        {
            var rule = Assert.IsType<UniqueRule>(ColumnRuleParser.Parse("'UNIQUE_FIRST BY LoadedAt'").Single());

            Assert.Equal(UniqueMode.First, rule.Mode);
            Assert.NotNull(rule.OrderKey);
            Assert.Equal("UNIQUE_FIRST BY LoadedAt", rule.Text);
        }

        [Fact]
        public void UniqueFirst_WithoutBy_IsHardError()
        {
            var ex = Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse("'UNIQUE_FIRST'"));
            Assert.Contains("BY", ex.Message);

            Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse("'UNIQUE_LAST'"));
        }

        [Fact]
        public void Parses_Matches_WithCommasInsideRegex()
        {
            // Commas inside {n,m} braces and character classes are literal, not rule separators.
            var rules = ColumnRuleParser.Parse(@"'MATCHES ^[a-z,;]{1,10}$, NOT NULL'");

            Assert.Equal(2, rules.Count);
            var matches = Assert.IsType<MatchesRule>(rules[0]);
            Assert.Equal(@"^[a-z,;]{1,10}$", matches.Pattern);
            Assert.IsType<NotNullRule>(rules[1]);
        }

        [Fact]
        public void Parses_Matches_EmailRegex_WithAtAndBackslash()
        {
            var matches = Assert.IsType<MatchesRule>(
                ColumnRuleParser.Parse(@"'MATCHES ^[^@]+@[^@]+\.com$'").Single());

            Assert.Equal(@"^[^@]+@[^@]+\.com$", matches.Pattern);
            Assert.Matches(matches.Compile(caseSensitive: true), "user@example.com");
        }

        [Fact]
        public void Matches_NonBacktrackingIncompatible_Backreference_IsHardError()
        {
            var ex = Assert.Throws<ColumnRuleParseException>(() =>
                ColumnRuleParser.Parse(@"'MATCHES ^(a)\1$'"));
            Assert.Contains("NonBacktracking", ex.Message);
        }

        [Fact]
        public void Matches_NonBacktrackingIncompatible_Lookahead_IsHardError()
        {
            Assert.Throws<ColumnRuleParseException>(() =>
                ColumnRuleParser.Parse(@"'MATCHES ^(?=a).*$'"));
        }

        [Fact]
        public void Parses_InList_WithStringsAndNumbers()
        {
            var rule = Assert.IsType<InListRule>(
                ColumnRuleParser.Parse("\"IN ('NA','EMEA','APAC')\"").Single());
            Assert.Equal(new object?[] { "NA", "EMEA", "APAC" }, rule.Values);

            var numeric = Assert.IsType<InListRule>(ColumnRuleParser.Parse("'IN (1, 2, -3)'").Single());
            Assert.Equal(new object?[] { 1m, 2m, -3m }, numeric.Values);
        }

        [Fact]
        public void Parses_InList_CommasInsideListDoNotSplitRules()
        {
            var rules = ColumnRuleParser.Parse("\"NOT NULL, IN ('a,b', 'c')\"");

            Assert.Equal(2, rules.Count);
            var inList = Assert.IsType<InListRule>(rules[1]);
            Assert.Equal(new object?[] { "a,b", "c" }, inList.Values);
        }

        [Fact]
        public void InList_WithNull_IsHardError()
        {
            Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse("'IN (NULL)'"));
        }

        [Fact]
        public void Parses_ExistsIn_TableAndKeyColumn()
        {
            var rule = Assert.IsType<ExistsInRule>(
                ColumnRuleParser.Parse("'EXISTS IN dim_region(Id)'").Single());

            Assert.Equal("dim_region", rule.Table);
            Assert.Equal(new[] { "Id" }, rule.KeyColumns);
            Assert.Null(rule.SourceColumns);
            Assert.False(rule.IsComposite);

            var temp = Assert.IsType<ExistsInRule>(ColumnRuleParser.Parse("'EXISTS IN #ref(Code)'").Single());
            Assert.Equal("#ref", temp.Table);
        }

        [Fact]
        public void ExistsIn_Malformed_IsHardError()
        {
            Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse("'EXISTS IN dim_region'"));
        }

        [Fact]
        public void Parses_ExistsWith_CompositeTuple()
        {
            var rule = Assert.IsType<ExistsInRule>(
                ColumnRuleParser.Parse("'EXISTS WITH (TenantId, CustomerId) IN dim_customer(TenantId, CustomerId)'")
                    .Single());

            Assert.True(rule.IsComposite);
            Assert.Equal("dim_customer", rule.Table);
            Assert.Equal(new[] { "TenantId", "CustomerId" }, rule.SourceColumns);
            Assert.Equal(new[] { "TenantId", "CustomerId" }, rule.KeyColumns);
        }

        [Fact]
        public void ExistsWith_MapsProbeColumnsOntoDifferentlyNamedReferenceColumns()
        {
            // The two tuples pair positionally, so the reference table's columns need not share
            // the source's names.
            var rule = Assert.IsType<ExistsInRule>(
                ColumnRuleParser.Parse("'EXISTS WITH (TenantId, CustomerId) IN dim_customer(Tenant, Id)'").Single());

            Assert.Equal(new[] { "TenantId", "CustomerId" }, rule.SourceColumns);
            Assert.Equal(new[] { "Tenant", "Id" }, rule.KeyColumns);
        }

        [Fact]
        public void ExistsWith_ArityMismatch_IsHardError()
        {
            var ex = Assert.Throws<ColumnRuleParseException>(() =>
                ColumnRuleParser.Parse("'EXISTS WITH (TenantId, CustomerId) IN dim_customer(Id)'"));

            Assert.Contains("arity", ex.Message);
        }

        [Fact]
        public void ExistsWith_NonIdentifierColumn_IsHardError()
        {
            // An expression here cannot be reproduced by the reference-table read that builds the
            // key set, so it is rejected rather than silently treated as a column name.
            Assert.Throws<ColumnRuleParseException>(() =>
                ColumnRuleParser.Parse("'EXISTS WITH (UPPER(TenantId)) IN dim_customer(TenantId)'"));

            Assert.Throws<ColumnRuleParseException>(() =>
                ColumnRuleParser.Parse("'EXISTS WITH (TenantId, ) IN dim_customer(TenantId, Id)'"));
        }

        [Fact]
        public void ExistsWith_Malformed_ReportsBothSupportedForms()
        {
            var ex = Assert.Throws<ColumnRuleParseException>(() =>
                ColumnRuleParser.Parse("'EXISTS WITH (TenantId) dim_customer(TenantId)'"));

            Assert.Contains("EXISTS IN table(KeyColumn)", ex.Message);
            Assert.Contains("EXISTS WITH", ex.Message);
        }

        [Fact]
        public void Parses_Expr_CrossColumnPredicate_WithFunctionCallCommas()
        {
            var rules = ColumnRuleParser.Parse("'EXPR StartDate <= EndDate, NOT NULL'");
            Assert.Equal(2, rules.Count);
            Assert.IsType<ExprRule>(rules[0]);

            // Commas inside a function call stay inside one EXPR rule.
            var withCall = ColumnRuleParser.Parse("'EXPR COALESCE(EndDate, StartDate) >= StartDate'");
            Assert.IsType<ExprRule>(Assert.Single(withCall));
        }

        [Fact]
        public void Expr_InvalidSql_IsHardError()
        {
            Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse("'EXPR >>>'"));
        }

        [Theory]
        [InlineData("'>= 0'", CompareOp.GreaterOrEqual, 0)]
        [InlineData("'<= 120'", CompareOp.LessOrEqual, 120)]
        [InlineData("'> -1.5'", CompareOp.Greater, -1.5)]
        [InlineData("'< 100'", CompareOp.Less, 100)]
        [InlineData("'= 42'", CompareOp.Equal, 42)]
        public void Parses_NumericComparisons_AsDecimal(string expect, CompareOp op, double bound)
        {
            var rule = Assert.IsType<ComparisonRule>(ColumnRuleParser.Parse(expect).Single());

            Assert.Equal(op, rule.Op);
            Assert.Equal((decimal)bound, rule.Value);
        }

        [Fact]
        public void Comparison_NonNumericBound_IsHardError()
        {
            Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse("'>= abc'"));
        }

        [Theory]
        [InlineData("'FROBNICATE'")]
        [InlineData("''")]
        [InlineData("'NOT NULL,,UNIQUE'")]
        public void MalformedRules_AreHardErrors_NeverSilentlyIgnored(string expect)
        {
            Assert.Throws<ColumnRuleParseException>(() => ColumnRuleParser.Parse(expect));
        }

        // ── Quote stripping (tag layer preserves outer quotes) ────────────

        [Fact]
        public void Unquote_StripsOuterQuotes_AndUnescapesDoubledQuotes()
        {
            Assert.Equal("NOT NULL", ColumnRuleParser.Unquote("'NOT NULL'"));
            Assert.Equal("IN ('NA','EMEA')", ColumnRuleParser.Unquote("\"IN ('NA','EMEA')\""));
            Assert.Equal("IN ('NA')", ColumnRuleParser.Unquote("'IN (''NA'')'"));
            Assert.Equal("bare", ColumnRuleParser.Unquote("  bare  "));
        }

        // ── @expect/@fail binding assembly ─────────────────────────────────

        [Fact]
        public void ParseBindings_PairsNumberedSuffixes_InOrder()
        {
            var metadata = Metadata(
                ("expect", "'NOT NULL'"), ("fail", "'THROW'"),
                ("expect_1", "'UNIQUE'"), ("fail_1", "'QUARANTINE'"),
                ("owner", "steward@example.com")); // unrelated tags ignored

            var bindings = ColumnRuleParser.ParseBindings(metadata);

            Assert.Collection(bindings,
                b =>
                {
                    Assert.Equal("expect", b.ExpectKey);
                    Assert.Equal(FailAction.Throw, b.Action);
                    Assert.IsType<NotNullRule>(b.Rules.Single());
                },
                b =>
                {
                    Assert.Equal("expect_1", b.ExpectKey);
                    Assert.Equal(FailAction.Quarantine, b.Action);
                    Assert.IsType<UniqueRule>(b.Rules.Single());
                });
        }

        [Fact]
        public void ParseBindings_MissingFail_DefaultsToWarn_FailSafeNotSilent()
        {
            var binding = ColumnRuleParser.ParseBindings(Metadata(("expect", "'>= 0'"))).Single();

            Assert.Equal(FailAction.Warn, binding.Action);
            Assert.False(binding.ActionExplicit);
        }

        [Fact]
        public void ParseBindings_FailWithoutExpect_IsHardError()
        {
            var ex = Assert.Throws<ColumnRuleParseException>(() =>
                ColumnRuleParser.ParseBindings(Metadata(("fail_1", "'THROW'"))));
            Assert.Contains("expect_1", ex.Message);
        }

        [Fact]
        public void ParseBindings_UnknownAction_IsHardError()
        {
            var ex = Assert.Throws<ColumnRuleParseException>(() =>
                ColumnRuleParser.ParseBindings(Metadata(("expect", "'NOT NULL'"), ("fail", "'EXPLODE'"))));
            Assert.Contains("THROW, WARN, QUARANTINE", ex.Message);
        }

        [Fact]
        public void ParseBindings_NoRuleTags_ReturnsEmpty()
        {
            Assert.Empty(ColumnRuleParser.ParseBindings(Metadata(("owner", "Bob"), ("pii", "true"))));
            Assert.False(ColumnRuleParser.HasRuleTags(Metadata(("owner", "Bob"))));
            Assert.True(ColumnRuleParser.HasRuleTags(Metadata(("EXPECT_2", "'NOT NULL'"))));
        }

        // ── End-to-end through the comment-tag pipeline ────────────────────

        [Fact]
        public void RuleTags_FlowFromScriptCommentToBindings()
        {
            var source = "SELECT Email /* @expect: 'MATCHES ^[^@]+@[^@]+$, NOT NULL'; @fail: 'QUARANTINE'; */ FROM src;";
            var script = new ETL_SQL.Core.Parser.Parser(new ETL_SQL.Core.Parser.Lexer(source).Tokenize(), source).Parse();
            var column = ((SelectStatement)script.Statements[0]).Columns[0];

            var binding = ColumnRuleParser.ParseBindings(column.Metadata!).Single();

            Assert.Equal(FailAction.Quarantine, binding.Action);
            Assert.Equal(2, binding.Rules.Count);
            Assert.IsType<MatchesRule>(binding.Rules[0]);
            Assert.IsType<NotNullRule>(binding.Rules[1]);
        }

        private static Dictionary<string, string> Metadata(params (string Key, string Value)[] entries)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in entries) metadata[key] = value;
            return metadata;
        }
    }
}
