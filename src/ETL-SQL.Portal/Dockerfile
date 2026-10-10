FROM node:22-bookworm-slim AS browser-assets
WORKDIR /repo
COPY scripts/typecheck/package.json scripts/typecheck/package-lock.json scripts/typecheck/.npmrc ./scripts/typecheck/
RUN npm ci --prefix scripts/typecheck --no-audit --no-fund
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.Config tsconfig.browser-build.json ./
COPY LICENSE.md NOTICE.md THIRD-PARTY-NOTICES.md THIRD-PARTY-INVENTORY.md ./
COPY scripts/ ./scripts/
COPY types/ ./types/
COPY src/ ./src/
RUN node scripts/sync-assets.js && node scripts/sync-assets.js -Check

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Keep preparation inputs and generated outputs together for .NET freshness verification.
COPY --from=browser-assets /repo/ ./
COPY build/signing/etl-sql.snk ./build/signing/
COPY deploy/ ./deploy/
COPY docs/ ./docs/
COPY snippets/ ./snippets/

RUN dotnet restore "src/ETL-SQL.Portal/ETL-SQL.Portal.csproj" -r linux-x64

WORKDIR "/src/src/ETL-SQL.Portal"
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
