FROM node:22-bookworm-slim AS browser-assets
WORKDIR /repo
COPY scripts/typecheck/package.json scripts/typecheck/package-lock.json ./scripts/typecheck/
RUN npm ci --prefix scripts/typecheck
COPY scripts/compile-browser.mjs scripts/sync-assets.js ./scripts/
COPY scripts/lib/browser-asset-inputs.cjs ./scripts/lib/
COPY tsconfig.browser-build.json ./
COPY types/ ./types/
COPY src/ETL-SQL.ReportRuntime/Resources/ ./src/ETL-SQL.ReportRuntime/Resources/
COPY src/ETL-SQL.Portal/BrowserSources/ ./src/ETL-SQL.Portal/BrowserSources/
COPY src/ETL-SQL.Portal/wwwroot/ ./src/ETL-SQL.Portal/wwwroot/
RUN node scripts/sync-assets.js

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.Config", "."]
COPY ["build/signing/etl-sql.snk", "build/signing/"]
COPY ["src/", "src/"]
COPY ["tools/lean-worker-experiment/", "tools/lean-worker-experiment/"]
COPY ["docs/", "docs/"]
COPY ["snippets/", "snippets/"]
# Generated assets and their verification manifest come from the build-only Node stage.
COPY --from=browser-assets /repo/src/ ./src/
COPY --from=browser-assets /repo/types/ ./types/
COPY --from=browser-assets /repo/tsconfig.browser-build.json ./
COPY --from=browser-assets /repo/scripts/compile-browser.mjs /repo/scripts/sync-assets.js ./scripts/
COPY --from=browser-assets /repo/scripts/lib/browser-asset-inputs.cjs ./scripts/lib/
COPY --from=browser-assets /repo/scripts/typecheck/package.json /repo/scripts/typecheck/package-lock.json ./scripts/typecheck/
COPY --from=browser-assets /repo/artifacts/browser-assets.props /repo/artifacts/browser-assets.json ./artifacts/

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
