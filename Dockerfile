# syntax=docker/dockerfile:1.7

ARG DOTNET_VERSION=10.0

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Project files first (for layer caching)
COPY ["Directory.Build.props", "global.json", "./"]
COPY ["LagersystemLVHome.sln", "./"]
COPY ["LagersystemLVHome/LagersystemLVHome.csproj", "LagersystemLVHome/"]
COPY ["LagersystemLVHome.Domain/LagersystemLVHome.Domain.csproj", "LagersystemLVHome.Domain/"]
COPY ["LagersystemLVHome.Data/LagersystemLVHome.Data.csproj", "LagersystemLVHome.Data/"]
COPY ["LagersystemLVHome.Application/LagersystemLVHome.Application.csproj", "LagersystemLVHome.Application/"]
COPY ["LagersystemLVHome.Infrastructure/LagersystemLVHome.Infrastructure.csproj", "LagersystemLVHome.Infrastructure/"]
COPY ["LagersystemLVHome.UnitTests/LagersystemLVHome.UnitTests.csproj", "LagersystemLVHome.UnitTests/"]

ENV CI=true

RUN dotnet restore "LagersystemLVHome/LagersystemLVHome.csproj"

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
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
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
