# GEMINI-001 — Build a Runnable Sandbox Worker Image

## Objective

Add a purpose-built Linux container image that runs the ETL-SQL CLI as a single sandbox workload.
The current Portal and Orchestrator images are service hosts; although the Orchestrator build also
publishes the CLI, neither image is a clear, independently tested worker-image contract.

This task supplies the immutable image input required by later Docker Desktop and Hardened-runtime
tests. It does **not** certify a sandbox runtime or close a TODO item.

## Read first

- `AGENTS.md`
- `src/ETL-SQL.App/ETL-SQL.App.csproj`
- `src/ETL-SQL.Orchestrator.Service/Dockerfile`
- `src/ETL-SQL.Portal/Dockerfile`
- `.dockerignore`
- `tests/ETL-SQL.Portal.Tests/ContainerBuildContextTests.cs`
- `src/ETL-SQL.Orchestrator/Execution/DockerSandboxExecutionProvider.cs`

## Required design

Create `src/ETL-SQL.App/Dockerfile.sandbox` with these properties:

- Multi-stage .NET 10 build using repository central package management and signing inputs.
- Publishes `ETL-SQL.App` for `linux-x64`, framework-dependent, without single-file or ReadyToRun.
- Final image contains the .NET 10 runtime needed by the CLI, not the ASP.NET service host unless the
  CLI demonstrably requires it.
- Exposes one stable executable path, `/app/etl-sql`, usable as the provider `Entrypoint`.
- Runs as numeric non-root user/group `65532:65532` by default.
- Has no service port, daemon entrypoint, Docker socket, build SDK, package manager cache, repository
  source, or writable application directory in the final layer.
- Includes required license notices. Do not add a third-party package.
- Does not bake scripts, credentials, keys, datasets, sessions, or mutable environment configuration
  into the image.
- Uses `ENTRYPOINT ["/app/etl-sql"]`; arguments remain supplied by the sandbox provider.

If a native runtime dependency is truly needed, use the same FOSS system packages already justified
by an existing image and explain why. Do not broaden the image opportunistically.

## Verification helpers

Add a PowerShell build/check script under `scripts/` following existing script style. It must:

1. build the image with a local temporary tag;
2. obtain the resulting content image ID (`sha256:...`);
3. run `/app/etl-sql --version` as the image's default non-root user;
4. inspect and fail if the configured user is blank or root;
5. print the exact image ID for the next packet;
6. avoid pushing to a registry or changing global Docker configuration.

The script must not describe a local image ID as a registry `RepoDigest`. Name the distinction
correctly in output and docs.

## Tests

Extend `ContainerBuildContextTests` so this Dockerfile's `COPY` sources are checked against
`.dockerignore`. Add a focused source-contract test that checks the non-root user and stable
entrypoint without asserting fragile whitespace.

Run at minimum:

```powershell
dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj `
  --filter "FullyQualifiedName~ContainerBuildContextTests" -m:1
```

Then run the new Docker build/check script on Docker Desktop. Report the built image ID and command
output, but do not store generated image layers or evidence directories in Git.

## Prohibited shortcuts

- Do not reuse `etl-sql/orchestrator:latest` as the final contract.
- Do not run as root because mounted directories are inconvenient.
- Do not copy the entire repository into the final layer.
- Do not use a mutable tag as certification identity.
- Do not change sandbox isolation-tier validation.
- Do not edit `TODO.md` or make a Hardened-runtime claim.

## Acceptance criteria

- A clean checkout can build the worker image.
- The image runs `--version` as `65532:65532`.
- Its immutable local image ID is printed and unambiguously labeled.
- Build-context tests include the new Dockerfile and pass.
- The change is one commit with no unrelated files.
