# Build context is backend/, so Directory.Build.props and
# Directory.Packages.props are reachable for restore. Build with:
#   docker build -f EmailService/Dockerfile .
# from backend/, or context: ../backend / dockerfile: EmailService/Dockerfile
# in docker-compose.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY EmailService/EmailService.csproj EmailService/
COPY BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj BuildingBlocks.Messaging/
COPY BuildingBlocks.Telemetry/BuildingBlocks.Telemetry.csproj BuildingBlocks.Telemetry/
RUN dotnet restore EmailService/EmailService.csproj

COPY EmailService/ EmailService/
COPY BuildingBlocks.Messaging/ BuildingBlocks.Messaging/
COPY BuildingBlocks.Telemetry/ BuildingBlocks.Telemetry/
RUN dotnet publish EmailService/EmailService.csproj -c Release -o /app --no-restore

# No migrator stage here: EmailService has no database.

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "EmailService.dll"]
