# ========================================================
# Multi-stage Dockerfile for ATCIS Unified Tender Scraper
# Optimized for Coolify & Railway (.NET 8 + Python Scrapers)
# ========================================================

# Stage 1: Build & Restore using .NET 8 SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["ZimbabweTenderAPI.csproj", "./"]
RUN dotnet restore "ZimbabweTenderAPI.csproj"

# The Git repository root is already the .NET project directory.
COPY . .
RUN dotnet publish "ZimbabweTenderAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: ASP.NET Core 8.0 Runtime with Python 3 + Scraper Tools
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install Python 3, pip, curl, ca-certificates, and scraping dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && pip install --no-cache-dir --break-system-packages curl_cffi requests

# Copy compiled .NET output
COPY --from=build /app/publish .

# Copy scripts directory (checked by SupabaseScraperHost)
COPY scripts/ /scripts/
COPY scripts/ /app/scripts/

# Container environment defaults (credentials are injected at runtime)
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ScraperOnly=true
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV PORT=8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "ZimbabweTenderAPI.dll"]
