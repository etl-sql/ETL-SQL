[CmdletBinding(DefaultParameterSetName = 'Verify')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][string] $FixtureParent,
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][string] $FirstExecutable,
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][ValidatePattern('^[a-fA-F0-9]{64}$')][string] $FirstSha256,
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][string] $FirstMarker,
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][string] $SecondExecutable,
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][ValidatePattern('^[a-fA-F0-9]{64}$')][string] $SecondSha256,
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][string] $SecondMarker,
    [Parameter(ParameterSetName = 'Prepare')][string] $WorkerAccount,
    [Parameter(Mandatory, ParameterSetName = 'Activate')]
    [Parameter(Mandatory, ParameterSetName = 'Verify')][string] $FixtureRoot,
    [Parameter(Mandatory, ParameterSetName = 'Activate')][ValidateSet(1, 2)][int] $Generation,
    [switch] $MechanicsOnly,
    [Parameter(ParameterSetName = 'Verify')][switch] $AsJson
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The XT08 qualification fixture requires PowerShell 7 on Windows.' }

$contract = 'etlsql-xt08-windows-fixture-v1'
. (Join-Path $PSScriptRoot 'lib/windows-tool-artifact-fixture.ps1')

function Read-FixtureJson([string] $Path) {
    $file = Resolve-FixtureItem $Path $false
    $stream = [IO.File]::Open($file.FullName, 'Open', 'Read', [IO.FileShare]::Read -bor [IO.FileShare]::Delete)
    try {
        $reader = [IO.StreamReader]::new($stream)
        try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    } finally {
        $stream.Dispose()
    }
}

function Move-FixtureSelection([string] $Source, [string] $Destination) {
    if (-not ('Etlsql.WindowsFixtureSelection' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Etlsql;

public static class WindowsFixtureSelection
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RenameInfo
    {
        public uint Flags;
        public IntPtr RootDirectory;
        public uint FileNameLength;
        public char FileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass,
        byte[] information, uint size);

    public static void Replace(string source, string destination)
    {
        const uint DeleteAccess = 0x10000;
        const uint ShareReadWriteDelete = 7;
        const uint OpenExisting = 3;
        const int FileRenameInfoEx = 22;
        const uint ReplaceIfExists = 1;
        const uint PosixSemantics = 2;

        using var handle = CreateFileW(source, DeleteAccess, ShareReadWriteDelete, IntPtr.Zero,
            OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var name = Encoding.Unicode.GetBytes(destination);
        var lengthOffset = Marshal.OffsetOf<RenameInfo>(nameof(RenameInfo.FileNameLength)).ToInt32();
        var nameOffset = Marshal.OffsetOf<RenameInfo>(nameof(RenameInfo.FileName)).ToInt32();
        var information = new byte[nameOffset + name.Length + sizeof(char)];
        BinaryPrimitives.WriteUInt32LittleEndian(information, ReplaceIfExists | PosixSemantics);
        BinaryPrimitives.WriteUInt32LittleEndian(information.AsSpan(lengthOffset), (uint)name.Length);
        name.CopyTo(information, nameOffset);

        if (!SetFileInformationByHandle(handle, FileRenameInfoEx, information, (uint)information.Length))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
'@
    }
    [Etlsql.WindowsFixtureSelection]::Replace($Source, $Destination)
}

function Publish-Selection([IO.DirectoryInfo] $Root, [int] $Index, [Security.Principal.SecurityIdentifier] $Reader) {
    $catalog = Resolve-FixtureItem (Join-Path $Root.FullName 'catalog') $true
    $final = Join-Path $catalog.FullName 'active.json'
    if ([IO.Path]::Exists($final)) {
        Assert-PublisherFixture (Resolve-FixtureItem $final $false) $Root.FullName
    }
    $staged = Join-Path $catalog.FullName ([guid]::NewGuid().ToString('N') + '.tmp')
    $stream = [IO.File]::Open($staged, 'CreateNew', 'Write', 'None')
    try {
        try {
            $bytes = [Text.Encoding]::UTF8.GetBytes((@{ contract = $contract; generation = $Index } | ConvertTo-Json -Compress))
            $stream.Write($bytes)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
        Protect-FixtureItem ([IO.FileInfo]::new($staged)) $Reader
        Move-FixtureSelection $staged $final
    } finally {
        if ([IO.File]::Exists($staged)) { [IO.File]::Delete($staged) }
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Prepare') {
    Assert-FixturePublisher
    if ($FirstSha256 -ieq $SecondSha256 -or $FirstMarker -ceq $SecondMarker) {
        throw 'Prepare two distinct reviewed executable digests and output markers.'
    }
    $parent = Resolve-FixtureItem $FixtureParent $true
    $readerSid = $null
    if (-not $MechanicsOnly) {
        if ([string]::IsNullOrWhiteSpace($WorkerAccount)) { throw 'Specify the dedicated Windows service account.' }
        Assert-PublisherFixture $parent $parent.FullName
        $readerSid = [Security.Principal.NTAccount]::new($WorkerAccount).Translate([Security.Principal.SecurityIdentifier])
        if ($readerSid -in @($adminSid, $systemSid)) { throw 'Choose a dedicated non-administrator worker account.' }
    }

    $versions = @(
        @{ source = (Resolve-FixtureItem $FirstExecutable $false).FullName; sha256 = $FirstSha256.ToLowerInvariant(); marker = $FirstMarker },
        @{ source = (Resolve-FixtureItem $SecondExecutable $false).FullName; sha256 = $SecondSha256.ToLowerInvariant(); marker = $SecondMarker }
    )
    foreach ($entry in $versions) {
        if ([IO.Path]::GetExtension($entry.source) -ine '.exe' -or
            [string]::IsNullOrWhiteSpace($entry.marker) -or
            (Get-FileHash -LiteralPath $entry.source -Algorithm SHA256).Hash -ine $entry.sha256) {
            throw 'Both fixtures must be reviewed .exe files with matching SHA-256 digests and expected output.'
        }
    }
    $root = [IO.Directory]::CreateDirectory((Join-Path $parent.FullName ('xt08-qualification-' + [guid]::NewGuid().ToString('N'))))
    Protect-FixtureItem $root $readerSid
    foreach ($relative in 'artifacts', 'artifacts\fixture', 'catalog') {
        $item = [IO.Directory]::CreateDirectory((Join-Path $root.FullName $relative))
        Protect-FixtureItem $item $readerSid
    }
    foreach ($entry in $versions) {
        $alias = Join-Path $root.FullName 'artifacts\fixture'
        $staged = [IO.Directory]::CreateDirectory((Join-Path $alias ([guid]::NewGuid().ToString('N') + '.tmp')))
        Protect-FixtureItem $staged $readerSid
        $file = Join-Path $staged.FullName 'fixture.exe'
        [IO.File]::Copy($entry.source, $file)
        Protect-FixtureItem ([IO.FileInfo]::new($file)) $readerSid
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry.sha256) {
            throw 'Staged fixture SHA-256 does not match the reviewed digest.'
        }
        [IO.Directory]::Move($staged.FullName, (Join-Path $alias $entry.sha256))
        $entry.Remove('source')
    }
    $plan = Join-Path $root.FullName 'catalog\fixture.json'
    @{
        contract = $contract
        mechanicsOnly = $MechanicsOnly.IsPresent
        workerAccount = $WorkerAccount
        versions = $versions
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $plan -Encoding utf8NoBOM
    Protect-FixtureItem ([IO.FileInfo]::new($plan)) $readerSid
    Publish-Selection $root 1 $readerSid
    [pscustomobject]@{ FixtureRoot = $root.FullName; ActiveGeneration = 1; MechanicsOnly = $MechanicsOnly.IsPresent }
    return
}

$root = Resolve-FixtureItem $FixtureRoot $true
if ($root.Name -notmatch '^xt08-qualification-[a-f0-9]{32}$') {
    throw 'Expected a disposable root created by the qualification publisher.'
}
$planPath = Join-Path $root.FullName 'catalog\fixture.json'
if ($PSCmdlet.ParameterSetName -eq 'Activate') {
    Assert-FixturePublisher
    Assert-PublisherFixture (Resolve-FixtureItem $planPath $false) $root.FullName
}
$plan = Read-FixtureJson $planPath
if ($plan.contract -cne $contract -or $plan.mechanicsOnly -ne $MechanicsOnly.IsPresent -or $plan.versions.Count -ne 2) {
    throw 'The fixture format or mode does not match this invocation.'
}
foreach ($entry in $plan.versions) {
    if ($entry.sha256 -cnotmatch '^[a-f0-9]{64}$' -or [string]::IsNullOrWhiteSpace($entry.marker)) {
        throw 'The fixture has an invalid reviewed generation.'
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Activate') {
    $readerSid = if (-not $MechanicsOnly) {
        [Security.Principal.NTAccount]::new($plan.workerAccount).Translate([Security.Principal.SecurityIdentifier])
    }
    $expected = $plan.versions[$Generation - 1].sha256
    $path = Join-Path $root.FullName "artifacts\fixture\$expected\fixture.exe"
    $file = Resolve-FixtureItem $path $false
    Assert-PublisherFixture $file $root.FullName
    if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ine $expected) {
        throw 'The selected generation no longer matches its reviewed digest. The current selection was preserved.'
    }
    Publish-Selection $root $Generation $readerSid
    [pscustomobject]@{ FixtureRoot = $root.FullName; ActiveGeneration = $Generation; MechanicsOnly = $MechanicsOnly.IsPresent }
    return
}

$activePath = Join-Path $root.FullName 'catalog\active.json'
$active = Read-FixtureJson $activePath
if ($active.contract -cne $contract -or ($active.generation -ne 1 -and $active.generation -ne 2)) {
    throw 'The fixture selection is invalid.'
}
$selected = $plan.versions[$active.generation - 1]
$artifactRoot = Join-Path $root.FullName 'artifacts'
$parameters = @{
    ArtifactRoot = $artifactRoot
    Executable = Join-Path $artifactRoot "fixture\$($selected.sha256)\fixture.exe"
    ExpectedSha256 = $selected.sha256
    ExpectedOutput = $selected.marker
}
if ($MechanicsOnly) {
    $parameters.MechanicsOnly = $true
} else {
    $parameters.WorkerAccount = $plan.workerAccount
    $parameters.CatalogEntry = $activePath
    $parameters.CatalogPlan = $planPath
}
$result = & (Join-Path $PSScriptRoot 'Test-WindowsToolArtifactBoundary.ps1') @parameters
$result | Add-Member -NotePropertyName ActiveGeneration -NotePropertyValue $active.generation
if ($AsJson) { $result | ConvertTo-Json -Compress } else { $result }
