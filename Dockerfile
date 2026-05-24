# VetSystem API — multi-stage production image.
#
# Targets:
#   final    → the API runtime image (also hosts the in-process Hangfire worker, SignalR hub, sync
#              write path). This is what `api` runs in docker-compose.prod.yaml.
#   migrate  → a tiny image carrying an EF Core "migrations bundle" (a self-applying executable). The
#              one-shot `migrate` compose service runs it before the API starts, because auto-migration
#              is intentionally disabled in Program.cs (see vet-backend/CLAUDE.md "Important Rules").
#
# Build context is the vet-backend/ repo root. Layer order is csproj-first so `dotnet restore` is cached
# across source-only changes.

ARG DOTNET_VERSION=10.0

# ---------------------------------------------------------------------------
# Stage 1 — restore + publish the API, and emit the migrations bundle.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore against the project graph only (csproj + local tool manifest) for a cacheable layer.
COPY dotnet-tools.json ./
COPY src/VetSystem.Domain/VetSystem.Domain.csproj          src/VetSystem.Domain/
COPY src/VetSystem.Application/VetSystem.Application.csproj src/VetSystem.Application/
COPY src/VetSystem.Infrastructure/VetSystem.Infrastructure.csproj src/VetSystem.Infrastructure/
COPY src/VetSystem.API/VetSystem.API.csproj                src/VetSystem.API/
RUN dotnet restore src/VetSystem.API/VetSystem.API.csproj

# Now the source.
COPY src/ src/

# Publish the API (framework-dependent — the runtime image carries the shared framework).
RUN dotnet publish src/VetSystem.API/VetSystem.API.csproj \
        -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Emit a self-applying migrations bundle. `dotnet ef migrations bundle` builds the startup project and
# packs every migration into one executable that applies them against a --connection at runtime — no
# SDK or source needed on the VPS. Same --project/--startup-project pair the CLAUDE.md documents.
RUN dotnet tool restore \
 && dotnet ef migrations bundle \
        --project src/VetSystem.Infrastructure \
        --startup-project src/VetSystem.API \
        --configuration Release \
        --output /app/efbundle

# ---------------------------------------------------------------------------
# Stage 2 — the API runtime image.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

# libfontconfig1: QuestPDF (M12 PDF export) renders via SkiaSharp, which needs fontconfig present even
#   though the Arabic font is embedded in the assembly (libfreetype6 comes in as a dependency).
# curl: used by the container HEALTHCHECK (docker-compose.prod.yaml) to hit /health/live.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# Serilog rolls files into ./logs (relative to the content root); make it writable by the non-root
# user the base image ships (APP_UID 1654) before a named volume mounts over it.
RUN mkdir -p /app/logs && chown -R $APP_UID:$APP_UID /app/logs
USER $APP_UID

# Kestrel listens on 8080 inside the container; nginx terminates TLS in front of it.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "VetSystem.API.dll"]

# ---------------------------------------------------------------------------
# Stage 3 — the one-shot migrations runner.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrate
WORKDIR /app
COPY --from=build /app/efbundle ./efbundle
# The bundle rebuilds the DbContext from configuration, so it needs appsettings.json present (its
# placeholder connection is overridden by the --connection arg the compose `migrate` service passes).
COPY --from=build /app/publish/appsettings.json ./appsettings.json
USER $APP_UID
ENTRYPOINT ["./efbundle"]
