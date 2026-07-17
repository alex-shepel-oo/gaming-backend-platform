# Build context is backend/, so Directory.Build.props and
# Directory.Packages.props are reachable for restore. Build with:
#   docker build -f IdentityService/Dockerfile .
# from backend/, or context: ../backend / dockerfile: IdentityService/Dockerfile
# in docker-compose.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY IdentityService/IdentityService.csproj IdentityService/
RUN dotnet restore IdentityService/IdentityService.csproj

COPY IdentityService/ IdentityService/
RUN dotnet publish IdentityService/IdentityService.csproj -c Release -o /app --no-restore

# One-shot migration runner. Kept separate from the app image: with the
# service running as 2-10 replicas (see the HPA manifests planned for
# Kubernetes), migrating from inside the app's own startup would be a race
# between however many replicas boot at once. This stage runs once, applies
# migrations, and exits; identity-service only starts after it succeeds.
FROM build AS migrator
RUN dotnet tool install --global dotnet-ef --version 10.0.10
ENV PATH="$PATH:/root/.dotnet/tools"
WORKDIR /src/IdentityService
ENTRYPOINT ["dotnet", "ef", "database", "update"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "IdentityService.dll"]
