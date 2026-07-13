FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy all project files first for layer caching
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.Config", "."]
COPY ["etl-sql.snk", "."]
COPY ["THIRD-PARTY-NOTICES.md", "/"]
COPY ["src/ETL-SQL.Core/ETL-SQL.Core.csproj", "ETL-SQL.Core/"]
COPY ["src/ETL-SQL.Engine/ETL-SQL.Engine.csproj", "ETL-SQL.Engine/"]
COPY ["src/ETL-SQL.Connectors/ETL-SQL.Connectors.csproj", "ETL-SQL.Connectors/"]
COPY ["src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj", "ETL-SQL.Orchestrator/"]
COPY ["src/ETL-SQL.Orchestrator.Service/ETL-SQL.Orchestrator.Service.csproj", "ETL-SQL.Orchestrator.Service/"]
COPY ["src/ETL-SQL.Analysis/ETL-SQL.Analysis.csproj", "ETL-SQL.Analysis/"]
COPY ["src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj", "ETL-SQL.Reporting/"]
COPY ["src/ETL-SQL.ReportBuilder/ETL-SQL.ReportBuilder.csproj", "ETL-SQL.ReportBuilder/"]
COPY ["src/ETL-SQL.App/ETL-SQL.App.csproj", "ETL-SQL.App/"]
COPY ["src/ETL-SQL.TUI/ETL-SQL.TUI.csproj", "ETL-SQL.TUI/"]

RUN dotnet restore "ETL-SQL.Orchestrator.Service/ETL-SQL.Orchestrator.Service.csproj" -r linux-x64
RUN dotnet restore "ETL-SQL.App/ETL-SQL.App.csproj" -r linux-x64

# Copy the rest of the source
COPY src/ .
COPY deploy/docker/docker-compose.ha.yml /deploy/docker/docker-compose.ha.yml
COPY deploy/docker/environment-ha.env.example /deploy/docker/environment-ha.env.example
COPY capacity-results/workloads/postgres-ha-sustained.workload.json /capacity-results/workloads/postgres-ha-sustained.workload.json
COPY certification-results/ha-large-job-soak-scenarios.json /certification-results/ha-large-job-soak-scenarios.json
COPY certification-results/ha-fault-injection-matrix.json /certification-results/ha-fault-injection-matrix.json

WORKDIR "/src/ETL-SQL.Orchestrator.Service"
RUN dotnet build "ETL-SQL.Orchestrator.Service.csproj" -c Release -r linux-x64 --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish "ETL-SQL.Orchestrator.Service.csproj" -c Release -r linux-x64 --self-contained false --no-restore -o /app/publish /p:UseAppHost=false /p:PublishSingleFile=false /p:PublishReadyToRun=false
WORKDIR "/src/ETL-SQL.App"
RUN dotnet publish "ETL-SQL.App.csproj" -c Release -r linux-x64 --self-contained false --no-restore -o /app/publish /p:UseAppHost=true /p:PublishSingleFile=false /p:PublishReadyToRun=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SkiaSharp (PDF chart rendering via Svg.Skia) needs fontconfig + a font at runtime
# for scheduled report/PDF exports. (The native libSkiaSharp.so itself comes from
# the SkiaSharp.NativeAssets.Linux package.)
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Create runtime data directories
RUN mkdir -p logs Snapshots Reports data Sessions

EXPOSE 5001
ENV ASPNETCORE_URLS=http://+:5001
ENTRYPOINT ["dotnet", "ETL-SQL-Service.dll"]
