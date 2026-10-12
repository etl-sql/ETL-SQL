FROM node:22-bookworm-slim AS browser-assets
WORKDIR /repo
COPY scripts/typecheck/package.json scripts/typecheck/package-lock.json scripts/typecheck/.npmrc ./scripts/typecheck/
RUN npm ci --prefix scripts/typecheck --no-audit --no-fund
# The same inputs as the shipped images: preparation fingerprints every one of them, and the .NET
# stage rejects a manifest computed from a different set.
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.Config tsconfig.browser-build.json ./
COPY LICENSE.md NOTICE.md THIRD-PARTY-NOTICES.md THIRD-PARTY-INVENTORY.md ./
COPY scripts/ ./scripts/
COPY types/ ./types/
COPY src/ ./src/
RUN node scripts/sync-assets.js && node scripts/sync-assets.js -Check

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src

# Keep preparation inputs and generated outputs together for .NET freshness verification.
COPY --from=browser-assets /repo/ ./
COPY ["build/signing/etl-sql.snk", "build/signing/"]
COPY ["tools/lean-worker-experiment/", "tools/lean-worker-experiment/"]
COPY ["docs/", "docs/"]
COPY ["snippets/", "snippets/"]

RUN dotnet publish "tools/lean-worker-experiment/ETL-SQL.Worker.csproj" -c Release -r linux-x64 \
    --self-contained false -o /app/publish \
    /p:UseAppHost=true /p:PublishSingleFile=false /p:PublishReadyToRun=false \
    /p:NuGetAudit=false /p:TreatWarningsAsErrors=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish .
ENV ETLSQL_SECURITY_EVENT_OUTBOX_PATH=/tmp/security-events.db
ENV Logging__AppLog__Directory=/tmp/logs
USER 65532:65532
ENTRYPOINT ["/app/etl-sql-worker"]
