# ========================================================
# Multi-stage Dockerfile for ATCIS Unified Tender Scraper
# Optimized for Coolify Root Deployment (.NET 8 + Python Scrapers)
# ========================================================

# Stage 1: Build & Restore .NET Application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["ZimbabweTenderAPI/ZimbabweTenderAPI.csproj", "ZimbabweTenderAPI/"]
RUN dotnet restore "ZimbabweTenderAPI/ZimbabweTenderAPI.csproj"

COPY ZimbabweTenderAPI/ ZimbabweTenderAPI/
WORKDIR /src/ZimbabweTenderAPI
RUN dotnet publish "ZimbabweTenderAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime Container (.NET 8 + Python 3 with curl_cffi for portal scrapers)
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

# Copy compiled .NET application
COPY --from=build /app/publish .

# Copy scripts folder to both /scripts and /app/scripts
COPY scripts/ /scripts/
COPY scripts/ /app/scripts/

# Environment defaults for Scraper-only daemon
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ScraperOnly=true
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV PORT=8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "ZimbabweTenderAPI.dll"]
