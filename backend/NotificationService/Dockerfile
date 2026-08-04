# Build context is backend/, so Directory.Build.props and
# Directory.Packages.props are reachable for restore. Build with:
#   docker build -f NotificationService/Dockerfile .
# from backend/, or context: ../backend / dockerfile: NotificationService/Dockerfile
# in docker-compose.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY NotificationService/NotificationService.csproj NotificationService/
COPY BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj BuildingBlocks.Messaging/
COPY BuildingBlocks.Telemetry/BuildingBlocks.Telemetry.csproj BuildingBlocks.Telemetry/
RUN dotnet restore NotificationService/NotificationService.csproj

COPY NotificationService/ NotificationService/
COPY BuildingBlocks.Messaging/ BuildingBlocks.Messaging/
COPY BuildingBlocks.Telemetry/ BuildingBlocks.Telemetry/
RUN dotnet publish NotificationService/NotificationService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "NotificationService.dll"]
