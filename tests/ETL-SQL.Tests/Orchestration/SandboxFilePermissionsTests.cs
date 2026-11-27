using System;
using System.Collections.Generic;
using System.IO;
using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// The sandbox's writable mounts are shared with the orchestrator through its own group, not
    /// opened to every account.
    /// </summary>
    public class SandboxFilePermissionsTests
    {
        [Fact]
        public void SharedGroup_IsTheOrchestratorsGroup()
        {
            Assert.Equal(1000u, SandboxFilePermissions.SharedGroupFor(1000));
        }

        /// <summary>Joining gid 0 would hand the sandbox root's group, so root gets the fallback.</summary>
        [Fact]
        public void SharedGroup_IsNeverRootsGroup()
        {
            Assert.Null(SandboxFilePermissions.SharedGroupFor(0));
        }

        [Fact]
        public void Umask_ClosesOtherAccess_OnlyWhenThereIsASharedGroup()
        {
            Assert.Equal("0007", SandboxFilePermissions.UmaskFor(1000));
            Assert.Equal("0000", SandboxFilePermissions.UmaskFor(null));
        }

        /// <summary>
        /// A new directory takes its parent's group when the parent is setgid, and a reused session
        /// directory keeps whatever group it had. The leaf must be put in the shared group before it
        /// is opened to that group, or the sandbox joins one group and the leaf is shared with another.
        /// </summary>
        [Fact]
        public void ASharedLeaf_IsPutInTheSharedGroupBeforeItIsOpened()
        {
            var calls = new List<string>();

            SandboxFilePermissions.ShareLeafWithGroup("/leaf", 4242,
                (path, group) => { calls.Add($"group {path} {group}"); return 0; },
                (path, mode) => calls.Add($"mode {path} {mode}"));

            Assert.Equal(new[] { "group /leaf 4242", $"mode /leaf {SandboxFilePermissions.SharedLeafMode}" }, calls);
        }

        [Fact]
        public void ASharedLeaf_IsNotOpened_WhenItsGroupCannotBeSet()
        {
            var opened = false;

            Assert.Throws<InvalidOperationException>(() => SandboxFilePermissions.ShareLeafWithGroup("/leaf", 4242,
                (_, _) => -1,
                (_, _) => opened = true));
            Assert.False(opened);
        }

        [UnixFact]
        [Trait("Platform", "Unix")]
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        public void AWritableLeaf_IsSharedThroughTheGroupAndInheritsIt()
        {
            var group = SandboxFilePermissions.CurrentSharedGroupId;
            var leaf = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), $"etlsql-sandbox-leaf-{Guid.NewGuid():N}")).FullName;
            try
            {
                SandboxFilePermissions.AllowUnprivilegedSandboxWrites(leaf, group);

                var expected = group is null
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                      UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                      UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                      UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                      UnixFileMode.SetGroup;
                Assert.Equal(expected, File.GetUnixFileMode(leaf));
            }
            finally
            {
                Directory.Delete(leaf);
            }
        }
    }
}
