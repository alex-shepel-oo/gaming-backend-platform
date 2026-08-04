# Build context is backend/, so Directory.Build.props and
# Directory.Packages.props are reachable for restore. Build with:
#   docker build -f EconomyService/Dockerfile .
# from backend/, or context: ../backend / dockerfile: EconomyService/Dockerfile
# in docker-compose.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY EconomyService/EconomyService.csproj EconomyService/
COPY BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj BuildingBlocks.Messaging/
COPY BuildingBlocks.Telemetry/BuildingBlocks.Telemetry.csproj BuildingBlocks.Telemetry/
RUN dotnet restore EconomyService/EconomyService.csproj

COPY EconomyService/ EconomyService/
COPY BuildingBlocks.Messaging/ BuildingBlocks.Messaging/
COPY BuildingBlocks.Telemetry/ BuildingBlocks.Telemetry/
RUN dotnet publish EconomyService/EconomyService.csproj -c Release -o /app --no-restore

# One-shot migration runner, kept separate from the app image for the same
# reason as identity-service: with the service running multiple replicas,
# migrating from inside app startup would race between however many
# replicas boot at once. This stage runs once, applies migrations, and exits.
FROM build AS migrator
RUN dotnet tool install --global dotnet-ef --version 10.0.10
ENV PATH="$PATH:/root/.dotnet/tools"
WORKDIR /src/EconomyService
ENTRYPOINT ["dotnet", "ef", "database", "update"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "EconomyService.dll"]
