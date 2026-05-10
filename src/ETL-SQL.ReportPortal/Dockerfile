FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy all project files first for layer caching
COPY ["Directory.Build.props", "."]
COPY ["src/ETL-SQL.Core/ETL-SQL.Core.csproj", "ETL-SQL.Core/"]
COPY ["src/ETL-SQL.Engine/ETL-SQL.Engine.csproj", "ETL-SQL.Engine/"]
COPY ["src/ETL-SQL.Connectors/ETL-SQL.Connectors.csproj", "ETL-SQL.Connectors/"]
COPY ["src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj", "ETL-SQL.Orchestrator/"]
COPY ["src/ETL-SQL.ReportBuilder/ETL-SQL.ReportBuilder.csproj", "ETL-SQL.ReportBuilder/"]
COPY ["src/ETL-SQL.Analysis/ETL-SQL.Analysis.csproj", "ETL-SQL.Analysis/"]
COPY ["src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj", "ETL-SQL.Reporting/"]
COPY ["src/ETL-SQL.ReportPlayer/ETL-SQL.ReportPlayer.csproj", "ETL-SQL.ReportPlayer/"]
COPY ["src/ETL-SQL.App/ETL-SQL.App.csproj", "ETL-SQL.App/"]
COPY ["src/ETL-SQL.TUI/ETL-SQL.TUI.csproj", "ETL-SQL.TUI/"]
COPY ["src/ETL-SQL.ReportPortal/ETL-SQL.ReportPortal.csproj", "ETL-SQL.ReportPortal/"]

RUN dotnet restore "ETL-SQL.ReportPortal/ETL-SQL.ReportPortal.csproj"

# Copy the rest of the source
COPY src/ .

WORKDIR "/src/ETL-SQL.ReportPortal"
RUN dotnet build "ETL-SQL.ReportPortal.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ETL-SQL.ReportPortal.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create directories
RUN mkdir -p Reports Snapshots

EXPOSE 5002
ENV ASPNETCORE_URLS=http://+:5002
ENTRYPOINT ["dotnet", "ETL-SQL-Portal.dll"]
