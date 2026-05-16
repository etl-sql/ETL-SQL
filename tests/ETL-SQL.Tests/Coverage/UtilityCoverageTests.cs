using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Common;
using ETL_SQL.Core.Analysis;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Coverage
{
    /// <summary>
    /// Targets 0%/low-coverage utilities: ExecutionTreeAsciiRenderer, NullExtensions,
    /// TypeConverter, BinaryOperatorFactory, DmlDetector, TempFileHelper, EngineLogger,
    /// MinMaxValue, CanonicalEqualityComparer, SubqueryResult, ReportKeywordLintRule.
    /// </summary>
    public class UtilityCoverageTests
    {
        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task<IList<LintResult>> Lint(ILintRule rule, string sql,
            ILintContext ctx = null)
        {
            ctx ??= new DefaultLintContext();
            return (await rule.AnalyzeAsync(Parse(sql), ctx)).ToList();
        }

        // ── ExecutionTreeAsciiRenderer ────────────────────────────────────────

        private static ExecutionTree BuildTree(params (string name, ExecutionStatus status, long rows)[] nodes)
        {
            var tree = new ExecutionTree();
            foreach (var (name, status, rows) in nodes)
            {
                var node = new ExecutionNode { Name = name, Status = status, RowsProcessed = rows };
                if (status != ExecutionStatus.Waiting)
                {
                    node.StartTicks = Stopwatch.GetTimestamp();
                    node.EndTicks = node.StartTicks + Stopwatch.Frequency; // 1 second
                }
                tree.AddNode(node);
            }
            return tree;
        }

        [Fact]
        public void AsciiRenderer_SingleNode_RendersLabel()
        {
            var renderer = new ExecutionTreeAsciiRenderer();
            var tree = BuildTree(("SELECT #t", ExecutionStatus.Completed, 42));
            var lines = renderer.Render(tree);
            Assert.Single(lines);
            Assert.Contains("SELECT #t", lines[0].Label);
        }

        [Fact]
        public void AsciiRenderer_EmptyTree_ReturnsEmpty()
        {
            var renderer = new ExecutionTreeAsciiRenderer();
            var lines = renderer.Render(new ExecutionTree());
            Assert.Empty(lines);
        }

        [Fact]
        public void AsciiRenderer_MultipleRoots_RendersAll()
        {
            var renderer = new ExecutionTreeAsciiRenderer();
            var tree = BuildTree(
                ("stmt1", ExecutionStatus.Completed, 5),
                ("stmt2", ExecutionStatus.Completed, 10));
            var lines = renderer.Render(tree);
            Assert.Equal(2, lines.Count);
        }

        [Fact]
        public void AsciiRenderer_WithChildren_RendersTree()
        {
            var renderer = new ExecutionTreeAsciiRenderer();
            var tree = new ExecutionTree();
            var parent = new ExecutionNode { Name = "PARALLEL", IsParallelBlock = true, Status = ExecutionStatus.Running };
            parent.StartTicks = Stopwatch.GetTimestamp();
            tree.AddNode(parent);
            for (int i = 0; i < 3; i++)
            {
                var child = new ExecutionNode { Name = $"child{i}", Status = ExecutionStatus.Completed, RowsProcessed = i };
                child.StartTicks = Stopwatch.GetTimestamp();
                child.EndTicks = child.StartTicks + Stopwatch.Frequency;
                tree.AddNode(child, parent.Id);
            }
            var lines = renderer.Render(tree);
            Assert.True(lines.Count >= 3);
        }

        [Fact]
        public void AsciiRenderer_Collapse_WhenParallelExceedsThreshold()
        {
            var renderer = new ExecutionTreeAsciiRenderer(collapseThreshold: 3);
            var tree = new ExecutionTree();
            var parent = new ExecutionNode { Name = "PARALLEL", IsParallelBlock = true, Status = ExecutionStatus.Running };
            parent.StartTicks = Stopwatch.GetTimestamp();
            tree.AddNode(parent);
            var statuses = new[] {
                ExecutionStatus.Completed, ExecutionStatus.Running,
                ExecutionStatus.Faulted, ExecutionStatus.Waiting, ExecutionStatus.Completed
            };
            for (int i = 0; i < 5; i++)
            {
                var child = new ExecutionNode { Name = $"task{i}", Status = statuses[i] };
                if (statuses[i] != ExecutionStatus.Waiting)
                    child.StartTicks = Stopwatch.GetTimestamp();
                tree.AddNode(child, parent.Id);
            }
            var lines = renderer.Render(tree);
            Assert.True(lines.Any(l => l.IsSummary));
        }

        [Fact]
        public void AsciiRenderer_FormatStats_WaitingNode_Empty()
        {
            var node = new ExecutionNode { Status = ExecutionStatus.Waiting };
            Assert.Equal("", ExecutionTreeAsciiRenderer.FormatStats(node));
        }

        [Fact]
        public void AsciiRenderer_FormatStats_RunningNode_HasEllipsis()
        {
            var node = new ExecutionNode { Status = ExecutionStatus.Running, StartTicks = Stopwatch.GetTimestamp() };
            var stats = ExecutionTreeAsciiRenderer.FormatStats(node);
            Assert.Contains("…", stats);
        }

        [Fact]
        public void AsciiRenderer_FormatStats_CompletedWithRows()
        {
            var node = new ExecutionNode { Status = ExecutionStatus.Completed, RowsProcessed = 1500 };
            node.StartTicks = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
            node.EndTicks = Stopwatch.GetTimestamp();
            var stats = ExecutionTreeAsciiRenderer.FormatStats(node);
            Assert.NotEmpty(stats);
        }

        [Fact]
        public void AsciiRenderer_DefaultCollapseThreshold_IsFive()
        {
            var renderer = new ExecutionTreeAsciiRenderer();
            Assert.Equal(5, renderer.CollapseThreshold);
        }

        // ── NullExtensions ────────────────────────────────────────────────────

        [Fact]
        public void NullExtensions_IsNull_NullValue_True()
        {
            Assert.True(((object)null).IsNull());
        }

        [Fact]
        public void NullExtensions_IsNull_DBNullValue_True()
        {
            Assert.True(DBNull.Value.IsNull());
        }

        [Fact]
        public void NullExtensions_IsNull_NonNull_False()
        {
            Assert.False(((object)"hello").IsNull());
        }

        [Fact]
        public void NullExtensions_OrNull_DBNull_ReturnsNull()
        {
            Assert.Null(((object)DBNull.Value).OrNull());
        }

        [Fact]
        public void NullExtensions_OrNull_RegularValue_ReturnsValue()
        {
            Assert.Equal("hello", ((object)"hello").OrNull());
        }

        [Fact]
        public void NullExtensions_OrNull_Null_ReturnsNull()
        {
            Assert.Null(((object)null).OrNull());
        }

        [Fact]
        public void NullExtensions_ToDbNull_Null_ReturnsDBNull()
        {
            Assert.Equal(DBNull.Value, ((object)null).ToDbNull());
        }

        [Fact]
        public void NullExtensions_ToDbNull_NonNull_ReturnsSame()
        {
            Assert.Equal("test", ((object)"test").ToDbNull());
        }

        // ── TypeConverter ─────────────────────────────────────────────────────

        [Fact]
        public void TypeConverter_Cast_ValidJson_ReturnsString()
        {
            var result = TypeConverter.Cast("{\"k\":1}", "JSON");
            Assert.Equal("{\"k\":1}", result);
        }

        [Fact]
        public void TypeConverter_Cast_EmptyJson_ReturnsEmpty()
        {
            var result = TypeConverter.Cast("", "JSON");
            Assert.Equal("", result?.ToString());
        }

        [Fact]
        public void TypeConverter_Cast_ValidXml_ReturnsString()
        {
            var result = TypeConverter.Cast("<root><a>1</a></root>", "XML");
            Assert.Equal("<root><a>1</a></root>", result?.ToString());
        }

        [Fact]
        public void TypeConverter_Cast_EmptyXml_ReturnsEmpty()
        {
            var result = TypeConverter.Cast("", "XML");
            Assert.Equal("", result?.ToString());
        }

        [Fact]
        public void TypeConverter_Cast_Base64ToBinary_ReturnsByteArray()
        {
            var b64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            var result = TypeConverter.Cast(b64, "VARBINARY");
            Assert.IsType<byte[]>(result);
        }

        [Fact]
        public void TypeConverter_Cast_ByteArrayToBinary_ReturnsSame()
        {
            var bytes = new byte[] { 10, 20 };
            var result = TypeConverter.Cast(bytes, "BINARY");
            Assert.Same(bytes, result);
        }

        [Fact]
        public void TypeConverter_Cast_ByteArrayToBlob_ReturnsSame()
        {
            var bytes = new byte[] { 1 };
            var result = TypeConverter.Cast(bytes, "BLOB");
            Assert.Same(bytes, result);
        }

        [Fact]
        public void TypeConverter_Cast_Base64ToBlob_ReturnsByteArray()
        {
            var b64 = Convert.ToBase64String(new byte[] { 5, 6 });
            var result = TypeConverter.Cast(b64, "LOB");
            Assert.IsType<byte[]>(result);
        }

        [Fact]
        public void TypeConverter_Cast_GuidString_ReturnsGuid()
        {
            var g = Guid.NewGuid();
            var result = TypeConverter.Cast(g.ToString(), "UNIQUEIDENTIFIER");
            Assert.Equal(g, result);
        }

        [Fact]
        public void TypeConverter_Cast_GuidInstance_ReturnsSame()
        {
            var g = Guid.NewGuid();
            Assert.Equal(g, TypeConverter.Cast(g, "GUID"));
        }

        [Fact]
        public void TypeConverter_Cast_GuidString_WithUUID_ReturnsGuid()
        {
            var g = Guid.NewGuid();
            var result = TypeConverter.Cast(g.ToString(), "UUID");
            Assert.Equal(g, result);
        }

        [Fact]
        public void TypeConverter_Cast_ValidTimeSpan_ReturnsTimeSpan()
        {
            var result = TypeConverter.Cast("01:30:00", "TIME");
            Assert.Equal(TimeSpan.FromMinutes(90), result);
        }

        [Fact]
        public void TypeConverter_Cast_ImageFromByteArray_ReturnsSame()
        {
            var bytes = new byte[] { 0xFF, 0xD8 };
            var result = TypeConverter.Cast(bytes, "IMAGE");
            Assert.Same(bytes, result);
        }

        [Fact]
        public void TypeConverter_Cast_ImageFromJpegPath_ReturnsPath()
        {
            var result = TypeConverter.Cast("photo.jpg", "IMAGE");
            Assert.Equal("photo.jpg", result);
        }

        [Fact]
        public void TypeConverter_Cast_ImageFromBase64_ReturnsByteArray()
        {
            var b64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            var result = TypeConverter.Cast(b64, "IMAGE");
            Assert.IsType<byte[]>(result);
        }

        [Fact]
        public void TypeConverter_Cast_MinMaxFromValue_ReturnsMinMax()
        {
            var result = TypeConverter.Cast(5m, "MINMAX");
            Assert.IsType<MinMaxValue>(result);
        }

        [Fact]
        public void TypeConverter_Cast_MinMaxFromList_ReturnsMinMax()
        {
            var list = new List<object> { 1m, 10m };
            var result = TypeConverter.Cast(list, "MINMAX") as MinMaxValue;
            Assert.NotNull(result);
            Assert.Equal(1m, result!.Min);
            Assert.Equal(10m, result!.Max);
        }

        [Fact]
        public void TypeConverter_Cast_MinMaxInstance_ReturnsSame()
        {
            var mm = new MinMaxValue(0m, 5m);
            var result = TypeConverter.Cast(mm, "MINMAX");
            Assert.Same(mm, result);
        }

        [Fact]
        public void TypeConverter_Cast_Vector_ReturnsString()
        {
            var result = TypeConverter.Cast("[1,2,3]", "VECTOR");
            Assert.Equal("[1,2,3]", result?.ToString());
        }

        [Fact]
        public void TypeConverter_Cast_Sensitive_ReturnsOriginal()
        {
            var result = TypeConverter.Cast("secret_data", "SENSITIVE");
            Assert.Equal("secret_data", result);
        }

        [Fact]
        public void TypeConverter_Cast_Secret_ReturnsOriginal()
        {
            var result = TypeConverter.Cast("my_secret", "SECRET");
            Assert.Equal("my_secret", result);
        }

        [Fact]
        public void TypeConverter_Cast_RelDate_ReturnsString()
        {
            var result = TypeConverter.Cast("-7d", "RELDATE");
            Assert.Equal("-7d", result?.ToString());
        }

        [Fact]
        public void TypeConverter_Cast_NullTypeName_ReturnsValue()
        {
            var result = TypeConverter.Cast(42, null);
            Assert.Equal(42, result);
        }

        [Fact]
        public void TypeConverter_Cast_UnknownType_ReturnsValue()
        {
            var result = TypeConverter.Cast("hello", "UNKNOWN_XYZ");
            Assert.Equal("hello", result);
        }

        [Fact]
        public void TypeConverter_Cast_WithParens_ParsesBaseType()
        {
            var result = TypeConverter.Cast("42", "VARCHAR(100)");
            Assert.Equal("42", result?.ToString());
        }

        [Fact]
        public void TypeConverter_Register_CustomType_UsedByCast()
        {
            TypeConverter.Register("MYTEST", v => "custom:" + v.ToString());
            var result = TypeConverter.Cast("x", "MYTEST");
            Assert.Equal("custom:x", result?.ToString());
        }

        // ── BinaryOperatorFactory ─────────────────────────────────────────────

        [Fact]
        public void BinaryOp_DatePlusNumber_ReturnsDate()
        {
            var d = new DateTime(2025, 1, 1);
            var result = BinaryOperatorFactory.Execute(TokenType.PLUS, d, 10m);
            Assert.Equal(new DateTime(2025, 1, 11), result);
        }

        [Fact]
        public void BinaryOp_DateMinusDate_ReturnsDecimalDays()
        {
            var d1 = new DateTime(2025, 1, 11);
            var d2 = new DateTime(2025, 1, 1);
            var result = BinaryOperatorFactory.Execute(TokenType.MINUS, d1, d2);
            Assert.Equal(10m, result);
        }

        [Fact]
        public void BinaryOp_DateMinusNumber_ReturnsDate()
        {
            var d = new DateTime(2025, 1, 11);
            var result = BinaryOperatorFactory.Execute(TokenType.MINUS, d, 10m);
            Assert.Equal(new DateTime(2025, 1, 1), result);
        }

        [Fact]
        public void BinaryOp_StringConcat_ReturnsConcatenated()
        {
            var result = BinaryOperatorFactory.Execute(TokenType.PLUS, "hello", " world");
            Assert.Equal("hello world", result?.ToString());
        }

        [Fact]
        public void BinaryOp_NullLeft_ReturnsNull()
        {
            var result = BinaryOperatorFactory.Execute(TokenType.PLUS, null, 5m);
            Assert.Null(result);
        }

        [Fact]
        public void BinaryOp_NullRight_ReturnsNull()
        {
            var result = BinaryOperatorFactory.Execute(TokenType.MINUS, 5m, null);
            Assert.Null(result);
        }

        [Fact]
        public void BinaryOp_DivideByZero_Throws()
        {
            Assert.Throws<ExecutionException>(() =>
                BinaryOperatorFactory.Execute(TokenType.SLASH, 10m, 0m));
        }

        [Fact]
        public void BinaryOp_ModuloByZero_Throws()
        {
            Assert.Throws<ExecutionException>(() =>
                BinaryOperatorFactory.Execute(TokenType.MODULO, 10m, 0m));
        }

        [Fact]
        public void BinaryOp_UnknownOperator_ReturnsNull()
        {
            var result = BinaryOperatorFactory.Execute(TokenType.COMMA, 1m, 2m);
            Assert.Null(result);
        }

        [Fact]
        public void BinaryOp_Multiply_Decimals()
        {
            Assert.Equal(6m, BinaryOperatorFactory.Execute(TokenType.STAR, 2m, 3m));
        }

        [Fact]
        public void BinaryOp_Modulo_Decimals()
        {
            Assert.Equal(1m, BinaryOperatorFactory.Execute(TokenType.MODULO, 7m, 3m));
        }

        [Fact]
        public void BinaryOp_Register_CustomOp_UsedByExecute()
        {
            BinaryOperatorFactory.Register(TokenType.QUESTION, (l, r) => l?.ToString() + "&" + r?.ToString());
            var result = BinaryOperatorFactory.Execute(TokenType.QUESTION, "a", "b");
            Assert.Equal("a&b", result?.ToString());
        }

        [Fact]
        public void BinaryOp_DatePlusInvalidNumber_ReturnsNull()
        {
            var d = new DateTime(2025, 1, 1);
            var result = BinaryOperatorFactory.Execute(TokenType.PLUS, d, "notanumber");
            Assert.Null(result);
        }

        // ── DmlDetector ───────────────────────────────────────────────────────

        private static DmlDetector DetectDml(string sql, string targetTable = null, string targetConn = null)
        {
            var script = Parse(sql);
            var detector = new DmlDetector(targetTable, targetConn);
            foreach (var stmt in script.Statements)
                detector.Analyze(stmt);
            return detector;
        }

        [Fact]
        public void DmlDetector_Insert_Detected()
        {
            var d = DetectDml("INSERT INTO #t VALUES (1);");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_Update_Detected()
        {
            var d = DetectDml("UPDATE #t SET x = 1 WHERE x = 0;");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_Delete_Detected()
        {
            var d = DetectDml("DELETE FROM #t WHERE x = 1;");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_SelectInto_Detected()
        {
            var d = DetectDml("SELECT 1 AS n INTO #out;");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_TruncateTable_Detected()
        {
            var d = DetectDml("CREATE TABLE #t (x INT); TRUNCATE TABLE #t;");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_PureSelect_NotDetected()
        {
            var d = DetectDml("SELECT 1 AS n;");
            Assert.False(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_InsideBlock_Detected()
        {
            var d = DetectDml("BEGIN INSERT INTO #t VALUES (1); END");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_InsideIf_Detected()
        {
            var d = DetectDml("IF 1 = 1 BEGIN INSERT INTO #t VALUES (1); END");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_InsideIfElseIf_Detected()
        {
            var d = DetectDml(
                "IF 1 = 2 BEGIN SELECT 1; END " +
                "ELSE IF 1 = 1 BEGIN UPDATE #t SET x = 2; END");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_InsideIfElse_Detected()
        {
            var d = DetectDml(
                "IF 1 = 2 BEGIN SELECT 1; END ELSE BEGIN DELETE FROM #t; END");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_InsideWhile_Detected()
        {
            var d = DetectDml("WHILE 1 = 0 BEGIN INSERT INTO #t VALUES (1); END");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_InsideFor_Detected()
        {
            var d = DetectDml("FOR @i = 1 TO 3 BEGIN UPDATE #t SET x = @i; END");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_InsideForeach_Detected()
        {
            var d = DetectDml("FOREACH @r IN (SELECT 1 AS x) BEGIN DELETE FROM #t WHERE x = @r; END");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_InsideTryCatch_Detected()
        {
            var d = DetectDml(
                "BEGIN TRY INSERT INTO #t VALUES (1); END TRY " +
                "BEGIN CATCH SELECT @@ERROR; END CATCH");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_Pushdown_OpaqueCall_Detected()
        {
            var d = DetectDml("EXECUTE myconn BEGIN SELECT 1; END");
            Assert.True(d.IsDmlDetected);
            Assert.True(d.HasOpaqueCalls);
        }

        [Fact]
        public void DmlDetector_WithTargetFilter_MatchingTable_Detected()
        {
            var d = DetectDml("INSERT INTO #target VALUES (1);", targetTable: "#target");
            Assert.True(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_WithTargetFilter_NonMatchingTable_NotDetected()
        {
            var d = DetectDml("INSERT INTO #other VALUES (1);", targetTable: "#target");
            Assert.False(d.IsDmlDetected);
        }

        [Fact]
        public void DmlDetector_NullStatement_NoThrow()
        {
            var detector = new DmlDetector();
            detector.Analyze(null); // should be a no-op
            Assert.False(detector.IsDmlDetected);
        }

        // ── TempFileHelper ────────────────────────────────────────────────────

        [Fact]
        public void TempFileHelper_NullPath_NoThrow()
        {
            TempFileHelper.SafeDelete(null);
        }

        [Fact]
        public void TempFileHelper_EmptyPath_NoThrow()
        {
            TempFileHelper.SafeDelete("");
        }

        [Fact]
        public void TempFileHelper_NonExistentFile_NoThrow()
        {
            TempFileHelper.SafeDelete(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp"));
        }

        [Fact]
        public void TempFileHelper_ExistingFile_Deletes()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
            File.WriteAllText(path, "test");
            TempFileHelper.SafeDelete(path);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void TempFileHelper_NonExistentFile_WithLogger_LogsDebug()
        {
            var logger = new EngineLogger("test");
            var logged = new List<string>();
            logger.OnMessage += (msg, _, _) => logged.Add(msg);
            logger.SuppressConsole = true;
            TempFileHelper.SafeDelete(Path.Combine(Path.GetTempPath(), "missing_xyz.tmp"), logger);
            Assert.Contains(logged, m => m.Contains("does not exist"));
        }

        [Fact]
        public void TempFileHelper_ExistingFile_WithLogger_LogsSuccess()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
            File.WriteAllText(path, "x");
            var logger = new EngineLogger("test");
            var logged = new List<string>();
            logger.OnMessage += (msg, _, _) => logged.Add(msg);
            logger.SuppressConsole = true;
            TempFileHelper.SafeDelete(path, logger);
            Assert.Contains(logged, m => m.Contains("Successfully deleted"));
        }

        // ── EngineLogger ──────────────────────────────────────────────────────

        [Fact]
        public void EngineLogger_DefaultCategory_Properties()
        {
            var log = new EngineLogger();
            Assert.True(log.IsDebugEnabled);
            Assert.True(log.IsVerboseEnabled);
        }

        [Fact]
        public void EngineLogger_Log_AllLevels_NoThrow()
        {
            var log = new EngineLogger("test");
            log.SuppressConsole = true;
            log.Log(LogLevel.Info, "info");
            log.Log(LogLevel.Warning, "warn");
            log.Log(LogLevel.Error, "error", new Exception("test ex"));
            log.Log(LogLevel.Debug, "debug");
        }

        [Fact]
        public void EngineLogger_WriteLine_NoThrow()
        {
            var log = new EngineLogger("test");
            log.SuppressConsole = true;
            log.WriteLine("test message", ConsoleColor.Green);
        }

        [Fact]
        public void EngineLogger_WithSessionId_IncludesInOutput()
        {
            var log = new EngineLogger("test");
            log.SessionId = "sess-123";
            log.SuppressConsole = true;
            string received = null;
            log.OnMessage += (msg, sid, _) => { received = msg; };
            log.Log(LogLevel.Info, "test");
            Assert.Contains("sess-123", received);
        }

        [Fact]
        public void EngineLogger_JsonMode_SerializesMessage()
        {
            var log = new EngineLogger("test");
            log.SuppressConsole = true;
            log.IsJsonMode = true;
            // Should not throw even in JSON mode
            log.Log(LogLevel.Info, "json msg");
        }

        [Fact]
        public void EngineLogger_OnMessage_Fires()
        {
            var log = new EngineLogger("test");
            log.SuppressConsole = true;
            bool fired = false;
            log.OnMessage += (_, _, _) => fired = true;
            log.Log(LogLevel.Info, "test");
            Assert.True(fired);
        }

        // ── MinMaxValue ───────────────────────────────────────────────────────

        [Fact]
        public void MinMaxValue_ToString_WithValues()
        {
            var mm = new MinMaxValue(1m, 10m);
            Assert.Equal("(1, 10)", mm.ToString());
        }

        [Fact]
        public void MinMaxValue_ToString_WithNulls()
        {
            var mm = new MinMaxValue();
            Assert.Equal("(NULL, NULL)", mm.ToString());
        }

        [Fact]
        public void MinMaxValue_Properties_Accessible()
        {
            var mm = new MinMaxValue("a", "z");
            Assert.Equal("a", mm.Min);
            Assert.Equal("z", mm.Max);
        }

        // ── CanonicalEqualityComparer ─────────────────────────────────────────

        [Fact]
        public void CanonicalEqualityComparer_NullHash_IsZero()
        {
            Assert.Equal(0, CanonicalEqualityComparer.Instance.GetHashCode(null));
        }

        [Fact]
        public void CanonicalEqualityComparer_DbNullHash_IsZero()
        {
            Assert.Equal(0, CanonicalEqualityComparer.Instance.GetHashCode(DBNull.Value));
        }

        [Fact]
        public void CanonicalEqualityComparer_DecimalHash_Consistent()
        {
            var h1 = CanonicalEqualityComparer.Instance.GetHashCode(5m);
            var h2 = CanonicalEqualityComparer.Instance.GetHashCode(5m);
            Assert.Equal(h1, h2);
        }

        [Fact]
        public void CanonicalEqualityComparer_IntHash_MatchesDecimal()
        {
            var hi = CanonicalEqualityComparer.Instance.GetHashCode(5);
            var hd = CanonicalEqualityComparer.Instance.GetHashCode(5m);
            Assert.Equal(hd, hi);
        }

        [Fact]
        public void CanonicalEqualityComparer_LongHash_MatchesDecimal()
        {
            var hl = CanonicalEqualityComparer.Instance.GetHashCode(5L);
            var hd = CanonicalEqualityComparer.Instance.GetHashCode(5m);
            Assert.Equal(hd, hl);
        }

        [Fact]
        public void CanonicalEqualityComparer_BoolHash_Consistent()
        {
            var h = CanonicalEqualityComparer.Instance.GetHashCode(true);
            Assert.Equal(true.GetHashCode(), h);
        }

        [Fact]
        public void CanonicalEqualityComparer_OnHash_MapsToTrue()
        {
            var hOn = CanonicalEqualityComparer.Instance.GetHashCode("ON");
            Assert.Equal(true.GetHashCode(), hOn);
        }

        [Fact]
        public void CanonicalEqualityComparer_OffHash_MapsToFalse()
        {
            var hOff = CanonicalEqualityComparer.Instance.GetHashCode("OFF");
            Assert.Equal(false.GetHashCode(), hOff);
        }

        [Fact]
        public void CanonicalEqualityComparer_DateTimeHash_Consistent()
        {
            var dt = new DateTime(2025, 1, 1);
            var h = CanonicalEqualityComparer.Instance.GetHashCode(dt);
            Assert.Equal(dt.GetHashCode(), h);
        }

        [Fact]
        public void CanonicalEqualityComparer_StringNumericHash_MatchesDecimal()
        {
            var hs = CanonicalEqualityComparer.Instance.GetHashCode("42");
            var hd = CanonicalEqualityComparer.Instance.GetHashCode(42m);
            Assert.Equal(hd, hs);
        }

        [Fact]
        public void CanonicalEqualityComparer_Equals_SoftEqual()
        {
            Assert.True(CanonicalEqualityComparer.Instance.Equals(1m, 1));
        }

        // ── SubqueryResult ────────────────────────────────────────────────────

        [Fact]
        public void SubqueryResult_Scalar_IsScalar()
        {
            var r = new SubqueryResult(42m);
            Assert.True(r.IsScalar);
            Assert.Equal(42m, r.ScalarValue);
            Assert.True(r.MemoryUsageBytes > 0);
        }

        [Fact]
        public void SubqueryResult_InSet_NotScalar()
        {
            var set = new HashSet<object?> { 1m, 2m, 3m };
            var r = new SubqueryResult(set);
            Assert.False(r.IsScalar);
            Assert.NotNull(r.InSet);
            Assert.True(r.MemoryUsageBytes > 0);
        }

        [Fact]
        public void SubqueryResult_NullScalar_ZeroMemory()
        {
            var r = new SubqueryResult((object)null);
            Assert.Equal(0, r.MemoryUsageBytes);
        }

        [Fact]
        public async Task SubqueryResult_DisposeAsync_NoThrow()
        {
            var r = new SubqueryResult(99m);
            await r.DisposeAsync();
        }

        // ── ReportKeywordLintRule ─────────────────────────────────────────────

        [Fact]
        public async Task ReportKeywordLintRule_NonKeywordName_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule, "CREATE VISUAL myvis AS BAR (SOURCE (SELECT 1 AS n));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ReportKeywordLintRule_KeywordAsVisualName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            script.Statements.Add(new CreateVisualStatement
            {
                Name = "SELECT",
                VisualType = VisualType.Bar,
                Source = new VisualSourceExpression()
            });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.RuleName == "Report-SQL Object Keyword Check");
        }

        [Fact]
        public async Task ReportKeywordLintRule_KeywordAsPageName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            script.Statements.Add(new CreatePageStatement { Name = "SELECT", Structure = "A" });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ReportKeywordLintRule_KeywordAsDatasetName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            // TempTableName without & sigil: TrimStart('&') leaves "SELECT" intact → keyword
            script.Statements.Add(new CreateDatasetStatement
            {
                TempTableName = "SELECT",
                SourceQuery = new NoOpStatement()
            });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ReportKeywordLintRule_KeywordAsTemplateName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            script.Statements.Add(new CreateTemplateStatement { Name = "ORDER" });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ReportKeywordLintRule_NormalDataset_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule, "CREATE DATASET &salesdata AS (SELECT 1 AS v);");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ReportKeywordLintRule_NormalTemplate_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule, "CREATE TEMPLATE mytemplate AS (TYPE = 'table');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ReportKeywordLintRule_SelectOnly_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule, "SELECT 1 AS n;");
            Assert.Empty(results);
        }
    }
}
