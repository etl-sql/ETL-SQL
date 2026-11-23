using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

public sealed class ExternalToolSecurityTests : IDisposable
{
    private readonly string _tempRoot;

    public ExternalToolSecurityTests()
    {
        SecurityEventRuntime.ConfigureLocalOutboxFactory(new SqliteSecurityEventOutboxFactory());
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        _tempRoot = Path.Combine(Path.GetTempPath(), "etlsql_xt_sec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── XT04: Windows ancestor boundary ───────────────────────────────────────

    // Stock Windows lets Authenticated Users create folders in C:\ and Users write to
    // C:\ProgramData. Checking ancestors with the full write mask rejected every catalog on every
    // Windows host, so ancestors are held only to the rights that could replace the path.
    [WindowsFact]
    [Trait("Platform", "Windows")]
    [SupportedOSPlatform("windows")]
    public void XT04_WindowsAncestorMayAllowNewSiblingsButNotReplacement()
    {
        AssertWindowsAncestorRule();
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsAncestorRule()
    {
        static System.Security.AccessControl.DirectorySecurity Acl(System.Security.AccessControl.FileSystemRights othersRights)
        {
            var acl = new System.Security.AccessControl.DirectorySecurity();
            var system = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null);
            var others = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null);
            acl.SetOwner(system);
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(system,
                System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(others,
                othersRights, System.Security.AccessControl.AccessControlType.Allow));
            return acl;
        }

        var createOnly = Acl(System.Security.AccessControl.FileSystemRights.CreateDirectories | System.Security.AccessControl.FileSystemRights.CreateFiles);
        OsToolCatalogProtector.VerifyWindowsBoundary(createOnly, @"C:\", ToolCatalogOwnershipMode.MachineAdministrator,
            allowTrustedInstaller: true, ancestorOnly: true);
        Assert.Throws<UnauthorizedAccessException>(() => OsToolCatalogProtector.VerifyWindowsBoundary(
            createOnly, @"C:\catalog", ToolCatalogOwnershipMode.MachineAdministrator, allowTrustedInstaller: true));

        foreach (var replace in new[]
                 {
                     System.Security.AccessControl.FileSystemRights.Delete,
                     System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles,
                     System.Security.AccessControl.FileSystemRights.ChangePermissions,
                     System.Security.AccessControl.FileSystemRights.TakeOwnership,
                 })
        {
            Assert.Throws<UnauthorizedAccessException>(() => OsToolCatalogProtector.VerifyWindowsBoundary(
                Acl(replace), @"C:\parent", ToolCatalogOwnershipMode.MachineAdministrator,
                allowTrustedInstaller: true, ancestorOnly: true));
        }
    }

    // ── XT01: LocalToolCatalogProvider containment ────────────────────────────

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("sub/tool")]
    [InlineData("sub\\tool")]
    [InlineData("C:\\temp\\evil")]
    [InlineData("C:evil")]
    [InlineData("/etc/passwd")]
    [InlineData("\\evil")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("foo..bar")]
    [InlineData("tool.disabled")]
    [InlineData("tool:stream")]
    [InlineData("tool\0bad")]
    public async Task XT01_LocalCatalogRejectsEscapeAliasesAcrossAllOperations(string alias)
    {
        var catalogDir = Path.Combine(_tempRoot, "catalog");
        var outsideDir = Path.Combine(_tempRoot, "outside");

        // Seed a valid encrypted tool definition OUTSIDE the catalog root.
        var outsideCatalog = new LocalToolCatalogProvider(outsideDir, ToolCatalogOwnershipMode.PersonalWorkstation);
        await outsideCatalog.StoreAsync(new ToolDefinition("outside", "CONTAINER",
            new Dictionary<string, string> { ["COMMAND"] = "test", ["IMAGE"] = "img@sha256:" + new string('a', 64) }, false));
        Assert.True(File.Exists(Path.Combine(outsideDir, "outside.json")));

        var catalog = new LocalToolCatalogProvider(catalogDir, ToolCatalogOwnershipMode.PersonalWorkstation);
        var def = new ToolDefinition(alias, "CONTAINER",
            new Dictionary<string, string> { ["COMMAND"] = "test", ["IMAGE"] = "img@sha256:" + new string('a', 64) }, false);

        await Assert.ThrowsAsync<ArgumentException>(() => catalog.StoreAsync(def));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.ResolveAsync(alias));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.GetDefinitionAsync(alias));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.DisableAsync(alias));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.EnableAsync(alias));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.DeleteAsync(alias));

        // Outside file remains completely untouched.
        Assert.True(File.Exists(Path.Combine(outsideDir, "outside.json")));
    }

    [Theory]
    [InlineData("relative/tools")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task XT01_LocalCatalogRequiresAbsoluteRoot(string root)
    {
        var catalog = new LocalToolCatalogProvider(root, ToolCatalogOwnershipMode.PersonalWorkstation);
        await Assert.ThrowsAnyAsync<Exception>(() => catalog.ResolveAsync("validtool"));
        await Assert.ThrowsAnyAsync<Exception>(() => catalog.ListAsync());
    }

    [WindowsFact]
    [Trait("Platform", "Windows")]
    public async Task XT01_LocalCatalogRejectsDirectoryLinkRoot()
    {
        var targetDir = Path.Combine(_tempRoot, "real_target");
        var junctionDir = Path.Combine(_tempRoot, "junction_catalog");
        Directory.CreateDirectory(targetDir);

        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionDir}\" \"{targetDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        Assert.NotNull(mklink);
        Assert.True(mklink!.WaitForExit(5000), "mklink /J timed out");
        Assert.Equal(0, mklink.ExitCode);
        Assert.True(Directory.Exists(junctionDir), "Expected junction directory to be created");

        try
        {
            var catalog = new LocalToolCatalogProvider(junctionDir, ToolCatalogOwnershipMode.PersonalWorkstation);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => catalog.ListAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => catalog.ResolveAsync("anytool"));
        }
        finally
        {
            try { Directory.Delete(junctionDir); } catch { }
        }
    }

    // ── XT02: EXECUTE TOOL honors WHAT_IF ────────────────────────────────────

    [Theory]
    [InlineData("CONTAINER")]
    [InlineData("EXECUTABLE")]
    public async Task XT02_ExecuteToolWhatIfHasZeroSideEffects(string toolType)
    {
        var exeDir = Path.Combine(_tempRoot, "safe_bin");
        Directory.CreateDirectory(exeDir);
        var protector = new OsToolCatalogProtector();
        protector.ProtectDirectory(exeDir, ToolCatalogOwnershipMode.PersonalWorkstation, null);
        var fakeExe = Path.Combine(exeDir, "safe_tool.exe");
        await File.WriteAllTextAsync(fakeExe, "stub");
        protector.ProtectFile(fakeExe, ToolCatalogOwnershipMode.PersonalWorkstation, null);

        var options = toolType == "CONTAINER"
            ? new Dictionary<string, string>
            {
                ["COMMAND"] = "run-report",
                ["IMAGE"] = "approved.io/report@sha256:" + new string('b', 64),
                ["CAPABILITIES"] = "NETWORK"
            }
            : new Dictionary<string, string>
            {
                ["COMMAND"] = fakeExe,
                ["CAPABILITIES"] = "NETWORK"
            };

        var catalog = new Mock<IToolCatalogProvider>();
        catalog.Setup(c => c.ResolveAsync("mytool", It.IsAny<ExecutionIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDefinition("mytool", toolType, options, false));

        var ledger = new Mock<IOperationLedger>(MockBehavior.Strict);
        var tokenIssuer = new Mock<ICapabilityTokenIssuer>(MockBehavior.Strict);
        var factory = new RecordingProcessFactory();

        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.IsWhatIf).Returns(true);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.Connections).Returns(new Dictionary<string, IDataSource>());
        context.SetupGet(c => c.Ledger).Returns(ledger.Object);

        var handler = new ExecuteToolStatementHandler(new Mock<ILogger>().Object, catalog.Object,
            new ConfigurationBuilder().Build(), tokenIssuer.Object, factory);

        var tempFoldersBefore = Directory.GetDirectories(Path.GetTempPath(), "etlsql_tool_*").ToHashSet(StringComparer.OrdinalIgnoreCase);

        await handler.Execute(new ExecuteToolStatement("mytool"), context.Object);

        Assert.Empty(factory.StartedCommands);
        ledger.VerifyNoOtherCalls();
        tokenIssuer.VerifyNoOtherCalls();

        var tempFoldersAfter = Directory.GetDirectories(Path.GetTempPath(), "etlsql_tool_*");
        Assert.Equal(tempFoldersBefore.Count, tempFoldersAfter.Length);
    }

    [Fact]
    public async Task XT02_EvaluatorWhatIfSuppressesExecuteToolViaDi()
    {
        var toolCatalogDir = Path.Combine(_tempRoot, "eval_catalog");
        var provider = new LocalToolCatalogProvider(toolCatalogDir, ToolCatalogOwnershipMode.PersonalWorkstation);
        await provider.StoreAsync(new ToolDefinition("sample_container", "CONTAINER",
            new Dictionary<string, string>
            {
                ["COMMAND"] = "echo",
                ["IMAGE"] = "alpine@sha256:" + new string('c', 64),
                ["ARGS"] = "--msg {TEXT}"
            }, false));

        await using var sp = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
        {
            ["Governance:Tools:Provider"] = "LOCAL",
            ["Governance:Tools:LocalRoot"] = toolCatalogDir,
            ["Governance:Tools:OwnershipMode"] = nameof(ToolCatalogOwnershipMode.PersonalWorkstation)
        });
        var evaluator = sp.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer("SET WHAT_IF ON; EXECUTE TOOL 'sample_container' WITH (TEXT = 'hello');").Tokenize()).Parse();
        await evaluator.Evaluate(script);
    }

    // ── XT03: Docker approval across all launch & reuse paths ─────────────────

    [Fact]
    public void XT03_EnrolledEmptyAllowlistAndRevocationAndUntrustedRegistryAreDenied()
    {
        // Enrolled with NO Security:AllowedDockerImages must fail closed.
        var emptyEnrolled = CreateEnrolledPolicy([]);
        EnterprisePolicyRuntime.SetCurrent(emptyEnrolled);
        var ctx = new Mock<IExecutionContext>();
        ctx.SetupGet(c => c.ExecutionPolicy).Returns(
            ExecutionPolicySnapshot.Capture(emptyEnrolled, "alice", ScriptExecutionMode.Batch, "h1"));

        Assert.Throws<OperationPolicyDeniedException>(() =>
            ProcessPolicyRules.EnforceDockerImage(ctx.Object, "postgres:15"));

        // Enrolled approving official 'postgres', then test untrusted registry hosting same repo name.
        var approved = CreateEnrolledPolicy(["postgres"]);
        EnterprisePolicyRuntime.SetCurrent(approved);
        Assert.Null(Record.Exception(() => ProcessPolicyRules.EnforceDockerImage(ctx.Object, "postgres:15-alpine")));
        Assert.Null(Record.Exception(() => ProcessPolicyRules.EnforceDockerImage(ctx.Object, "postgres@sha256:" + new string('1', 64))));
        Assert.Throws<OperationPolicyDeniedException>(() =>
            ProcessPolicyRules.EnforceDockerImage(ctx.Object, "evil.registry.example/postgres:15-alpine"));

        // Policy revoked -> EnforceDockerImage denies even the previously approved image.
        EnterprisePolicyRuntime.SetCurrent(CreateEnrolledPolicy([]));
        Assert.Throws<OperationPolicyDeniedException>(() =>
            ProcessPolicyRules.EnforceDockerImage(ctx.Object, "postgres:15-alpine"));
    }

    [Fact]
    public async Task XT03_DockerResumeReevaluatesPolicyAndFailsAfterRevocation()
    {
        var approved = CreateEnrolledPolicy(["postgres:15-alpine"]);
        EnterprisePolicyRuntime.SetCurrent(approved);

        var docker = new Mock<IDockerManager>();
        docker.Setup(d => d.GetLastAlias(It.IsAny<string>())).Returns("db");
        docker.Setup(d => d.GetState(It.IsAny<string>()))
            .Returns(new Dictionary<string, string> { ["db"] = "Host=localhost;Port=5432;" });
        docker.Setup(d => d.GetImageName(It.IsAny<string>(), "db")).Returns("postgres:15-alpine");

        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.DockerManager).Returns(docker.Object);
        context.SetupGet(c => c.ExecutionPolicy).Returns(
            ExecutionPolicySnapshot.Capture(approved, "alice", ScriptExecutionMode.Batch, "h1"));
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var handler = new DockerActionStatementHandler(new Mock<ILogger>().Object);
        var resumeStmt = new DockerActionStatement(DockerAction.Resume, "db", DockerTargetMode.Single);

        // Initially allowed.
        await handler.Execute(resumeStmt, context.Object);
        docker.Verify(d => d.ResumeContainer(It.IsAny<string>(), "db", It.IsAny<CancellationToken>()), Times.Once);

        // Policy revoked between pause and resume -> ResumeContainer must never be called again.
        EnterprisePolicyRuntime.SetCurrent(CreateEnrolledPolicy([]));
        await Assert.ThrowsAsync<OperationPolicyDeniedException>(() => handler.Execute(resumeStmt, context.Object));
        docker.Verify(d => d.ResumeContainer(It.IsAny<string>(), "db", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void XT03_PinnedDigestRequirementEnforcedAndBadDigestRejected()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Governance:Docker:AllowedImages:0"] = "postgres",
                ["Governance:Docker:RequirePinnedDigest"] = "true"
            })
            .Build();

        var ctx = new Mock<IExecutionContext>();
        ctx.SetupGet(c => c.ExecutionPolicy).Returns(
            ExecutionPolicySnapshot.Capture(EffectiveEnterprisePolicy.Standalone, "op", ScriptExecutionMode.Batch, "h"));

        Assert.Throws<ExecutionException>(() => ProcessPolicyRules.EnforceDockerImage(ctx.Object, "postgres:15", config));
        Assert.Throws<ExecutionException>(() => ProcessPolicyRules.EnforceDockerImage(ctx.Object, "postgres@sha256:nothex", config));
        Assert.Null(Record.Exception(() =>
            ProcessPolicyRules.EnforceDockerImage(ctx.Object, "postgres:15@sha256:" + new string('a', 64), config)));
    }

    [Fact]
    public async Task XT03_ContainerToolIsCheckedAgainstEnrolledEnterprisePolicy()
    {
        var digestImage = "unapproved.io/tool@sha256:" + new string('d', 64);
        var catalog = new Mock<IToolCatalogProvider>();
        catalog.Setup(c => c.ResolveAsync("t", It.IsAny<ExecutionIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDefinition("t", "CONTAINER", new Dictionary<string, string>
            {
                ["COMMAND"] = "run",
                ["IMAGE"] = digestImage
            }, false));

        var policy = CreateEnrolledPolicy(["approved.io/*"]);
        EnterprisePolicyRuntime.SetCurrent(policy);

        var ctx = new Mock<IExecutionContext>();
        ctx.SetupGet(c => c.ExecutionPolicy).Returns(
            ExecutionPolicySnapshot.Capture(policy, "alice", ScriptExecutionMode.Batch, "h"));
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var factory = new RecordingProcessFactory();
        var handler = new ExecuteToolStatementHandler(new Mock<ILogger>().Object, catalog.Object,
            new ConfigurationBuilder().Build(), processFactory: factory);

        await Assert.ThrowsAsync<OperationPolicyDeniedException>(() => handler.Execute(new ExecuteToolStatement("t"), ctx.Object));
        Assert.Empty(factory.StartedCommands);
    }

    // ── XT04: Admin ownership & deterministic native executable lookup ─────────

    [Fact]
    public async Task XT04_ToolAdminServiceRequiresElevationForMachineMutations()
    {
        var catDir = Path.Combine(_tempRoot, "admin_cat");
        var writableCatalog = new Mock<IWritableToolCatalogProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>();
        var ctx = new CliContext
        {
            Command = "admin-machine-tool-set",
            ToolName = "mytool",
            ToolType = "CONTAINER",
            ToolOptions = [$"COMMAND=run", $"IMAGE=img@sha256:{new string('e', 64)}"]
        };

        var deniedExit = await ToolAdminService.ExecuteAsync(
            "admin-machine-tool-set", ctx, writableCatalog.Object, logger.Object, CancellationToken.None, isElevated: () => false);
        Assert.Equal(1, deniedExit);
        writableCatalog.VerifyNoOtherCalls();

        var deleteDenied = await ToolAdminService.ExecuteAsync(
            "admin-machine-tool-delete", ctx, writableCatalog.Object, logger.Object, CancellationToken.None, isElevated: () => false);
        Assert.Equal(1, deleteDenied);
        writableCatalog.VerifyNoOtherCalls();

        // Explicit elevation succeeds and delegates store cleanly.
        writableCatalog.Setup(w => w.StoreAsync(It.IsAny<ToolDefinition>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var allowedExit = await ToolAdminService.ExecuteAsync(
            "admin-machine-tool-set", ctx, writableCatalog.Object, logger.Object, CancellationToken.None, isElevated: () => true);
        Assert.Equal(0, allowedExit);
        writableCatalog.Verify(w => w.StoreAsync(It.Is<ToolDefinition>(d => d.Name == "mytool"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task XT04_MachineAdministratorCatalogRejectsUserOwnedRootAndNonOwnerWritableAcls()
    {
        // On a normal non-admin test run, MachineAdministrator mode must refuse user-owned catalog roots
        // instead of silently inheriting current-user permissions.
        if (!AdministrativePrivilege.IsElevated())
        {
            var unprivilegedDir = Path.Combine(_tempRoot, "unpriv_cat");
            Directory.CreateDirectory(unprivilegedDir);
            var machineCatalog = new LocalToolCatalogProvider(unprivilegedDir); // defaults to MachineAdministrator
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => machineCatalog.ListAsync());
        }

        // PersonalWorkstation is explicit and still rejects shared/Users/world-writable directories.
        var sharedDir = Path.Combine(_tempRoot, "shared_cat");
        Directory.CreateDirectory(sharedDir);
        if (OperatingSystem.IsWindows())
            GrantWindowsUsersWrite(sharedDir);
        else
            File.SetUnixFileMode(sharedDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);

        var personalCatalog = new LocalToolCatalogProvider(sharedDir, ToolCatalogOwnershipMode.PersonalWorkstation);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => personalCatalog.ListAsync());
    }

    [Theory]
    [InlineData("python")]
    [InlineData("cmd.exe")]
    [InlineData("./relative.sh")]
    [InlineData("..\\relative.exe")]
    [InlineData("\\\\untrusted-server\\share\\tool.exe")]
    public async Task XT04_NativeCommandRejectsPathDependentAndUncLookups(string command)
    {
        var catDir = Path.Combine(_tempRoot, "native_cat");
        var cat = new LocalToolCatalogProvider(catDir, ToolCatalogOwnershipMode.PersonalWorkstation);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            cat.StoreAsync(new ToolDefinition("bad_native", "EXECUTABLE",
                new Dictionary<string, string> { ["COMMAND"] = command }, false)));

        var mockCat = new Mock<IToolCatalogProvider>();
        mockCat.Setup(c => c.ResolveAsync("bad_native", It.IsAny<ExecutionIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDefinition("bad_native", "EXECUTABLE",
                new Dictionary<string, string> { ["COMMAND"] = command }, false));

        var ctx = new Mock<IExecutionContext>();
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        ctx.SetupGet(c => c.Connections).Returns(new Dictionary<string, IDataSource>());

        var factory = new RecordingProcessFactory();
        var handler = new ExecuteToolStatementHandler(new Mock<ILogger>().Object, mockCat.Object,
            new ConfigurationBuilder().Build(), processFactory: factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Execute(new ExecuteToolStatement("bad_native"), ctx.Object));
        Assert.Empty(factory.StartedCommands);
    }

    // ── XT05: Parameter roles & safe BuildArguments ──────────────────────────

    [Fact]
    public void XT05_BuildArgumentsPerformsSinglePassWithoutPlaceholderReexpansion()
    {
        var args = ExecuteToolStatementHandler.BuildArguments(
            "--first {A} --second {B} {EMPTY}",
            new Dictionary<string, string>
            {
                ["A"] = "{B}",
                ["B"] = "--exec evil",
                ["EMPTY"] = ""
            });

        Assert.Equal(["--first", "{B}", "--second", "--exec evil"], args);
    }

    [Theory]
    [InlineData("--exec=evil")]
    [InlineData("-e")]
    [InlineData("/c")]
    public void XT05_ValidateAndBuildRejectsOptionShapedValuesWhileAllowingNegativesAndPaths(string maliciousFlag)
    {
        var toolDir = Path.Combine(_tempRoot, "bin");
        Directory.CreateDirectory(toolDir);
        var binPath = Path.Combine(toolDir, "data_proc.exe");
        var handler = new ExecuteToolStatementHandler(new Mock<ILogger>().Object);
        var def = new ToolDefinition("data_proc", "EXECUTABLE", new Dictionary<string, string>
        {
            ["COMMAND"] = binPath,
            ["ARGS"] = "--offset {NUM} --input {FILE}",
            ["PARAM_ROLES"] = "NUM=NUMBER;FILE=PATH",
            ["SUPPORTS_END_OF_OPTIONS"] = "true"
        }, false);

        // Option-shaped parameter rejected.
        Assert.Throws<ExecutionException>(() => handler.ValidateAndBuildArguments(
            def, binPath, def.Options["ARGS"],
            new Dictionary<string, string> { ["NUM"] = "-42", ["FILE"] = maliciousFlag }));

        // Negative numbers and valid paths pass cleanly.
        var okArgs = handler.ValidateAndBuildArguments(
            def, binPath, "--offset {NUM} {FILE}",
            new Dictionary<string, string> { ["NUM"] = "-3.14", ["FILE"] = "/data/input.csv" });
        Assert.Equal(["--offset", "-3.14", "--", "/data/input.csv"], okArgs);
    }

    [Fact]
    public void XT05_InterpreterToolsBlockCodeSwitchesAndCallerScriptSubstitutionsUnlessAuthorized()
    {
        var pyPath = OperatingSystem.IsWindows() ? @"C:\Python\python3.exe" : "/usr/bin/python3";
        var handler = new ExecuteToolStatementHandler(new Mock<ILogger>().Object);

        var codeSwitchDef = new ToolDefinition("py_eval", "EXECUTABLE", new Dictionary<string, string>
        {
            ["COMMAND"] = pyPath,
            ["ARGS"] = "-c {CODE}"
        }, false);

        Assert.Throws<ExecutionException>(() => handler.ValidateAndBuildArguments(
            codeSwitchDef, pyPath, "-c {CODE}", new Dictionary<string, string> { ["CODE"] = "import os" }));

        var scriptSubDef = new ToolDefinition("py_script", "EXECUTABLE", new Dictionary<string, string>
        {
            ["COMMAND"] = pyPath,
            ["ARGS"] = "{SCRIPT}"
        }, false);

        Assert.Throws<ExecutionException>(() => handler.ValidateAndBuildArguments(
            scriptSubDef, pyPath, "{SCRIPT}", new Dictionary<string, string> { ["SCRIPT"] = "/tmp/pwn.py" }));

        // Fixed approved script followed by bounded data argument succeeds cleanly.
        var fixedScriptPath = Path.Combine(_tempRoot, "approved.py");
        var fixedScriptDef = new ToolDefinition("py_fixed", "EXECUTABLE", new Dictionary<string, string>
        {
            ["COMMAND"] = pyPath,
            ["ARGS"] = $"{fixedScriptPath} --format {{FMT}} {{INFILE}}",
            ["PARAM_ROLES"] = "FMT=ENUM(json,csv);INFILE=PATH",
            ["SUPPORTS_END_OF_OPTIONS"] = "true"
        }, false);

        var built = handler.ValidateAndBuildArguments(
            fixedScriptDef, pyPath, fixedScriptDef.Options["ARGS"],
            new Dictionary<string, string> { ["FMT"] = "csv", ["INFILE"] = @"C:\work\data.csv" });
        Assert.Equal([fixedScriptPath, "--format", "csv", "--", @"C:\work\data.csv"], built);
    }

    // ── XT06: Container resource limits & isolation profile checks ────────────

    [Fact]
    public async Task XT06_ContainerToolsEmitResourceCeilingsAndRejectOverBudgetOrUnenforcedProfiles()
    {
        var catalog = new Mock<IToolCatalogProvider>();
        catalog.Setup(c => c.ResolveAsync("bounded", It.IsAny<ExecutionIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDefinition("bounded", "CONTAINER", new Dictionary<string, string>
            {
                ["COMMAND"] = "worker",
                ["IMAGE"] = "approved.io/worker@sha256:" + new string('f', 64),
                ["MEMORY_MB"] = "256",
                ["CPUS"] = "0.5",
                ["PIDS_LIMIT"] = "64"
            }, false));

        var factory = new RecordingProcessFactory();
        var ctx = new Mock<IExecutionContext>();
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        ctx.SetupGet(c => c.Connections).Returns(new Dictionary<string, IDataSource>());

        var handler = new ExecuteToolStatementHandler(new Mock<ILogger>().Object, catalog.Object,
            new ConfigurationBuilder().Build(), processFactory: factory);

        await handler.Execute(new ExecuteToolStatement("bounded"), ctx.Object);

        var runArgs = factory.StartedCommands[0].ArgumentList;
        AssertArgumentPair(runArgs, "--cpus", "0.5");
        AssertArgumentPair(runArgs, "--memory", "256m");
        AssertArgumentPair(runArgs, "--memory-swap", "256m");
        AssertArgumentPair(runArgs, "--pids-limit", "64");

        // Reject tool requesting HardenedShared when no hardened OCI runtime is configured.
        catalog.Setup(c => c.ResolveAsync("claimed_hardened", It.IsAny<ExecutionIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDefinition("claimed_hardened", "CONTAINER", new Dictionary<string, string>
            {
                ["COMMAND"] = "worker",
                ["IMAGE"] = "approved.io/worker@sha256:" + new string('f', 64),
                ["ISOLATION_PROFILE"] = "HardenedShared"
            }, false));
        await Assert.ThrowsAsync<ExecutionException>(() =>
            handler.Execute(new ExecuteToolStatement("claimed_hardened"), ctx.Object));

        // Reject native EXECUTABLE tool claiming container/hostile isolation or running in tenant scope.
        var nativeExe = Path.Combine(_tempRoot, "native.exe");
        catalog.Setup(c => c.ResolveAsync("native_claimed", It.IsAny<ExecutionIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDefinition("native_claimed", "EXECUTABLE", new Dictionary<string, string>
            {
                ["COMMAND"] = nativeExe,
                ["ISOLATION_PROFILE"] = "HardenedShared"
            }, false));
        await Assert.ThrowsAsync<ExecutionException>(() =>
            handler.Execute(new ExecuteToolStatement("native_claimed"), ctx.Object));

        catalog.Setup(c => c.ResolveAsync("native_plain", It.IsAny<ExecutionIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDefinition("native_plain", "EXECUTABLE", new Dictionary<string, string>
            {
                ["COMMAND"] = nativeExe
            }, false));
        ctx.SetupGet(c => c.ExecutionIdentity).Returns(new ExecutionIdentity
        {
            EffectiveUser = "bob",
            RealUser = "bob",
            IsAdmin = false,
            TenantId = "tenant-a"
        });
        await Assert.ThrowsAsync<ExecutionException>(() =>
            handler.Execute(new ExecuteToolStatement("native_plain"), ctx.Object));
    }

    private static EffectiveEnterprisePolicy CreateEnrolledPolicy(string[] allowedDockerImages)
    {
        var document = new OrganizationPolicyDocument
        {
            Process = new ProcessPolicySection { AllowedDockerImages = allowedDockerImages }
        };
        return new EffectiveEnterprisePolicy(
            true,
            true,
            "Live",
            "v1",
            "test-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow,
            document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }

    private static void AssertArgumentPair(IList<string> args, string flag, string expectedValue)
    {
        var idx = args.IndexOf(flag);
        Assert.True(idx >= 0 && idx + 1 < args.Count, $"Expected flag '{flag}' in [{string.Join(", ", args)}]");
        Assert.Equal(expectedValue, args[idx + 1]);
    }

    [SupportedOSPlatform("windows")]
    private static void GrantWindowsUsersWrite(string directory)
    {
        var info = new DirectoryInfo(directory);
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (identity.User != null)
            {
                sec.AddAccessRule(new FileSystemAccessRule(
                    identity.User,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
        }
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(sec);
    }

    private sealed class RecordingProcessFactory : IToolProcessFactory
    {
        public List<ProcessStartInfo> StartedCommands { get; } = [];

        public IToolProcess Create(ProcessStartInfo start)
        {
            if (start.ArgumentList.Count == 0 || start.ArgumentList[0] == "run")
                StartedCommands.Add(start);
            return new StubToolProcess();
        }
    }

    private sealed class StubToolProcess : IToolProcess
    {
        public int ExitCode => 0;
        public bool HasExited => true;
        public StreamWriter StandardInput { get; } = new(new MemoryStream(), Encoding.UTF8);
        public StreamReader StandardOutput { get; } = new(new MemoryStream());
        public StreamReader StandardError { get; } = new(new MemoryStream());
        public void Start() { }
        public void Kill() { }
        public Task WaitForExitAsync(CancellationToken token) => Task.CompletedTask;
        public void Dispose() { StandardInput.Dispose(); StandardOutput.Dispose(); StandardError.Dispose(); }
    }
}
