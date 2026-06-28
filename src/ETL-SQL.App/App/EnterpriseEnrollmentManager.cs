using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App;

internal static class EnterpriseEnrollmentManager
{
    public static Task<int> RunAsync(CliContext context, ILogger logger)
    {
        try
        {
            var store = new EnterpriseEnrollmentStore();
            return Task.FromResult(context.Command switch
            {
                "enterprise-enroll" => Enroll(context, store, logger),
                "enterprise-status" => Status(store, logger),
                "enterprise-unenroll" => Unenroll(context, store, logger),
                _ => throw new InvalidOperationException($"Unsupported enterprise command '{context.Command}'.")
            });
        }
        catch (Exception ex)
        {
            logger.WriteLine($"Enterprise enrollment failed: {ex.Message}", ConsoleColor.Red);
            return Task.FromResult(1);
        }
    }

    private static int Enroll(CliContext context, EnterpriseEnrollmentStore store, ILogger logger)
    {
        RequireElevation();
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

    private static int Status(EnterpriseEnrollmentStore store, ILogger logger)
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
            bootstrapPath = status.Path
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static int Unenroll(CliContext context, EnterpriseEnrollmentStore store, ILogger logger)
    {
        RequireElevation();
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
