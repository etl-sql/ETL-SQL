using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace ETL_SQL.Common;

public sealed record InstalledBootstrapOptions(
    string BaseDirectory,
    string? RegisteredInstallFolder,
    string MutexName = InstalledConfigurationBootstrap.DefaultMutexName);

[Flags]
public enum InstalledBootstrapResult
{
    Skipped = 0,
    Ran = 1,
    SecretsMoved = 2,
    SecretsGenerated = 4,
    Slimmed = 8,
    FirstRunWritten = 16,
}

/// <summary>
/// Prepares an MSI install's configuration when the Orchestrator or Portal starts (MI01), so the
/// installer runs no script: generates the missing service secrets into
/// <see cref="ConfigurationExtensions.InstalledSecretsFileName"/>, moves any secret found in the
/// shared appsettings.json there, and publishes the one-time sign-in for a fresh Portal. Runs only
/// for the host the installer recorded; a developer or container run is never touched, so a missing
/// secret there still fails fast.
/// </summary>
public static class InstalledConfigurationBootstrap
{
    public const string DefaultMutexName = @"Global\ETL-SQL-InstalledConfiguration";
    public const string FailureMessage = "ETL-SQL installed configuration could not be prepared.";
    public const string FirstRunFileName = "first-run-password.txt";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static InstalledBootstrapResult Ensure(string baseDirectory)
    {
        var values = WindowsInstallerRegistry.Instance.ReadInstallValues();
        string? registered = null;
        values?.TryGetValue(InstallerRegistry.InstallFolder, out registered);
        return Ensure(new InstalledBootstrapOptions(baseDirectory, registered));
    }

    public static InstalledBootstrapResult Ensure(InstalledBootstrapOptions options)
    {
        if (!OperatingSystem.IsWindows() || options.RegisteredInstallFolder is null) return InstalledBootstrapResult.Skipped;
        var recorded = new Dictionary<string, string> { [InstallerRegistry.InstallFolder] = options.RegisteredInstallFolder };
        if (InstallerRegistry.MatchingInstallFolder(recorded, options.BaseDirectory) is null) return InstalledBootstrapResult.Skipped;

        var stage = "lock";
        using var mutex = new Mutex(initiallyOwned: false, options.MutexName);
        var owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromMinutes(2)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new TimeoutException();
            return Prepare(Path.GetFullPath(options.BaseDirectory), ref stage);
        }
        catch (Exception ex)
        {
            // Exception messages can quote settings or secrets: keep only the stage and the type.
            throw new InvalidOperationException(FailureMessage,
                new IOException($"Stage '{stage}' failed with {ex.GetType().Name} (HRESULT 0x{ex.HResult:X8})."));
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static InstalledBootstrapResult Prepare(string directory, ref string stage)
    {
        var result = InstalledBootstrapResult.Ran;
        var operatorPath = Path.Combine(directory, "appsettings.json");
        var secretsPath = Path.Combine(directory, ConfigurationExtensions.InstalledSecretsFileName);
        var defaultsPath = Path.Combine(directory, ConfigurationExtensions.DefaultsFileName);

        stage = "read";
        var operatorFile = File.Exists(operatorPath) ? ReadObject(operatorPath) : new JsonObject();
        var secrets = File.Exists(secretsPath) ? ReadObject(secretsPath) : new JsonObject();
        var defaults = File.Exists(defaultsPath) ? ReadObject(defaultsPath) : new JsonObject();
        var secretsChanged = false;

        stage = "move";
        var operatorChanged = false;
        foreach (var path in ConfigurationExtensions.InstalledSecretPaths)
        {
            var (parent, key) = Locate(operatorFile, path);
            if (parent is null || !parent.TryGetPropertyValue(key, out var value) || IsEmpty(value)) continue;
            if (IsEmpty(Get(secrets, path)))
            {
                Set(secrets, path, value!.DeepClone());
                secretsChanged = true;
            }
            parent[key] = value switch
            {
                JsonArray => new JsonArray(),
                JsonObject => new JsonObject(),
                _ => JsonValue.Create(string.Empty),
            };
            operatorChanged = true;
            result |= InstalledBootstrapResult.SecretsMoved;
        }

        stage = "generate";
        foreach (var path in new[] { "Portal:Jwt:Secret", "Portal:Dataset:AtRestKey" })
        {
            if (!IsEmpty(Get(secrets, path))) continue;
            Set(secrets, path, NewSecret());
            secretsChanged = true;
            result |= InstalledBootstrapResult.SecretsGenerated;
        }
        // The Portal calls the Orchestrator with these: both sides must hold the same value.
        foreach (var name in new[] { "ApiKey", "IdentitySigningSecret" })
        {
            var service = $"Orchestrator:{name}";
            var client = $"Portal:Orchestrator:{name}";
            var shared = FirstNonEmpty(Get(secrets, service), Get(secrets, client));
            if (shared is null)
            {
                shared = NewSecret();
                result |= InstalledBootstrapResult.SecretsGenerated;
            }
            foreach (var side in new[] { service, client })
            {
                if (!IsEmpty(Get(secrets, side))) continue;
                Set(secrets, side, shared);
                secretsChanged = true;
            }
        }

        string? signIn = null;
        var signInPath = Path.Combine(directory, FirstRunFileName);
        if (!File.Exists(Path.Combine(directory, "portal.db")) && !File.Exists(signInPath))
        {
            var password = AsString(Get(secrets, "Portal:FirstRun:AdminPassword"));
            if (string.IsNullOrWhiteSpace(password))
            {
                password = NewPassword();
                Set(secrets, "Portal:FirstRun:AdminPassword", password);
                secretsChanged = true;
                result |= InstalledBootstrapResult.SecretsGenerated;
            }
            var user = FirstNonEmpty(Get(operatorFile, "Portal:FirstRun:AdminUsername"), Get(defaults, "Portal:FirstRun:AdminUsername")) ?? "admin";
            signIn = user + "\r\n" + password + "\r\n";
        }

        // Secrets first: a failure here must not publish a sign-in that cannot work.
        if (secretsChanged)
        {
            stage = "write-secrets";
            ProtectedInstallFile.Write(secretsPath, Utf8(secrets.ToJsonString(WriteOptions)));
        }
        if (operatorChanged)
        {
            stage = "write-settings";
            ReplaceKeepingAcl(operatorPath, Utf8(operatorFile.ToJsonString(WriteOptions) + Environment.NewLine));
        }
        if (signIn is not null)
        {
            stage = "write-sign-in";
            ProtectedInstallFile.Write(signInPath, Utf8(signIn));
            result |= InstalledBootstrapResult.FirstRunWritten;
        }
        return result;
    }

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject
        ?? throw new InvalidDataException();

    private static (JsonObject? Parent, string Key) Locate(JsonObject root, string path)
    {
        var parts = path.Split(':');
        JsonObject? node = root;
        for (var i = 0; i < parts.Length - 1 && node is not null; i++)
            node = node[parts[i]] as JsonObject;
        return (node, parts[^1]);
    }

    private static JsonNode? Get(JsonObject root, string path)
    {
        var (parent, key) = Locate(root, path);
        return parent?[key];
    }

    private static void Set(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split(':');
        var node = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (node[parts[i]] is not JsonObject child)
            {
                child = new JsonObject();
                node[parts[i]] = child;
            }
            node = child;
        }
        node[parts[^1]] = value;
    }

    private static bool IsEmpty(JsonNode? value) => value switch
    {
        null => true,
        JsonArray array => array.Count == 0,
        JsonObject obj => obj.Count == 0,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetValue<string>()),
        JsonValue v => v.GetValueKind() == JsonValueKind.Null,
        _ => false,
    };

    private static string? AsString(JsonNode? value) =>
        value is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static string? FirstNonEmpty(params JsonNode?[] values)
    {
        foreach (var value in values)
        {
            var text = AsString(value);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    // Same shape as the installer script generated: the Portal's password rules accept it.
    private static string NewPassword() => NewSecret()[..24].Replace('/', 'x').Replace('+', 'y') + "9!";

    private static byte[] Utf8(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    private static void ReplaceKeepingAcl(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
