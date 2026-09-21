# ========================================================
# Multi-stage Dockerfile for ATCIS Tender Scraper
# Optimized for Railway (.NET 8 runtime)
# ========================================================

# Stage 1: Build & Restore using .NET 8 SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies first for caching
COPY ["ZimbabweTenderAPI.csproj", "./"]
RUN dotnet restore "ZimbabweTenderAPI.csproj"

# Copy remaining source code and publish release binary
COPY . .
RUN dotnet publish "ZimbabweTenderAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Minimal ASP.NET Core 8.0 Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Copy compiled output
COPY --from=build /app/publish .

# Container environment
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ScraperOnly=true
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Port is assigned dynamically by Railway via $PORT
EXPOSE 8080

ENTRYPOINT ["dotnet", "ZimbabweTenderAPI.dll"]
