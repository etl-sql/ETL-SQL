# Enterprise Machine Enrollment

Enterprise policy is opt-in. When no machine enrollment exists, ETL-SQL remains in standalone mode:
it uses local configuration, requires no policy-server connection, and applies only its built-in safety
controls. Enterprise enrollment is deliberately stored outside `appsettings.json`, environment variables,
and command-line configuration so those lower-authority sources cannot disable it.

Generate or obtain the organization's RSA policy-signing key pair and place only the public PEM file on
the machine being enrolled. Run enrollment from an elevated Administrator or root shell:

```powershell
etl-sql enterprise enroll `
  --tenant corp-production `
  --policy-endpoint https://policy.example.com/etl-sql/policy `
  --signing-key C:\Install\etl-sql-policy-public.pem `
  --client-certificate-thumbprint 0123456789ABCDEF0123456789ABCDEF01234567 `
  --service-identity "NT SERVICE\ETL-SQL" `
  --max-offline-hours 24
```

The policy endpoint must be HTTPS without embedded credentials. The signing key must be RSA PEM with at
least 2048 bits. The optional certificate thumbprint identifies the machine credential presented to the
policy endpoint. `--service-identity` grants that Windows service identity read access to enrollment and
write access only to the separate protected policy-cache directory;
omit it when ETL-SQL runs as Local System. On Unix, install as root and arrange the service identity or
service manager so it can read the root-owned bootstrap without making it group- or world-writable.

Enrollment is stored at:

- Windows: `%ProgramData%\ETL-SQL\Enterprise\enrollment.json`
- Linux/macOS: `/etc/etl-sql/enterprise/enrollment.json`

Windows grants control only to Local System and Administrators, plus read access to the optional service
identity. Unix writes the directory as `0700` and the file as `0600`. Every ETL-SQL executable checks this
fixed location before loading ordinary application configuration. If enrollment exists but is malformed,
uses unsafe permissions, has an unsupported schema, or contains an invalid endpoint or trust key, normal
startup fails closed.

Inspect status without exposing the key or certificate value:

```text
etl-sql enterprise status
```

Remove enrollment only from an elevated shell with explicit confirmation:

```text
etl-sql enterprise unenroll --yes
```

The unenrollment command can remove a malformed but still OS-protected bootstrap for disaster recovery.
If file permissions themselves are unsafe, repair ownership and permissions first; the command will not
trust or delete a broadly writable bootstrap. Removing enrollment returns the installation to standalone
mode. Organizations should monitor and restrict this administrative operation through endpoint management.

Enrollment protects the trusted ETL-SQL installation. It cannot stop a user from downloading, compiling,
or running unrelated software. Environments requiring mandatory enforcement must also restrict executable
launch through Windows Defender Application Control/AppLocker, managed software deployment, container
admission policy, or equivalent operating-system controls.

Filesystem approved-root enforcement canonicalizes paths and resolves symbolic links and junctions,
but a path cannot reliably identify every hard-link alias to the same underlying file. Treat hard-link
creation as an operating-system privilege boundary: deny it to ETL-SQL service accounts and protect
approved roots with ACLs or equivalent mount permissions. ETL-SQL does not claim hard-link containment
against a local administrator.

Local file and directory delete, move, rename, copy, archive extraction, and archive overwrite paths
use the filesystem policy authorizer immediately before mutation. On Windows and Linux, ETL-SQL
re-checks the final path reported by the opened file handle before destructive file mutation; on
platforms that cannot report a handle final path, enforcement remains best-effort by canonical path.
Remote filesystem connectors (`IRemoteFileSystem`, including SFTP, FTP, S3, Azure Blob, and
SharePoint-style object stores) are outside that OS-handle guarantee. ETL-SQL still applies connector
and path policy before dispatch, but remote delete, move, and rename semantics are governed by the
provider. Use provider IAM, scoped credentials, bucket/container policies, object versioning or object
lock where available, remote audit logs, and least-privilege service identities to contain provider-side
mutation risk.

## Related

- [Native Admin Services](native-admin-services.md)
- [Authoritative organization policy](organization-policy.md)
- [Central security events and SIEM delivery](security-events.md)
- [Platform administration](README.md)
