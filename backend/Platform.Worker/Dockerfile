# Build context is backend/, so Directory.Build.props and
# Directory.Packages.props are reachable for restore. Build with:
#   docker build -f Platform.Worker/Dockerfile .
# from backend/, or context: ../backend / dockerfile: Platform.Worker/Dockerfile
# in docker-compose.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY Platform.Worker/Platform.Worker.csproj Platform.Worker/
RUN dotnet restore Platform.Worker/Platform.Worker.csproj

COPY Platform.Worker/ Platform.Worker/
RUN dotnet publish Platform.Worker/Platform.Worker.csproj -c Release -o /app --no-restore

# No migrator stage here: the worker only deletes rows in tables that
# identity-service and economy-service already own and migrate.
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Platform.Worker.dll"]
