FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build-env
WORKDIR /source

# Copy only csprojes for restore
COPY SchemaEvolution/SchemaEvolution.csproj ./
# Restore as distinct layers
RUN dotnet restore SchemaEvolution.csproj
# Build and publish a release
COPY SchemaEvolution/ ./
RUN dotnet publish --no-restore --configuration Release -o /app SchemaEvolution.csproj

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0-noble-chiseled
WORKDIR /app
COPY --from=build-env /app .
ENTRYPOINT ["dotnet", "./SchemaEvolution.dll"]
