FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.Config", "."]
COPY ["build/signing/etl-sql.snk", "build/signing/"]
COPY ["src/", "src/"]
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
