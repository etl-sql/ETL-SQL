using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Quality;
using Xunit;

namespace ETL_SQL.Tests.Core.Quality
{
    /// <summary>
    /// The <c>EXPECT</c> column-rule grammar, parsed from real script text: every rule form, the
    /// clause's action, repetition, and the boundaries that keep a rule from swallowing the rest of
    /// the select list. Rules are grammar, so a malformed one is a <see cref="SyntaxException"/>
    /// with a position — not a deferred lint finding and not a silently dropped rule.
    /// </summary>
    public class ColumnRuleParserTests
    {
        // ── Individual rule forms ──────────────────────────────────────────

        [Fact]
        public void Parses_NotNull_And_Unique()
        {
            var rules = ParseRules("NOT NULL AND UNIQUE");

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
            var rules = ParseRules("NOT NULL AND NOT BLANK");

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
            var rule = Assert.IsType<LengthRule>(ParseRule(text));

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
            Assert.Throws<SyntaxException>(() => ParseRules(text));
        }

        [Fact]
        public void Parses_Castable_WithAndWithoutADeclaredWidth()
        {
            var date = Assert.IsType<CastableRule>(ParseRule("CASTABLE AS DATE"));
            Assert.Equal("DATE", date.BaseType);
            Assert.Equal("DATE", date.DeclaredType);
            Assert.Null(date.Precision);
            Assert.Null(date.Scale);

            var money = Assert.IsType<CastableRule>(ParseRule("CASTABLE AS DECIMAL(18,2)"));
            Assert.Equal("DECIMAL", money.BaseType);
            Assert.Equal("DECIMAL(18,2)", money.DeclaredType);
            Assert.Equal(18, money.Precision);
            Assert.Equal(2, money.Scale);

            var name = Assert.IsType<CastableRule>(ParseRule("castable as varchar(50)"));
            Assert.Equal("VARCHAR", name.BaseType);
            Assert.Equal(50, name.Precision);
            Assert.Null(name.Scale);
        }

        [Fact]
        public void Castable_UnknownType_IsHardError()
        {
            // An unregistered type makes the shared cast a no-op, so the rule would accept every
            // value. Catching it at parse time is the difference between a rule and a decoration.
            var ex = Assert.Throws<SyntaxException>(() => ParseRules("CASTABLE AS BANANA"));

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
            Assert.Throws<SyntaxException>(() => ParseRules(text));
        }

        [Fact]
        public void Parses_NegatedMembershipAndPattern()
        {
            var notIn = Assert.IsType<InListRule>(ParseRule("NOT IN ('UNKNOWN', 'N/A')"));
            Assert.True(notIn.Negated);
            Assert.Equal(new object?[] { "UNKNOWN", "N/A" }, notIn.Values);

            var notMatches = Assert.IsType<MatchesRule>(ParseRule("NOT MATCHES '<script[^>]*>'"));
            Assert.True(notMatches.Negated);
            Assert.Equal("<script[^>]*>", notMatches.Pattern);
            Assert.Equal("NOT MATCHES '<script[^>]*>'", notMatches.Text);
        }

        [Fact]
        public void PositiveFormsStayUnnegated()
        {
            Assert.False(Assert.IsType<InListRule>(ParseRule("IN ('NA')")).Negated);
            Assert.False(Assert.IsType<MatchesRule>(ParseRule("MATCHES '^a+$'")).Negated);
        }

        [Fact]
        public void NegatedForms_RejectWhatThePositiveFormsReject()
        {
            // Both directions run through one parser, so an invalid pattern or list is invalid
            // either way rather than only when written positively.
            Assert.Throws<SyntaxException>(() => ParseRules(@"NOT MATCHES '(a)\1'"));
            Assert.Throws<SyntaxException>(() => ParseRules("NOT IN (NULL)"));
            Assert.Throws<SyntaxException>(() => ParseRules("NOT MATCHES"));
        }

        [Fact]
        public void NotIn_DoesNotSwallowExistsIn()
        {
            // 'NOT' only negates when NULL, BLANK, MATCHES, or IN follows immediately.
            var exists = Assert.IsType<ExistsInRule>(ParseRule("EXISTS IN dim_region(Id)"));
            Assert.Equal("dim_region", exists.Table);
        }

        [Fact]
        public void Parses_Between_WithExpressionBounds()
        {
            var rule = Assert.IsType<BetweenRule>(
                ParseRule("BETWEEN DATEADD(DAY, -30, @RunDate) AND @RunDate"));

            Assert.NotNull(rule.Lower);
            Assert.NotNull(rule.Upper);
            Assert.Equal("BETWEEN DATEADD(DAY, -30, @RunDate) AND @RunDate", rule.Text);
        }

        [Fact]
        public void Between_BoundsCannotSwallowTheSeparatingAnd()
        {
            // Bounds parse at additive precedence — the level SQL's own BETWEEN uses — so the AND
            // between them is always the separator and never a boolean operator.
            var rule = Assert.IsType<BetweenRule>(ParseRule("BETWEEN IIF(1 = 1 AND 2 = 2, 5, 10) AND 100"));

            Assert.NotNull(rule.Lower);
            Assert.NotNull(rule.Upper);
        }

        [Fact]
        public void Between_CombinesWithOtherRules_ViaAnd()
        {
            var rules = ParseRules("NOT NULL AND BETWEEN 1 AND 10");

            Assert.Collection(rules,
                r => Assert.IsType<NotNullRule>(r),
                r => Assert.IsType<BetweenRule>(r));
        }

        [Theory]
        [InlineData("BETWEEN 1")]
        [InlineData("BETWEEN AND 10")]
        public void Between_Malformed_IsHardError(string text)
        {
            Assert.Throws<SyntaxException>(() => ParseRules(text));
        }

        [Fact]
        public void Between_MissingUpperBound_IsHardError()
        {
            Assert.Throws<SyntaxException>(() => ParseStatement("SELECT c EXPECT BETWEEN 1 AND;"));
        }

        [Fact]
        public void Parses_UniqueWith_CompositeTuple()
        {
            var rule = Assert.IsType<UniqueRule>(ParseRule("UNIQUE WITH (TenantId, Region)"));

            Assert.Equal(UniqueMode.All, rule.Mode);
            Assert.Equal(new[] { "TenantId", "Region" }, rule.CompositeColumns);
        }

        [Fact]
        public void Parses_UniqueFirst_WithByKey()
        {
            var rule = Assert.IsType<UniqueRule>(ParseRule("UNIQUE_FIRST BY LoadedAt"));

            Assert.Equal(UniqueMode.First, rule.Mode);
            Assert.NotNull(rule.OrderKey);
            Assert.Equal("UNIQUE_FIRST BY LoadedAt", rule.Text);
        }

        [Fact]
        public void UniqueFirst_WithoutBy_IsHardError()
        {
            var ex = Assert.Throws<SyntaxException>(() => ParseRules("UNIQUE_FIRST"));
            Assert.Contains("BY", ex.Message);

            Assert.Throws<SyntaxException>(() => ParseRules("UNIQUE_LAST"));
        }

        [Fact]
        public void Parses_Matches_WithCommasInsideRegex()
        {
            // The pattern is a string literal, so commas, braces, and classes inside it are the
            // lexer's business and never reach the rule grammar.
            var rules = ParseRules(@"MATCHES '^[a-z,;]{1,10}$' AND NOT NULL");

            Assert.Equal(2, rules.Count);
            var matches = Assert.IsType<MatchesRule>(rules[0]);
            Assert.Equal(@"^[a-z,;]{1,10}$", matches.Pattern);
            Assert.IsType<NotNullRule>(rules[1]);
        }

        [Fact]
        public void Parses_Matches_EmailRegex_WithAtAndBackslash()
        {
            // '@' would lex as a variable and the backslash would need escaping if the pattern were
            // bare — quoting is what makes an arbitrary regex expressible at all.
            var matches = Assert.IsType<MatchesRule>(ParseRule(@"MATCHES '^[^@]+@[^@]+\.com$'"));

            Assert.Equal(@"^[^@]+@[^@]+\.com$", matches.Pattern);
            Assert.Matches(matches.Compile(caseSensitive: true), "user@example.com");
        }

        [Fact]
        public void Matches_UnquotedPattern_IsHardError_WithGuidance()
        {
            var ex = Assert.Throws<SyntaxException>(() => ParseRules("MATCHES ^a+$"));
            Assert.Contains("quoted pattern", ex.Message);
        }

        [Fact]
        public void Matches_NonBacktrackingIncompatible_Backreference_IsHardError()
        {
            var ex = Assert.Throws<SyntaxException>(() => ParseRules(@"MATCHES '^(a)\1$'"));
            Assert.Contains("NonBacktracking", ex.Message);
        }

        [Fact]
        public void Matches_NonBacktrackingIncompatible_Lookahead_IsHardError()
        {
            Assert.Throws<SyntaxException>(() => ParseRules(@"MATCHES '^(?=a).*$'"));
        }

        [Fact]
        public void Parses_InList_WithStringsAndNumbers()
        {
            // No outer quoting, so no SQL-style quote doubling: the list is written once.
            var rule = Assert.IsType<InListRule>(ParseRule("IN ('NA','EMEA','APAC')"));
            Assert.Equal(new object?[] { "NA", "EMEA", "APAC" }, rule.Values);

            var numeric = Assert.IsType<InListRule>(ParseRule("IN (1, 2, -3)"));
            Assert.Equal(new object?[] { 1m, 2m, -3m }, numeric.Values);
        }

        [Fact]
        public void Parses_InList_CommasInsideListDoNotEndTheColumn()
        {
            var rules = ParseRules("NOT NULL AND IN ('a,b', 'c')");

            Assert.Equal(2, rules.Count);
            var inList = Assert.IsType<InListRule>(rules[1]);
            Assert.Equal(new object?[] { "a,b", "c" }, inList.Values);
        }

        [Fact]
        public void InList_WithNull_IsHardError()
        {
            Assert.Throws<SyntaxException>(() => ParseRules("IN (NULL)"));
        }

        [Fact]
        public void Parses_ExistsIn_TableAndKeyColumn()
        {
            var rule = Assert.IsType<ExistsInRule>(ParseRule("EXISTS IN dim_region(Id)"));

            Assert.Equal("dim_region", rule.Table);
            Assert.Equal(new[] { "Id" }, rule.KeyColumns);
            Assert.Null(rule.SourceColumns);
            Assert.False(rule.IsComposite);

            var temp = Assert.IsType<ExistsInRule>(ParseRule("EXISTS IN #ref(Code)"));
            Assert.Equal("#ref", temp.Table);
        }

        [Fact]
        public void ExistsIn_Malformed_IsHardError()
        {
            Assert.Throws<SyntaxException>(() => ParseRules("EXISTS IN dim_region"));
        }

        [Fact]
        public void Parses_ExistsWith_CompositeTuple()
        {
            var rule = Assert.IsType<ExistsInRule>(
                ParseRule("EXISTS WITH (TenantId, CustomerId) IN dim_customer(TenantId, CustomerId)"));

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
                ParseRule("EXISTS WITH (TenantId, CustomerId) IN dim_customer(Tenant, Id)"));

            Assert.Equal(new[] { "TenantId", "CustomerId" }, rule.SourceColumns);
            Assert.Equal(new[] { "Tenant", "Id" }, rule.KeyColumns);
        }

        [Fact]
        public void ExistsWith_ArityMismatch_IsHardError()
        {
            var ex = Assert.Throws<SyntaxException>(() =>
                ParseRules("EXISTS WITH (TenantId, CustomerId) IN dim_customer(Id)"));

            Assert.Contains("arity", ex.Message);
        }

        [Fact]
        public void ExistsWith_NonIdentifierColumn_IsHardError()
        {
            // An expression here cannot be reproduced by the reference-table read that builds the
            // key set, so it is rejected rather than silently treated as a column name.
            Assert.Throws<SyntaxException>(() =>
                ParseRules("EXISTS WITH (UPPER(TenantId)) IN dim_customer(TenantId)"));

            Assert.Throws<SyntaxException>(() =>
                ParseRules("EXISTS WITH (TenantId, ) IN dim_customer(TenantId, Id)"));
        }

        [Fact]
        public void ExistsWith_Malformed_ReportsBothSupportedForms()
        {
            var ex = Assert.Throws<SyntaxException>(() =>
                ParseRules("EXISTS WITH (TenantId) dim_customer(TenantId)"));

            Assert.Contains("IN <table>(cols)", ex.Message);
        }

        [Fact]
        public void Parses_Expr_CrossColumnPredicate_WithFunctionCallCommas()
        {
            var rules = ParseRules("EXPR StartDate <= EndDate AND NOT NULL");
            Assert.Equal(2, rules.Count);
            Assert.IsType<ExprRule>(rules[0]);
            Assert.IsType<NotNullRule>(rules[1]);

            // Commas inside a function call stay inside one EXPR rule.
            Assert.IsType<ExprRule>(ParseRule("EXPR COALESCE(EndDate, StartDate) >= StartDate"));
        }

        [Fact]
        public void Expr_ParsesAtComparisonPrecedence_SoTheNextRuleSurvives()
        {
            // A predicate that consumed the AND would swallow the rule after it. Parenthesize to
            // put AND/OR inside one predicate.
            var compound = Assert.IsType<ExprRule>(ParseRule("EXPR (StartDate <= EndDate AND Qty > 0)"));
            Assert.NotNull(compound.Predicate);
        }

        [Fact]
        public void Expr_InvalidSql_IsHardError()
        {
            Assert.Throws<SyntaxException>(() => ParseRules("EXPR >>>"));
        }

        [Theory]
        [InlineData(">= 0", CompareOp.GreaterOrEqual, 0)]
        [InlineData("<= 120", CompareOp.LessOrEqual, 120)]
        [InlineData("> -1.5", CompareOp.Greater, -1.5)]
        [InlineData("< 100", CompareOp.Less, 100)]
        [InlineData("= 42", CompareOp.Equal, 42)]
        public void Parses_NumericComparisons_AsDecimal(string text, CompareOp op, double bound)
        {
            var rule = Assert.IsType<ComparisonRule>(ParseRule(text));

            Assert.Equal(op, rule.Op);
            Assert.Equal((decimal)bound, rule.Value);
        }

        [Fact]
        public void Comparison_NonNumericBound_IsHardError()
        {
            var ex = Assert.Throws<SyntaxException>(() => ParseRules(">= abc"));
            Assert.Contains("EXPR", ex.Message); // points at the rule that does take an expression
        }

        [Theory]
        [InlineData("FROBNICATE")]
        [InlineData("NOT NULL AND AND UNIQUE")]
        public void MalformedRules_AreHardErrors_NeverSilentlyIgnored(string text)
        {
            Assert.Throws<SyntaxException>(() => ParseRules(text));
        }

        [Fact]
        public void EmptyClause_IsHardError()
        {
            Assert.Throws<SyntaxException>(() => ParseColumn("c EXPECT"));
        }

        // ── Clause shape: action, repetition, boundaries ────────────────────

        [Fact]
        public void OmittedAction_DefaultsToWarn_FailSafeNotSilent()
        {
            var clause = Assert.Single(ParseColumn("c EXPECT >= 0").Expectations!);

            Assert.Equal(FailAction.Warn, clause.Action);
            Assert.False(clause.ActionExplicit);
        }

        [Theory]
        [InlineData("THROW", FailAction.Throw)]
        [InlineData("WARN", FailAction.Warn)]
        [InlineData("QUARANTINE", FailAction.Quarantine)]
        public void OnFailure_SelectsTheAction(string word, FailAction expected)
        {
            var clause = Assert.Single(ParseColumn($"c EXPECT NOT NULL ON FAILURE {word}").Expectations!);

            Assert.Equal(expected, clause.Action);
            Assert.True(clause.ActionExplicit);
        }

        [Fact]
        public void RepeatedClauses_ReplaceTheNumberedTagPairing()
        {
            var column = ParseColumn("UserId EXPECT NOT NULL ON FAILURE THROW EXPECT UNIQUE ON FAILURE QUARANTINE");

            Assert.Collection(column.Expectations!,
                c =>
                {
                    Assert.IsType<NotNullRule>(c.Rules.Single());
                    Assert.Equal(FailAction.Throw, c.Action);
                },
                c =>
                {
                    Assert.IsType<UniqueRule>(c.Rules.Single());
                    Assert.Equal(FailAction.Quarantine, c.Action);
                });
        }

        [Fact]
        public void Notify_IsRejectedOnAColumn_WithAPointerToAssertJob()
        {
            var ex = Assert.Throws<SyntaxException>(
                () => ParseColumn("c EXPECT NOT NULL ON FAILURE NOTIFY alerts"));

            Assert.Contains("ASSERT JOB", ex.Message);
        }

        [Fact]
        public void ColumnLevelAction_TakesNoTargetOrOptions()
        {
            // Routing is declared once per statement; a per-column target would let two columns
            // disagree about where the same run's rows land.
            var target = Assert.Throws<SyntaxException>(
                () => ParseColumn("c EXPECT NOT NULL ON FAILURE QUARANTINE TO q"));
            Assert.Contains("ON FAILURE QUARANTINE TO <table>", target.Message);

            var options = Assert.Throws<SyntaxException>(
                () => ParseColumn("c EXPECT NOT NULL ON FAILURE WARN WITH (RETENTION = '30 DAYS')"));
            Assert.Contains("RETENTION", options.Message);
        }

        [Fact]
        public void ClauseFollowsAnAlias_AndNeedsNoAs()
        {
            var aliased = ParseColumn("RawEmail AS Email EXPECT NOT NULL");
            Assert.Equal("Email", aliased.Alias);
            Assert.Single(aliased.Expectations!);

            // EXPECT is a reserved token, so it can never be swallowed as an implicit alias.
            var bare = ParseColumn("Email EXPECT NOT NULL");
            Assert.Null(bare.Alias);
            Assert.Single(bare.Expectations!);
        }

        [Fact]
        public void TopLevelComma_EndsTheColumn_NotTheRuleList()
        {
            var sql = "SELECT a EXPECT NOT NULL, b EXPECT UNIQUE FROM src;";
            var columns = ((SelectStatement)ParseStatement(sql)).Columns;

            Assert.Equal(2, columns.Count);
            Assert.IsType<NotNullRule>(Assert.Single(columns[0].Expectations!).Rules.Single());
            Assert.IsType<UniqueRule>(Assert.Single(columns[1].Expectations!).Rules.Single());
        }

        [Fact]
        public void DescriptiveTagsStillAttach_AlongsideRules()
        {
            // Comments keep describing; only enforcement moved into the grammar.
            var column = ParseColumn("Email EXPECT NOT NULL ON FAILURE THROW /* @d: primary contact; @pii: true; */");

            Assert.Equal("primary contact", column.Description);
            Assert.Equal("true", column.Metadata["pii"]);
            Assert.Equal(FailAction.Throw, Assert.Single(column.Expectations!).Action);
        }

        [Fact]
        public void ExpectSchema_IsNotMistakenForAColumnRule()
        {
            var ex = Assert.Throws<SyntaxException>(() => ParseColumn("c EXPECT SCHEMA target (a INT)"));
            Assert.Contains("EXPECT SCHEMA is a statement", ex.Message);
        }

        // ── Compound rules (AND / OR / parentheses) ─────────────────────────

        [Fact]
        public void TopLevelAnd_UnrollsIntoIndependentRules()
        {
            // Each conjunct reports its own failures, which is what the comma form used to give.
            var rules = ParseRules("NOT NULL AND > 0");

            Assert.Collection(rules,
                r => Assert.IsType<NotNullRule>(r),
                r => Assert.IsType<ComparisonRule>(r));
        }

        [Fact]
        public void Parse_CompoundOrRule_ReturnsOrRuleWithOperands()
        {
            var rule = Assert.IsType<OrRule>(ParseRule("> 100 OR < 10"));

            Assert.Equal(2, rule.Operands.Count);
            Assert.IsType<ComparisonRule>(rule.Operands[0]);
            Assert.IsType<ComparisonRule>(rule.Operands[1]);
        }

        [Fact]
        public void Parse_Precedence_AndBindsTighterThanOr()
        {
            var orRule = Assert.IsType<OrRule>(ParseRule("> 0 AND < 10 OR > 100 AND < 200"));
            Assert.Equal(2, orRule.Operands.Count);

            var and1 = Assert.IsType<AndRule>(orRule.Operands[0]);
            Assert.Equal(CompareOp.Greater, Assert.IsType<ComparisonRule>(and1.Operands[0]).Op);
            Assert.Equal(CompareOp.Less, Assert.IsType<ComparisonRule>(and1.Operands[1]).Op);

            var and2 = Assert.IsType<AndRule>(orRule.Operands[1]);
            Assert.Equal(CompareOp.Greater, Assert.IsType<ComparisonRule>(and2.Operands[0]).Op);
            Assert.Equal(CompareOp.Less, Assert.IsType<ComparisonRule>(and2.Operands[1]).Op);
        }

        [Fact]
        public void Parse_Parentheses_OverridePrecedence()
        {
            var rules = ParseRules("NOT NULL AND (= 1 OR = 2)");

            Assert.Collection(rules,
                r => Assert.IsType<NotNullRule>(r),
                r =>
                {
                    var orRule = Assert.IsType<OrRule>(r);
                    Assert.Equal(2, orRule.Operands.Count);
                });
        }

        [Fact]
        public void Parse_BetweenAndLengthBetween_ConsumeTheirOwnAnd()
        {
            var orRule = Assert.IsType<OrRule>(ParseRule("BETWEEN 1 AND 10 OR BETWEEN 20 AND 30"));
            Assert.Equal(2, orRule.Operands.Count);
            Assert.IsType<BetweenRule>(orRule.Operands[0]);
            Assert.IsType<BetweenRule>(orRule.Operands[1]);

            var lengthRules = ParseRules("LENGTH BETWEEN 5 AND 10 AND NOT NULL");
            Assert.Collection(lengthRules,
                r => Assert.IsType<LengthRule>(r),
                r => Assert.IsType<NotNullRule>(r));
        }

        [Fact]
        public void Parse_FlattenAll_FlattensNestedTree()
        {
            var rules = ParseRules(@"NOT NULL AND (LENGTH BETWEEN 5 AND 10 OR MATCHES '^LEGACY-')");
            var flattened = rules.FlattenAll().ToList();

            // NotNullRule, OrRule, LengthRule, MatchesRule — the top-level AND is already unrolled.
            Assert.Equal(4, flattened.Count);
            Assert.Contains(flattened, r => r is NotNullRule);
            Assert.Contains(flattened, r => r is LengthRule);
            Assert.Contains(flattened, r => r is MatchesRule);
        }

        // ── Rule text is the clause as written ─────────────────────────────

        [Fact]
        public void RuleText_IsSlicedFromTheSource_NotReconstructed()
        {
            // __dq_rule and every diagnostic quote this text, so it must read back as the author
            // wrote it, spacing and casing included.
            var rule = ParseRule("in ('NA',  'EMEA')");
            Assert.Equal("in ('NA',  'EMEA')", rule.Text);
        }

        // ── Projection onto stewardship tags ───────────────────────────────

        [Fact]
        public void Clauses_ProjectOntoExpectAndFailTags_ForTheStewardReadSide()
        {
            var column = ParseColumn(
                "UserId EXPECT NOT NULL ON FAILURE THROW EXPECT UNIQUE ON FAILURE QUARANTINE");

            var tags = ColumnExpectProjection.WithProjectedTags(column, column.Metadata);

            Assert.Equal("NOT NULL", tags["expect"]);
            Assert.Equal("THROW", tags["fail"]);
            Assert.Equal("UNIQUE", tags["expect_1"]);
            Assert.Equal("QUARANTINE", tags["fail_1"]);
        }

        [Fact]
        public void ProjectedTags_ReadBackThroughTheStringParser_Unchanged()
        {
            // The catalog, Portal, and SHOW DATA QUALITY RULES all re-read these tags off lineage,
            // so what is projected has to survive the trip back.
            var column = ParseColumn(@"Email EXPECT MATCHES '^[^@]+@[^@]+$' AND NOT NULL ON FAILURE QUARANTINE");
            var tags = ColumnExpectProjection.WithProjectedTags(column, column.Metadata);

            var binding = ColumnRuleParser.ParseBindings(tags).Single();

            Assert.Equal(FailAction.Quarantine, binding.Action);
            Assert.Collection(binding.Rules,
                r => Assert.Equal("^[^@]+@[^@]+$", Assert.IsType<MatchesRule>(r).Pattern),
                r => Assert.IsType<NotNullRule>(r));
        }

        [Fact]
        public void ClauseLabel_NamesTheClause_NotADeadTagForm()
        {
            var column = ParseColumn("c EXPECT NOT NULL EXPECT UNIQUE");
            var bindings = ColumnExpectProjection.ToBindings(column);

            Assert.Equal("EXPECT", bindings[0].ClauseLabel);
            Assert.Equal("EXPECT #2", bindings[1].ClauseLabel);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Parses one statement directly, so a malformed rule surfaces as the SyntaxException the
        /// parser raises. Script-level <c>Parse()</c> collects diagnostics instead of throwing (the
        /// language server needs the rest of the file), and the Evaluator turns any Error
        /// diagnostic into a hard failure before execution — both paths refuse the script.
        /// </summary>
        private static Statement ParseStatement(string sql) =>
            new ETL_SQL.Core.Parser.Parser(new Lexer(sql).Tokenize(), sql).ParseStatement();

        private static SelectColumn ParseColumn(string columnSql)
        {
            var sql = $"SELECT {columnSql} FROM src;";
            return ((SelectStatement)ParseStatement(sql)).Columns[0];
        }

        private static IReadOnlyList<ColumnRule> ParseRules(string ruleText) =>
            Assert.Single(ParseColumn($"c EXPECT {ruleText}").Expectations!).Rules;

        private static ColumnRule ParseRule(string ruleText) => Assert.Single(ParseRules(ruleText));
    }
}
