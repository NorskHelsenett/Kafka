# Build health check app
FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build-health-checker
WORKDIR /source
# Because this doesn't really ever change, don't bother with spending lines and layers on optimizing for cache hits during development
COPY HealthChecker/ ./
RUN dotnet publish --configuration Release --no-self-contained -o /app

# Build the BigPayloads app
FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build-env
WORKDIR /source

# Copy only csprojes for restore
COPY BigPayloads/BigPayloads.csproj ./
# Restore as distinct layers
RUN dotnet restore BigPayloads.csproj
# Build and publish a release
COPY BigPayloads/ ./
RUN dotnet publish --no-restore --configuration Release -o /app BigPayloads.csproj

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble-chiseled-composite
WORKDIR /app
COPY --from=build-env /app .
COPY --from=build-health-checker /app /HealthChecker/
ENTRYPOINT ["dotnet", "./BigPayloads.dll"]
