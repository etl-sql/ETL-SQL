FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy all project files first for layer caching
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.Config", "."]
COPY ["THIRD-PARTY-NOTICES.md", "/"]
COPY ["src/ETL-SQL.Core/ETL-SQL.Core.csproj", "ETL-SQL.Core/"]
COPY ["src/ETL-SQL.Engine/ETL-SQL.Engine.csproj", "ETL-SQL.Engine/"]
COPY ["src/ETL-SQL.Connectors/ETL-SQL.Connectors.csproj", "ETL-SQL.Connectors/"]
COPY ["src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj", "ETL-SQL.Orchestrator/"]
COPY ["src/ETL-SQL.Orchestrator.Service/ETL-SQL.Orchestrator.Service.csproj", "ETL-SQL.Orchestrator.Service/"]
COPY ["src/ETL-SQL.Analysis/ETL-SQL.Analysis.csproj", "ETL-SQL.Analysis/"]
COPY ["src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj", "ETL-SQL.Reporting/"]
COPY ["src/ETL-SQL.ReportBuilder/ETL-SQL.ReportBuilder.csproj", "ETL-SQL.ReportBuilder/"]

RUN dotnet restore "ETL-SQL.Orchestrator.Service/ETL-SQL.Orchestrator.Service.csproj"

# Copy the rest of the source
COPY src/ .

WORKDIR "/src/ETL-SQL.Orchestrator.Service"
RUN dotnet build "ETL-SQL.Orchestrator.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ETL-SQL.Orchestrator.Service.csproj" -c Release -o /app/publish /p:UseAppHost=false
WORKDIR "/src/ETL-SQL.App"
RUN dotnet publish "ETL-SQL.App.csproj" -c Release -o /app/publish /p:UseAppHost=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SkiaSharp (PDF chart rendering via Svg.Skia) needs fontconfig + a font at runtime
# for scheduled report/PDF exports. (The native libSkiaSharp.so itself comes from
# the SkiaSharp.NativeAssets.Linux package.)
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Create runtime data directories
RUN mkdir -p logs Snapshots Reports data Sessions

EXPOSE 5001
ENV ASPNETCORE_URLS=http://+:5001
ENTRYPOINT ["dotnet", "ETL-SQL-Service.dll"]
