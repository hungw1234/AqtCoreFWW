# AqtCoreFW.Api — Multi-stage Docker build
# Base: .NET 10 Alpine | Non-root user | Port 8080

# ─── Stage 1: Build & Publish ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /app

# Override NuGet package root to Linux path — prevents MSB4018 caused by
# Windows-specific C:\Program Files paths in locally generated obj/*.nuget.g.props
ENV NUGET_PACKAGES=/root/.nuget/packages

# Copy MSBuild props + solution for layer-cached restore
COPY src/Directory.Packages.props src/Directory.Build.props ./
COPY src/AqtCoreFW.slnx ./

# Copy all .csproj files preserving directory structure for restore cache.
# Shell glob copies ALL module/shared/host projects in one layer.
COPY src/host/*/*.csproj                    ./host-projects/
COPY src/shared/*/*.csproj                  ./shared-projects/
COPY src/modules/*/*/*.csproj               ./module-projects/

# Restore .csproj structure from flat copies (dotnet restore needs correct paths)
RUN for f in host-projects/*.csproj; do \
      name=$(basename "$f" .csproj); \
      mkdir -p "host/$name" && mv "$f" "host/$name/"; \
    done && \
    for f in shared-projects/*.csproj; do \
      name=$(basename "$f" .csproj); \
      mkdir -p "shared/$name" && mv "$f" "shared/$name/"; \
    done && \
    for f in module-projects/*.csproj; do \
      name=$(basename "$f" .csproj); \
      dir=$(echo "$name" | sed 's/AqtCoreFW\.\([^.]*\).*/\1/' | tr '[:upper:]' '[:lower:]'); \
      mkdir -p "modules/$dir/$name" && mv "$f" "modules/$dir/$name/"; \
    done && \
    rm -rf host-projects shared-projects module-projects

# RID follows the build machine so arm64 hosts (Apple Silicon) get arm64 assemblies and native assets.
# A hard-coded linux-musl-x64 stamps PlatformTarget=x64, which an arm64 runtime cannot load (exit 139).
RUN case "$(uname -m)" in aarch64|arm64) echo linux-musl-arm64;; *) echo linux-musl-x64;; esac > /tmp/rid && \
    dotnet restore host/AqtCoreFW.Api/AqtCoreFW.Api.csproj --runtime "$(cat /tmp/rid)"

# Copy full source and publish
COPY src/ ./
RUN dotnet publish host/AqtCoreFW.Api/AqtCoreFW.Api.csproj \
        -c Release \
        -o /app/publish \
        --runtime "$(cat /tmp/rid)" \
        --self-contained false \
        /p:UseAppHost=false \
        /p:NuGetAudit=false

# ─── Stage 2: Runtime ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

# OCI labels for container metadata
LABEL org.opencontainers.image.title="AqtCoreFW API" \
      org.opencontainers.image.description="Vietnamese government case management API" \
      org.opencontainers.image.vendor="AQT" \
      org.opencontainers.image.source="https://github.com/thiennd212/AqtCoreFW"

# Install timezone data, CA certs, ICU libs, and fonts for Vietnamese PDF rendering
# ICU required by Microsoft.Data.SqlClient on Alpine (Globalization Invariant Mode not supported)
# fontconfig + noto-fonts ensure Vietnamese glyphs render in QuestPDF even without embedded fonts
RUN apk add --no-cache tzdata ca-certificates icu-libs fontconfig font-noto && \
    update-ca-certificates && fc-cache -f

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app

# Create non-root user for security compliance (NĐ85/TT12 hardening)
RUN addgroup -S appgroup && adduser -S appuser -G appgroup -H -D

# Copy published output from build stage
COPY --from=build --chown=appuser:appgroup /app/publish ./

# Read-only tmp directory for ASP.NET Core temp files
RUN mkdir -p /tmp/asp && chown appuser:appgroup /tmp/asp

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_GCConserveMemory=9 \
    DOTNET_EnableDiagnostics=0 \
    TMPDIR=/tmp/asp

# Health check — Docker/K8s can detect unhealthy containers
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health/ready || exit 1

USER appuser

ENTRYPOINT ["dotnet", "AqtCoreFW.Api.dll"]
