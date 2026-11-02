using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// Regression tests for the 2026-11-01 engine bug and security review (TODO §6, ETL-SQL.Engine).
/// </summary>
public sealed class EngineAuditRegressionTests
{
    private static Evaluator NewEvaluator() =>
        ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    // ── PARALLEL forks ────────────────────────────────────────────────────────

    [Fact]
    public void Fork_SharesFileOperationBudgetWithParent()
    {
        // Two branches of 60 operations each must hit the 100-operation ceiling together,
        // not get 100 apiece.
        var parent = NewEvaluator();
        var fork = parent.Fork();

        parent.IncrementOperationCount(OperationType.FileSystem, null, 60);
        Assert.Throws<ETL_SQL.Services.SecurityException>(
            () => fork.IncrementOperationCount(OperationType.FileSystem, null, 60));
    }

    [Fact]
    public void Fork_StartsAtParentRecursionDepthAndPaths()
    {
        var parent = NewEvaluator();
        parent.CurrentRecursiveDepth = 3;
        parent.CurrentScriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "outer.etlsql");
        parent.WorkingDirectory = System.IO.Path.GetTempPath();

        var fork = (Evaluator)parent.Fork();

        Assert.Equal(3, fork.CurrentRecursiveDepth);
        Assert.Equal(parent.CurrentScriptPath, fork.CurrentScriptPath);
        Assert.Equal(parent.WorkingDirectory, fork.WorkingDirectory);
    }

    [Fact]
    public void Merge_FoldsLocalScopeWritesBack()
    {
        // PARALLEL inside a procedure: the branch writes the procedure's local, not a global.
        var parent = new VariableScopeManager();
        parent.DeclareVariable("@global", 1m);
        parent.PushScope(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["@local"] = 1m });

        var fork = parent.Fork();
        fork.SetVariable("@local", 2m);
        fork.SetVariable("@global", 3m);
        parent.Merge(fork);

        Assert.Equal(2m, parent.GetVariable("@local"));
        Assert.Equal(3m, parent.GetVariable("@global"));
    }

    [Fact]
    public void Merge_KeepsSecretMetadataDeclaredInBranch()
    {
        var parent = new VariableScopeManager();
        var fork = parent.Fork();
        fork.DeclareVariable("@secret", "s3cr3t", new VariableMetadata { IsDeclared = true, IsSecret = true });

        parent.Merge(fork);

        Assert.Equal("s3cr3t", parent.GetVariable("@secret"));
        Assert.True(parent.TryGetVariableMetadata("@secret", out var meta) && meta.IsSecret);
    }

    // ── Sensitive values ──────────────────────────────────────────────────────

    [Fact]
    public async Task Interpolation_DoesNotDecryptSensitiveValueIntoPrint()
    {
        var evaluator = NewEvaluator();
        evaluator.ScriptPassword = "audit-key";
        var encrypted = CryptoUtils.Encrypt("plain-pw-123", "audit-key");
        evaluator.VarContext.DeclareVariable("@pw", encrypted,
            new VariableMetadata { IsDeclared = true, IsSensitive = true });

        await evaluator.EvaluateStatement(Parse("PRINT 'pw=${@pw}';").Statements.Single());

        Assert.DoesNotContain(evaluator.Messages, m => m.Message.Contains("plain-pw-123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Interpolation_StillDecryptsForCredentialConsumers()
    {
        var evaluator = NewEvaluator();
        evaluator.ScriptPassword = "audit-key";
        var encrypted = CryptoUtils.Encrypt("plain-pw-123", "audit-key");
        evaluator.VarContext.DeclareVariable("@pw", encrypted,
            new VariableMetadata { IsDeclared = true, IsSensitive = true });

        var value = await evaluator.EvaluateValue(
            new LiteralExpression("pw=${@pw}", TokenType.STRING_LITERAL), new Row(), decryptSensitive: true);

        Assert.Equal("pw=plain-pw-123", value);
    }

    [Fact]
    public async Task Print_MasksSecretDeclaredInLocalScope()
    {
        var evaluator = NewEvaluator();
        evaluator.VarContext.PushScope(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase));
        evaluator.VarContext.DeclareVariable("@token", "local-secret-value",
            new VariableMetadata { IsDeclared = true, IsSecret = true });

        await evaluator.EvaluateStatement(Parse("PRINT @token;").Statements.Single());

        Assert.DoesNotContain(evaluator.Messages, m => m.Message.Contains("local-secret-value", StringComparison.Ordinal));
        Assert.Contains(evaluator.Messages, m => m.Message.Contains("*******", StringComparison.Ordinal));
    }

    // ── Path resolution ───────────────────────────────────────────────────────

    [Fact]
    public void ResolvePath_WithUnresolvedPlaceholder_StillValidatesTraversalIntoSystemDirectory()
    {
        var evaluator = NewEvaluator();
        var path = OperatingSystem.IsWindows()
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "${x}", "..", "drivers", "etc", "hosts")
            : "/etc/${x}/../passwd";

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() => evaluator.ResolvePath(path));
    }

    [Fact]
    public void ResolvePath_WithPlaceholderInAllowedPath_KeepsPlaceholder()
    {
        var evaluator = NewEvaluator();
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "out_${region}.csv");

        Assert.Equal(path, evaluator.ResolvePath(path));
    }

    // ── Correlated subquery stack ─────────────────────────────────────────────

    [Fact]
    public async Task EvaluateStream_EarlyExit_PopsOuterRowFrame()
    {
        var evaluator = NewEvaluator();
        var query = Parse("SELECT 1 AS n UNION ALL SELECT 2 AS n;").Statements.Single();

        await foreach (var _ in evaluator.EvaluateStream(new SubqueryExpression(query), new Row()))
            break;

        Assert.Empty(evaluator.OuterRowStack);
    }
}
