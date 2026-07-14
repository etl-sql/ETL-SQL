using System.IO.Compression;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Moq;

namespace ETL_SQL.Tests.Core;

public sealed class FileSystemPolicyAuthorizerTests
{
    [Fact]
    public void Authorize_EnforcesEnterpriseRootsAndReturnsSanitizedDecision()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_root_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"policy_outside_{Guid.NewGuid():N}", "secret.csv");
        Directory.CreateDirectory(root);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash", correlationId: "corr-1"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var allowed = authorizer.Authorize(context.Object, Path.Combine(root, "data.csv"),
                FileSystemAccessKind.Read);
            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, outside, FileSystemAccessKind.Read));

            Assert.True(allowed.Decision.IsAllowed);
            Assert.Equal("corr-1", denied.Decision.CorrelationId);
            Assert.Equal("<path>/secret.csv", denied.Decision.RequestedTarget);
            Assert.DoesNotContain(Path.GetDirectoryName(outside)!, denied.Decision.RequestedTarget,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Authorize_RejectsNonCanonicalWindowsForms()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), $"policy_forms_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            // NTFS alternate data stream inside the approved root: passes prefix checks but
            // evades extension checks — must be rejected on form.
            var ads = Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, Path.Combine(root, "data.csv:hidden"), FileSystemAccessKind.Write,
                validateFileType: false));
            Assert.Contains("Alternate data stream", ads.Decision.Reason);

            // Extended-length/device namespace prefixes skip Win32 normalization.
            var device = Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, @"\\?\" + Path.Combine(root, "data.csv"), FileSystemAccessKind.Read,
                validateFileType: false));
            Assert.Contains("namespace path prefixes", device.Decision.Reason);

            // Win32 strips trailing dots/spaces at open time, so 'pipeline.etlsql ' would bypass
            // the script-immutability write check yet still write pipeline.etlsql.
            var trailing = Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, Path.Combine(root, "pipeline.etlsql "), FileSystemAccessKind.Write,
                validateFileType: false));
            Assert.Contains("dot or space", trailing.Decision.Reason);
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Authorize_WriteRechecksScriptImmutability()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_write_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                new FileSystemPolicyAuthorizer(security).Authorize(context.Object,
                    Path.Combine(root, "pipeline.etlsql"), FileSystemAccessKind.Write));

            Assert.Equal("Filesystem:Write", denied.Decision.PolicyKey);
            Assert.Contains("immutability", denied.Decision.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void OpenValidated_AllowsGenuineTargetAndTruncatesAfterValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_handle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var file = Path.Combine(root, "genuine.txt");
            File.WriteAllText(file, "stale content that must be truncated");
            var authorized = authorizer.Authorize(context.Object, file, FileSystemAccessKind.Write,
                validateFileType: false);

            using (var writer = new StreamWriter(authorizer.OpenValidatedWrite(context.Object, authorized)))
                writer.Write("fresh");

            var readAuth = authorizer.Authorize(context.Object, file, FileSystemAccessKind.Read,
                validateFileType: false);
            using var reader = new StreamReader(authorizer.OpenValidatedRead(context.Object, readAuth));
            Assert.Equal("fresh", reader.ReadToEnd());
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void OpenValidated_DetectsLinkSubstitutionAfterAuthorization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_race_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"policy_race_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            var canonical = Path.Combine(sub, "swap.txt");
            File.WriteAllText(canonical, "authorized");
            var authorized = authorizer.Authorize(context.Object, canonical, FileSystemAccessKind.Write,
                validateFileType: false);

            // Simulate the check/use race: after authorization, a parent directory is replaced
            // with a link pointing outside the approved root. On Windows a junction is used
            // because (unlike symlinks) creating one requires no privilege.
            var target = Path.Combine(outside, "swap.txt");
            File.WriteAllText(target, "protected");
            Directory.Delete(sub, recursive: true);
            if (OperatingSystem.IsWindows())
            {
                using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "cmd.exe", $"/c mklink /J \"{sub}\" \"{outside}\"")
                { UseShellExecute = false, CreateNoWindow = true });
                mklink!.WaitForExit();
                Assert.Equal(0, mklink.ExitCode);
            }
            else
            {
                Directory.CreateSymbolicLink(sub, outside);
            }

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                authorizer.OpenValidatedWrite(context.Object, authorized));
            Assert.Contains("final path", denied.Decision.Reason);
            // Validation happens before truncation, so the link's target survives intact.
            Assert.Equal("protected", File.ReadAllText(target));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DeleteValidatedFile_DetectsJunctionSubstitutionAfterAuthorization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_delete_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"policy_delete_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            var canonical = Path.Combine(sub, "swap.txt");
            File.WriteAllText(canonical, "authorized");
            var authorized = authorizer.Authorize(context.Object, canonical, FileSystemAccessKind.Delete,
                validateFileType: false);

            var outsideTarget = Path.Combine(outside, "swap.txt");
            File.WriteAllText(outsideTarget, "protected");
            Directory.Delete(sub, recursive: true);
            CreateDirectoryLink(sub, outside);

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                authorizer.DeleteValidatedFile(context.Object, authorized));
            Assert.Contains("final path", denied.Decision.Reason);
            Assert.Equal("protected", File.ReadAllText(outsideTarget));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void MoveValidatedFile_DetectsJunctionSubstitutionAfterAuthorization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_move_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"policy_move_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            var canonical = Path.Combine(sub, "move.txt");
            var destinationPath = Path.Combine(root, "moved.txt");
            File.WriteAllText(canonical, "authorized");
            var source = authorizer.Authorize(context.Object, canonical, FileSystemAccessKind.Move,
                validateFileType: false);
            var destination = authorizer.Authorize(context.Object, destinationPath, FileSystemAccessKind.Move,
                validateFileType: false);

            var outsideTarget = Path.Combine(outside, "move.txt");
            File.WriteAllText(outsideTarget, "protected");
            Directory.Delete(sub, recursive: true);
            CreateDirectoryLink(sub, outside);

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                authorizer.MoveValidatedFile(context.Object, source, destination, overwrite: true));
            Assert.Contains("final path", denied.Decision.Reason);
            Assert.Equal("protected", File.ReadAllText(outsideTarget));
            Assert.False(File.Exists(destinationPath));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void MoveValidatedFile_OverwriteDestinationDetectsJunctionSubstitutionAfterAuthorization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_move_overwrite_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"policy_move_overwrite_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var sourcePath = Path.Combine(root, "source.txt");
            File.WriteAllText(sourcePath, "authorized");
            var destDir = Path.Combine(root, "dest");
            Directory.CreateDirectory(destDir);
            var destinationPath = Path.Combine(destDir, "existing.txt");
            File.WriteAllText(destinationPath, "authorized-destination");
            var source = authorizer.Authorize(context.Object, sourcePath, FileSystemAccessKind.Move,
                validateFileType: false);
            var destination = authorizer.Authorize(context.Object, destinationPath, FileSystemAccessKind.Move,
                validateFileType: false);

            var outsideDestination = Path.Combine(outside, "existing.txt");
            File.WriteAllText(outsideDestination, "protected");
            Directory.Delete(destDir, recursive: true);
            CreateDirectoryLink(destDir, outside);

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                authorizer.MoveValidatedFile(context.Object, source, destination, overwrite: true));
            Assert.Contains("canonical target", denied.Decision.Reason);
            Assert.Equal("protected", File.ReadAllText(outsideDestination));
            Assert.True(File.Exists(sourcePath));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DeleteValidatedDirectory_DetectsJunctionSubstitutionAfterAuthorization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_dir_delete_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"policy_dir_delete_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var sub = Path.Combine(root, "sub");
            var canonical = Path.Combine(sub, "victim");
            Directory.CreateDirectory(canonical);
            File.WriteAllText(Path.Combine(canonical, "data.txt"), "authorized");
            var authorized = authorizer.Authorize(context.Object, canonical, FileSystemAccessKind.Delete,
                validateFileType: false);

            var outsideVictim = Path.Combine(outside, "victim");
            Directory.CreateDirectory(outsideVictim);
            File.WriteAllText(Path.Combine(outsideVictim, "data.txt"), "protected");
            Directory.Delete(sub, recursive: true);
            CreateDirectoryLink(sub, outside);

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                authorizer.DeleteValidatedDirectory(context.Object, authorized, recursive: true));
            Assert.Contains("canonical target", denied.Decision.Reason);
            Assert.True(Directory.Exists(outsideVictim));
            Assert.Equal("protected", File.ReadAllText(Path.Combine(outsideVictim, "data.txt")));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void MoveValidatedDirectory_DetectsJunctionSubstitutionAfterAuthorization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_dir_move_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"policy_dir_move_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var sub = Path.Combine(root, "sub");
            var canonical = Path.Combine(sub, "victim");
            Directory.CreateDirectory(canonical);
            File.WriteAllText(Path.Combine(canonical, "data.txt"), "authorized");
            var source = authorizer.Authorize(context.Object, canonical, FileSystemAccessKind.Move,
                validateFileType: false);
            var destinationPath = Path.Combine(root, "moved-victim");
            var destination = authorizer.Authorize(context.Object, destinationPath, FileSystemAccessKind.Move,
                validateFileType: false);

            var outsideVictim = Path.Combine(outside, "victim");
            Directory.CreateDirectory(outsideVictim);
            File.WriteAllText(Path.Combine(outsideVictim, "data.txt"), "protected");
            Directory.Delete(sub, recursive: true);
            CreateDirectoryLink(sub, outside);

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                authorizer.MoveValidatedDirectory(context.Object, source, destination, overwrite: true));
            Assert.Contains("canonical target", denied.Decision.Reason);
            Assert.True(Directory.Exists(outsideVictim));
            Assert.False(Directory.Exists(destinationPath));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SafeZipExtractor_RejectsTraversalBeforeWritingEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_zip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "payload.zip");
        var destination = Path.Combine(root, "output");
        var escaped = Path.Combine(root, "escape.csv");
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escape.csv");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("blocked");
            }

            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() => SafeZipExtractor.Extract(
                archivePath, destination, overwrite: false, context.Object,
                new FileSystemPolicyAuthorizer(security)));

            Assert.Equal("Filesystem:ArchiveExtraction", denied.Decision.PolicyKey);
            Assert.False(File.Exists(escaped));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static Mock<IExecutionContext> Context(
        SecurityService security,
        ExecutionPolicySnapshot snapshot)
    {
        var context = new Mock<IExecutionContext>();
        context.SetupProperty(value => value.ExecutionPolicy, snapshot);
        context.SetupGet(value => value.SecurityService).Returns(security);
        context.SetupGet(value => value.AllowedFileTypeOverrides)
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        context.SetupGet(value => value.AllowUnknownFileTypes).Returns(false);
        context.SetupGet(value => value.InteractiveMode).Returns(false);
        return context;
    }

    private static EffectiveEnterprisePolicy EffectivePolicy(string root)
    {
        var document = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = [root] }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            { UseShellExecute = false, CreateNoWindow = true });
            mklink!.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
    }
}
