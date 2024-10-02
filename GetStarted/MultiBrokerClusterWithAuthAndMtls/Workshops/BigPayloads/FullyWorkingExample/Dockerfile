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
ENTRYPOINT ["dotnet", "./BigPayloads.dll"]
