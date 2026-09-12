# syntax=docker/dockerfile:1.7


# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
WORKDIR /src

# Project files first (for layer caching)
COPY ["Directory.Build.props", "global.json", "./"]
COPY ["LagersystemLVHome.sln", "./"]
COPY ["LagersystemLVHome/LagersystemLVHome.csproj", "LagersystemLVHome/packages.lock.json", "LagersystemLVHome/"]
COPY ["LagersystemLVHome.Domain/LagersystemLVHome.Domain.csproj", "LagersystemLVHome.Domain/packages.lock.json", "LagersystemLVHome.Domain/"]
COPY ["LagersystemLVHome.Data/LagersystemLVHome.Data.csproj", "LagersystemLVHome.Data/packages.lock.json", "LagersystemLVHome.Data/"]
COPY ["LagersystemLVHome.Application/LagersystemLVHome.Application.csproj", "LagersystemLVHome.Application/packages.lock.json", "LagersystemLVHome.Application/"]
COPY ["LagersystemLVHome.Infrastructure/LagersystemLVHome.Infrastructure.csproj", "LagersystemLVHome.Infrastructure/packages.lock.json", "LagersystemLVHome.Infrastructure/"]
COPY ["LagersystemLVHome.UnitTests/LagersystemLVHome.UnitTests.csproj", "LagersystemLVHome.UnitTests/packages.lock.json", "LagersystemLVHome.UnitTests/"]

ENV CI=true

# --locked-mode: the restore must match the committed packages.lock.json files exactly.
RUN dotnet restore "LagersystemLVHome/LagersystemLVHome.csproj" --locked-mode

# Copy remaining source
COPY . .

# Build + publish (includes static web assets)
RUN dotnet build "LagersystemLVHome/LagersystemLVHome.csproj" -c Release

RUN dotnet publish "LagersystemLVHome/LagersystemLVHome.csproj" \
    -c Release \
    -o /app/publish \
    --no-build \
    /p:UseAppHost=false \
    /p:StaticWebAssetsEnabled=true \
    /p:StaticWebAssetsCopyToOutput=true

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS runtime
WORKDIR /app

# Native dependencies for SkiaSharp (libfontconfig + freetype + ICU).
# Without these, libSkiaSharp.so fails to load with
# "libfontconfig.so.1: cannot open shared object file".
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libfreetype6 \
        fonts-liberation \
 && rm -rf /var/lib/apt/lists/*

# Create a non-root user
RUN getent group app || groupadd --system app \
 && id -u app 2>/dev/null || useradd --system --gid app --home-dir /app --shell /usr/sbin/nologin app \
 && mkdir -p /app/keys /app/data \
 && chown -R app:app /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=5000 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

# Copy publish output
COPY --from=build --chown=app:app /app/publish ./

USER app
EXPOSE 5000

ENTRYPOINT ["dotnet", "LagersystemLVHome.dll"]
