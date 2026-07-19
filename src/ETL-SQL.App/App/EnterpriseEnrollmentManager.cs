using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App;

internal static class EnterpriseEnrollmentManager
{
    public static async Task<int> RunAsync(CliContext context, ILogger logger)
        => await RunAsync(context, logger, new EnterpriseEnrollmentStore());

    internal static async Task<int> RunAsync(
        CliContext context,
        ILogger logger,
        EnterpriseEnrollmentStore store,
        Action? requireElevation = null,
        Func<EnterpriseEnrollmentStore, Task<EffectiveEnterprisePolicy>>? initializePolicy = null)
    {
        try
        {
            requireElevation ??= RequireElevation;
            initializePolicy ??= store => EnterprisePolicyRuntime.InitializeFromMachineAsync(store);
            return context.Command switch
            {
                "enterprise-enroll" => Enroll(context, store, logger, requireElevation),
                "enterprise-status" => await StatusAsync(store, logger, initializePolicy),
                "enterprise-unenroll" => Unenroll(context, store, logger, requireElevation),
                _ => throw new InvalidOperationException($"Unsupported enterprise command '{context.Command}'.")
            };
        }
        catch (Exception ex)
        {
            logger.WriteLine($"Enterprise enrollment failed: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
    }

    private static int Enroll(
        CliContext context,
        EnterpriseEnrollmentStore store,
        ILogger logger,
        Action requireElevation)
    {
        requireElevation();
        if (string.IsNullOrWhiteSpace(context.EnterpriseTenant)
            || string.IsNullOrWhiteSpace(context.EnterprisePolicyEndpoint)
            || string.IsNullOrWhiteSpace(context.EnterpriseSigningKeyPath))
            throw new InvalidOperationException("--tenant, --policy-endpoint, and --signing-key are required.");

        var keyPath = Path.GetFullPath(context.EnterpriseSigningKeyPath);
        if (!File.Exists(keyPath))
            throw new FileNotFoundException("Policy-signing public key was not found.", keyPath);

        var document = new EnterpriseEnrollmentDocument
        {
            Tenant = context.EnterpriseTenant.Trim(),
            PolicyEndpoint = context.EnterprisePolicyEndpoint.Trim(),
            PolicySigningPublicKey = File.ReadAllText(keyPath),
            ClientCertificateThumbprint = NormalizeOptional(context.EnterpriseClientCertificateThumbprint),
            ServiceIdentity = NormalizeOptional(context.EnterpriseServiceIdentity),
            MaxOfflineHours = context.EnterpriseMaxOfflineHours,
            FailClosed = !context.EnterpriseAllowOfflineFailure
        };
        store.Enroll(document);
        logger.WriteLine($"Machine enrolled in enterprise policy for tenant '{document.Tenant}'.", ConsoleColor.Green);
        logger.WriteLine($"Enrollment: {document.EnrollmentId}");
        logger.WriteLine($"Machine: {document.MachineId}");
        logger.WriteLine($"Policy endpoint: {document.PolicyEndpoint}");
        logger.WriteLine($"Protected bootstrap: {store.Path}");
        return 0;
    }

    private static async Task<int> StatusAsync(
        EnterpriseEnrollmentStore store,
        ILogger logger,
        Func<EnterpriseEnrollmentStore, Task<EffectiveEnterprisePolicy>> initializePolicy)
    {
        var status = store.GetStatus();
        if (!status.IsEnrolled)
        {
            logger.WriteLine("Enterprise enrollment: not enrolled (standalone mode).", ConsoleColor.Yellow);
            logger.WriteLine($"Bootstrap path: {status.Path}");
            return 0;
        }
        if (status.Enrollment is null)
        {
            logger.WriteLine("Enterprise enrollment: invalid or tampered.", ConsoleColor.Red);
            logger.WriteLine($"Bootstrap path: {status.Path}");
            logger.WriteLine($"Error: {status.Error}", ConsoleColor.Red);
            return 2;
        }

        var value = status.Enrollment;
        logger.WriteLine("Enterprise enrollment: active", ConsoleColor.Green);
        EffectiveEnterprisePolicy? policy = null;
        string? policyError = null;
        try { policy = await initializePolicy(store); }
        catch (Exception ex) { policyError = ex.Message; }
        logger.WriteLine(JsonSerializer.Serialize(new
        {
            value.SchemaVersion,
            value.EnrollmentId,
            value.MachineId,
            value.Tenant,
            value.PolicyEndpoint,
            policySigningKeyConfigured = true,
            clientCertificateConfigured = value.ClientCertificateThumbprint is not null,
            value.ServiceIdentity,
            value.MaxOfflineHours,
            value.FailClosed,
            value.EnrolledAtUtc,
            bootstrapPath = status.Path,
            policy = policy is null ? null : new
            {
                policy.Status,
                policy.IsAvailable,
                policy.PolicyVersion,
                policy.Source,
                policy.IssuedAtUtc,
                policy.ExpiresAtUtc,
                policy.LoadedAtUtc,
                governedKeys = policy.ConfigurationValues.Keys.OrderBy(key => key).ToArray(),
                warning = policy.Error
            },
            policyError
        }, new JsonSerializerOptions { WriteIndented = true }));
        return policyError is null && policy?.IsAvailable == true ? 0 : 2;
    }

    private static int Unenroll(
        CliContext context,
        EnterpriseEnrollmentStore store,
        ILogger logger,
        Action requireElevation)
    {
        requireElevation();
        if (!context.EnterpriseConfirm)
            throw new InvalidOperationException("Unenrollment requires --yes because it removes mandatory enterprise controls.");
        var removed = store.Unenroll();
        logger.WriteLine("Machine enterprise enrollment removed.", ConsoleColor.Yellow);
        logger.WriteLine(removed is null
            ? "The protected bootstrap was malformed and was removed during administrator recovery."
            : $"Removed enrollment: {removed.EnrollmentId}");
        return 0;
    }

    private static void RequireElevation()
    {
        if (!AdministrativePrivilege.IsElevated())
            throw new UnauthorizedAccessException(
                "Machine enterprise enrollment requires an elevated Administrator or root process.");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
