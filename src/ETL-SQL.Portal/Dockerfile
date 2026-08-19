FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy all project files first for layer caching
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.Config", "."]
COPY ["build/signing/etl-sql.snk", "build/signing/"]
COPY ["THIRD-PARTY-NOTICES.md", "/"]
COPY ["src/ETL-SQL.Core/ETL-SQL.Core.csproj", "ETL-SQL.Core/"]
COPY ["src/ETL-SQL.Engine/ETL-SQL.Engine.csproj", "ETL-SQL.Engine/"]
COPY ["src/ETL-SQL.Connectors.Common/ETL-SQL.Connectors.Common.csproj", "ETL-SQL.Connectors.Common/"]
COPY ["src/ETL-SQL.Connectors.Files/ETL-SQL.Connectors.Files.csproj", "ETL-SQL.Connectors.Files/"]
COPY ["src/ETL-SQL.Connectors.Databases/ETL-SQL.Connectors.Databases.csproj", "ETL-SQL.Connectors.Databases/"]
COPY ["src/ETL-SQL.Connectors.Cloud/ETL-SQL.Connectors.Cloud.csproj", "ETL-SQL.Connectors.Cloud/"]
COPY ["src/ETL-SQL.Connectors.Remote/ETL-SQL.Connectors.Remote.csproj", "ETL-SQL.Connectors.Remote/"]
COPY ["src/ETL-SQL.Connectors.Messaging/ETL-SQL.Connectors.Messaging.csproj", "ETL-SQL.Connectors.Messaging/"]
COPY ["src/ETL-SQL.Connectors/ETL-SQL.Connectors.csproj", "ETL-SQL.Connectors/"]
COPY ["src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj", "ETL-SQL.Orchestrator/"]
COPY ["src/ETL-SQL.ReportBuilder/ETL-SQL.ReportBuilder.csproj", "ETL-SQL.ReportBuilder/"]
COPY ["src/ETL-SQL.Analysis/ETL-SQL.Analysis.csproj", "ETL-SQL.Analysis/"]
COPY ["src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj", "ETL-SQL.Reporting/"]
COPY ["src/ETL-SQL.ReportHosting/ETL-SQL.ReportHosting.csproj", "ETL-SQL.ReportHosting/"]
COPY ["src/ETL-SQL.ReportPlayer/ETL-SQL.ReportPlayer.csproj", "ETL-SQL.ReportPlayer/"]
COPY ["src/ETL-SQL.App/ETL-SQL.App.csproj", "ETL-SQL.App/"]
COPY ["src/ETL-SQL.TUI/ETL-SQL.TUI.csproj", "ETL-SQL.TUI/"]
COPY ["src/ETL-SQL.Portal.Data/ETL-SQL.Portal.Data.csproj", "ETL-SQL.Portal.Data/"]
COPY ["src/ETL-SQL.Portal.Migrations.Postgres/ETL-SQL.Portal.Migrations.Postgres.csproj", "ETL-SQL.Portal.Migrations.Postgres/"]
COPY ["src/ETL-SQL.Portal/ETL-SQL.Portal.csproj", "ETL-SQL.Portal/"]
COPY ["src/ETL-SQL.Infrastructure.Docker/ETL-SQL.Infrastructure.Docker.csproj", "ETL-SQL.Infrastructure.Docker/"]
COPY ["src/ETL-SQL.Infrastructure.Logging/ETL-SQL.Infrastructure.Logging.csproj", "ETL-SQL.Infrastructure.Logging/"]
COPY ["src/ETL-SQL.Infrastructure.Sqlite/ETL-SQL.Infrastructure.Sqlite.csproj", "ETL-SQL.Infrastructure.Sqlite/"]
COPY ["src/ETL-SQL.Gateway/ETL-SQL.Gateway.csproj", "ETL-SQL.Gateway/"]


RUN dotnet restore "ETL-SQL.Portal/ETL-SQL.Portal.csproj" -r linux-x64

# Copy the rest of the source
COPY src/ .

COPY docs/ /docs/
COPY snippets/ /snippets/

WORKDIR "/src/ETL-SQL.Portal"
RUN dotnet build "ETL-SQL.Portal.csproj" -c Release -r linux-x64 --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish "ETL-SQL.Portal.csproj" -c Release -r linux-x64 --self-contained false --no-restore -o /app/publish /p:UseAppHost=false /p:PublishSingleFile=false /p:PublishReadyToRun=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SkiaSharp (PDF chart rendering via Svg.Skia) needs fontconfig + a font at runtime;
# the native libSkiaSharp.so links against fontconfig and chart labels need a real
# font. (The .so itself is supplied by the SkiaSharp.NativeAssets.Linux package.)
# Without these, PDF export fails with
# "The type initializer for 'SkiaSharp.SKImageInfo' threw an exception".
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Create directories
RUN mkdir -p Reports Snapshots data

EXPOSE 5002
ENV ASPNETCORE_URLS=http://+:5002
ENV ETLSQL_SECURITY_EVENT_OUTBOX_PATH=/app/data/security-events.db
ENTRYPOINT ["dotnet", "ETL-SQL-Portal.dll"]
