using System.Collections.Generic;
using System.IO;

namespace ETL_SQL.Core;
/// <summary>
/// Carries the parsed CLI arguments and settings for the current command invocation.
/// Lives in Core so both App (headless executor) and TUI can share the same type
/// without creating a circular project dependency.
/// </summary>
public class CliContext
{
    public string Command { get; set; } = "run";
    public FileInfo? ScriptFile { get; set; }
    public bool IsPerfMode { get; set; }
    public int BatchSize { get; set; }
    public bool IsGenerateMode => Command == "generate";
    public bool IsLogMode { get; set; }
    public string? LogPath { get; set; }
    public bool IsSilentMode { get; set; }
    public string? UiMode { get; set; }
    public int EstimatedRows { get; set; }
    public bool IsVerbose { get; set; }
    public string? TestVal { get; set; }
    public bool IsTestMode => Command == "test";
    public string? PreviewVal { get; set; }
    public string? DocsVal { get; set; }
    public string? Password { get; set; }
    public string? EncryptValue { get; set; }
    public bool IsJsonMode { get; set; }
    public bool EnablePaging { get; set; }
    public bool DisplayProgress { get; set; }
    public string SessionId { get; set; } = System.Guid.NewGuid().ToString("N");
    public bool Resume { get; set; }
    public bool UpdateConfig { get; set; }
    public Dictionary<string, object?> Variables { get; } = new(System.StringComparer.OrdinalIgnoreCase);

    // serve command
    public string? ServeManifest { get; set; }
    public int? ServePort { get; set; }
    public bool ServeNoBrowser { get; set; }

    // doctor command
    public bool DoctorStrict { get; set; }
    public string DoctorProfile { get; set; } = "quick";

    // purge command
    public bool PurgeDryRun { get; set; }
    public bool PurgeYes { get; set; }

    // gen-script command
    public string? SpecSchema { get; set; }
    public string? SpecOutput { get; set; }

    // extract-spec command
    public string? ExtractInput { get; set; }
    public string? ExtractOutput { get; set; }

    // admin support-bundle command
    public string? BundleOutput { get; set; }

    // init command
    public string? InitDirectory { get; set; }
    public bool InitForce { get; set; }

    // admin backup command
    public string? BackupOutputDir { get; set; }

    // admin restore command
    public string? RestoreFrom { get; set; }
    public string? RestoreKeys { get; set; }
    public string? RestoreTo { get; set; }
    public bool RestoreValidateOnly { get; set; }

    // admin migrate-database command
    public string? MigrateFrom { get; set; }
    public string? MigrateTo { get; set; }
    public bool MigrateDryRun { get; set; }

    // admin secret lifecycle commands
    public string? SecretName { get; set; }
    public string? SecretValue { get; set; }

    // enterprise enrollment commands
    public string? EnterpriseTenant { get; set; }
    public string? EnterprisePolicyEndpoint { get; set; }
    public string? EnterpriseSigningKeyPath { get; set; }
    public string? EnterpriseClientCertificateThumbprint { get; set; }
    public string? EnterpriseServiceIdentity { get; set; }
    public int EnterpriseMaxOfflineHours { get; set; } = 24;
    public bool EnterpriseAllowOfflineFailure { get; set; }
    public bool EnterpriseConfirm { get; set; }
}
